using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KKAPI.Studio;
using Studio;
using UnityEngine;

namespace MakerBlendShapeSync
{
    internal static class StudioPoseEditorBridge
    {
        private const float SyncTimeout = 5f;
        private const float StableSyncDuration = 0.8f;
        private const float SyncRetryInterval = 0.2f;

        private static readonly Dictionary<int, SyncState> SyncStates =
            new Dictionary<int, SyncState>();

        internal static void Init(Harmony harmony)
        {
            PatchPostfix(harmony,
                AccessTools.Method(typeof(OCIChar), "ChangeChara", new[] { typeof(string) }),
                nameof(OCIChar_ChangeChara_Postfix),
                "OCIChar.ChangeChara");

            PatchPostfix(harmony,
                AccessTools.Method(typeof(OCIChar), "LoadClothesFile", new[] { typeof(string) }),
                nameof(OCIChar_LoadClothesFile_Postfix),
                "OCIChar.LoadClothesFile");

            PatchPostfix(harmony,
                AccessTools.Method(typeof(OCIChar), "SetCoordinateInfo",
                    new[] { typeof(ChaFileDefine.CoordinateType), typeof(bool) }),
                nameof(OCIChar_SetCoordinateInfo_Postfix),
                "OCIChar.SetCoordinateInfo");
        }

        internal static void ScheduleForCharacter(ChaControl chaControl)
        {
            if (!StudioAPI.InsideStudio || chaControl == null)
                return;

            try
            {
                Schedule(chaControl.GetOCIChar());
            }
            catch (Exception ex)
            {
                MakerBlendShapeSyncPlugin.Log?.LogDebug(
                    $"Could not find OCIChar for Studio sync: {ex.Message}");
            }
        }

        private static void PatchPostfix(Harmony harmony, MethodBase original,
            string patchMethodName, string displayName)
        {
            if (original == null)
            {
                MakerBlendShapeSyncPlugin.Log?.LogWarning(
                    $"Could not patch {displayName}; PoseEditor synchronization for this event is disabled.");
                return;
            }

            harmony.Patch(original,
                postfix: new HarmonyMethod(typeof(StudioPoseEditorBridge), patchMethodName));
        }

        private static void OCIChar_ChangeChara_Postfix(OCIChar __instance)
        {
            Schedule(__instance);
        }

        private static void OCIChar_LoadClothesFile_Postfix(OCIChar __instance)
        {
            Schedule(__instance);
        }

        private static void OCIChar_SetCoordinateInfo_Postfix(OCIChar __instance)
        {
            Schedule(__instance);
        }

        internal static void Schedule(OCIChar ociChar)
        {
            if (!StudioAPI.InsideStudio || ociChar?.charInfo == null)
                return;

            var controller = ociChar.charInfo.GetComponent<BlendShapeSyncController>();
            if (controller == null)
                return;

            RemoveDeadStates();

            int key = ociChar.charInfo.GetInstanceID();
            if (!SyncStates.TryGetValue(key, out SyncState state) ||
                state.Controller != controller)
            {
                state = new SyncState
                {
                    Key = key,
                    OciChar = ociChar,
                    Controller = controller
                };
                SyncStates[key] = state;
            }
            else
            {
                state.OciChar = ociChar;
            }

            if (state.Coroutine != null)
                controller.StopCoroutine(state.Coroutine);

            ReleasePreviousCoordinateEntries(state, controller.CurrentCoordinateIndex);
            state.Coroutine = controller.StartCoroutine(SyncWhenStable(state));
        }

        private static IEnumerator SyncWhenStable(SyncState state)
        {
            float startedAt = Time.realtimeSinceStartup;
            float firstSuccessfulSyncAt = -1f;
            float nextSyncAt = 0f;
            int previousFingerprint = BuildTopologyFingerprint(state.OciChar);
            int stableFrames = 0;

            while (Time.realtimeSinceStartup - startedAt < SyncTimeout)
            {
                yield return new WaitForEndOfFrame();

                if (!IsStateAlive(state))
                    yield break;

                int fingerprint = BuildTopologyFingerprint(state.OciChar);
                if (fingerprint != previousFingerprint)
                {
                    previousFingerprint = fingerprint;
                    stableFrames = 0;
                    firstSuccessfulSyncAt = -1f;
                    nextSyncAt = 0f;
                    continue;
                }

                stableFrames++;
                if (stableFrames < 2)
                    continue;

                float now = Time.realtimeSinceStartup;
                if (now >= nextSyncAt)
                {
                    if (TrySynchronize(state) && firstSuccessfulSyncAt < 0f)
                        firstSuccessfulSyncAt = now;
                    nextSyncAt = now + SyncRetryInterval;
                }

                if (firstSuccessfulSyncAt >= 0f &&
                    now - firstSuccessfulSyncAt >= StableSyncDuration)
                {
                    TrySynchronize(state);
                    break;
                }
            }

            if (IsStateAlive(state))
            {
                TrySynchronize(state);
                state.Coroutine = null;
            }
        }

        private static bool TrySynchronize(SyncState state)
        {
            try
            {
                var controller = state.Controller;
                controller.ApplyCurrentCoordinate();

                object editor = FindBlendShapesEditor(state.OciChar);
                if (editor == null)
                    return false;

                Type editorType = editor.GetType();
                MethodInfo refresh = FindMethodInHierarchy(editorType,
                    "RefreshSkinnedMeshRendererList", Type.EmptyTypes);
                MethodInfo setWeight = FindMethodInHierarchy(editorType,
                    "SetBlendShapeWeight",
                    new[] { typeof(SkinnedMeshRenderer), typeof(int), typeof(float) });
                MethodInfo apply = FindMethodInHierarchy(editorType,
                    "ApplyBlendShapeWeights", Type.EmptyTypes);
                if (refresh == null || setWeight == null || apply == null)
                    return false;

                refresh.Invoke(editor, null);

                int coordinate = controller.CurrentCoordinateIndex;
                var activeRecords = controller.Records.Where(x =>
                    x.TargetScope == BlendShapeTargetScope.Character ||
                    (x.TargetScope == BlendShapeTargetScope.CharacterCoordinate &&
                     x.Coordinate == coordinate) ||
                    ((x.TargetScope == BlendShapeTargetScope.Clothing ||
                      x.TargetScope == BlendShapeTargetScope.Accessory) &&
                     x.Coordinate == coordinate)).ToList();

                var targets = new List<SyncTarget>();
                var activeIds = new HashSet<string>();
                foreach (var record in activeRecords)
                {
                    var renderer = BlendShapeUtilities.FindRenderer(
                        controller.ChaControl.transform, record);
                    if (renderer == null || renderer.sharedMesh == null)
                        continue;

                    int index = BlendShapeUtilities.FindBlendShapeIndex(renderer, record.ShapeName);
                    if (index < 0)
                        continue;

                    string id = MakeRecordId(record);
                    activeIds.Add(id);
                    targets.Add(new SyncTarget
                    {
                        Id = id,
                        Record = record,
                        Renderer = renderer,
                        BlendShapeIndex = index
                    });
                }

                CleanupInactiveOwnedEntries(state, activeIds);

                int imported = 0;
                foreach (var target in targets)
                {
                    if (ImportTarget(state, editor, setWeight, target))
                        imported++;
                }

                apply.Invoke(editor, null);
                MakerBlendShapeSyncPlugin.Log?.LogDebug(
                    $"Synchronized Maker blendshapes with PoseEditor: {imported} imported, " +
                    $"{targets.Count - imported} preserved or pending.");
                return true;
            }
            catch (Exception ex)
            {
                MakerBlendShapeSyncPlugin.Log?.LogDebug(
                    $"PoseEditor bridge synchronization failed: {ex.Message}");
                return false;
            }
        }

        private static bool ImportTarget(SyncState state, object editor,
            MethodInfo setWeight, SyncTarget target)
        {
            object blendRenderer = GetBlendRenderer(editor, target.Renderer);
            if (blendRenderer == null)
                return false;

            for (int i = state.OwnedEntries.Count - 1; i >= 0; i--)
            {
                var oldEntry = state.OwnedEntries[i];
                if (oldEntry.Id != target.Id ||
                    ReferenceEquals(oldEntry.BlendRenderer, blendRenderer))
                    continue;

                RemoveEntryIfStillOwned(oldEntry);
                state.OwnedEntries.RemoveAt(i);
            }

            IDictionary dirtyBlends = GetDirtyBlendDictionary(blendRenderer);
            if (dirtyBlends == null)
                return false;

            object existingData = dirtyBlends.Contains(target.Record.ShapeName)
                ? dirtyBlends[target.Record.ShapeName]
                : null;
            OwnedEntry ownedEntry = state.OwnedEntries.LastOrDefault(x =>
                x.Id == target.Id &&
                ReferenceEquals(x.BlendRenderer, blendRenderer));

            if (existingData != null)
            {
                if (ownedEntry == null ||
                    !ReferenceEquals(ownedEntry.DirtyData, existingData))
                    return false;

                OwnedStatus status = GetOwnedStatus(ownedEntry);
                if (status == OwnedStatus.Modified)
                {
                    state.OwnedEntries.Remove(ownedEntry);
                    return false;
                }
                if (status == OwnedStatus.Missing)
                {
                    state.OwnedEntries.Remove(ownedEntry);
                    ownedEntry = null;
                }
            }

            setWeight.Invoke(editor, new object[]
            {
                target.Renderer,
                target.BlendShapeIndex,
                target.Record.Weight
            });

            object dirtyData = dirtyBlends.Contains(target.Record.ShapeName)
                ? dirtyBlends[target.Record.ShapeName]
                : null;
            if (dirtyData == null)
                return false;

            if (ownedEntry == null)
            {
                ownedEntry = new OwnedEntry
                {
                    Id = target.Id,
                    Scope = target.Record.TargetScope,
                    Coordinate = target.Record.Coordinate,
                    ShapeName = target.Record.ShapeName,
                    BlendRenderer = blendRenderer
                };
                state.OwnedEntries.Add(ownedEntry);
            }

            ownedEntry.DirtyData = dirtyData;
            ownedEntry.InjectedWeight = target.Record.Weight;
            return true;
        }

        private static void ReleasePreviousCoordinateEntries(SyncState state, int coordinate)
        {
            for (int i = state.OwnedEntries.Count - 1; i >= 0; i--)
            {
                var entry = state.OwnedEntries[i];
                if ((entry.Scope != BlendShapeTargetScope.Clothing &&
                     entry.Scope != BlendShapeTargetScope.Accessory &&
                     entry.Scope != BlendShapeTargetScope.CharacterCoordinate) ||
                    entry.Coordinate == coordinate)
                    continue;

                RemoveEntryIfStillOwned(entry);
                state.OwnedEntries.RemoveAt(i);
            }
        }

        private static void CleanupInactiveOwnedEntries(SyncState state,
            HashSet<string> activeIds)
        {
            for (int i = state.OwnedEntries.Count - 1; i >= 0; i--)
            {
                var entry = state.OwnedEntries[i];
                if (activeIds.Contains(entry.Id))
                    continue;

                RemoveEntryIfStillOwned(entry);
                state.OwnedEntries.RemoveAt(i);
            }
        }

        private static void RemoveEntryIfStillOwned(OwnedEntry entry)
        {
            if (GetOwnedStatus(entry) != OwnedStatus.Owned)
                return;

            IDictionary dirtyBlends = GetDirtyBlendDictionary(entry.BlendRenderer);
            if (dirtyBlends != null &&
                dirtyBlends.Contains(entry.ShapeName) &&
                ReferenceEquals(dirtyBlends[entry.ShapeName], entry.DirtyData))
            {
                dirtyBlends.Remove(entry.ShapeName);
            }
        }

        private static OwnedStatus GetOwnedStatus(OwnedEntry entry)
        {
            IDictionary dirtyBlends = GetDirtyBlendDictionary(entry.BlendRenderer);
            if (dirtyBlends == null || !dirtyBlends.Contains(entry.ShapeName))
                return OwnedStatus.Missing;

            object dirtyData = dirtyBlends[entry.ShapeName];
            if (!ReferenceEquals(dirtyData, entry.DirtyData))
                return OwnedStatus.Missing;

            FieldInfo weightField = FindFieldInHierarchy(dirtyData.GetType(), "weight");
            if (weightField == null)
                return OwnedStatus.Owned;

            float weight = Convert.ToSingle(weightField.GetValue(dirtyData));
            return Mathf.Abs(weight - entry.InjectedWeight) <= 0.001f
                ? OwnedStatus.Owned
                : OwnedStatus.Modified;
        }

        private static object FindBlendShapesEditor(OCIChar ociChar)
        {
            object poseController = FindPoseEditorController(ociChar);
            if (poseController == null)
                return null;

            FieldInfo field = FindFieldInHierarchy(
                poseController.GetType(), "_blendShapesEditor");
            return field?.GetValue(poseController);
        }

        private static object FindPoseEditorController(OCIChar ociChar)
        {
#if KK
            Type poseControllerType = Type.GetType("HSPE.PoseController, KKPE");
#else
            Type poseControllerType = Type.GetType("HSPE.PoseController, KKSPE");
#endif
            if (poseControllerType == null)
                return null;

            FieldInfo controllersField = FindFieldInHierarchy(
                poseControllerType, "_poseControllers");
            var controllers = controllersField?.GetValue(null) as IEnumerable;
            if (controllers == null)
                return null;

            foreach (var controller in controllers)
            {
                PropertyInfo targetProperty = FindPropertyInHierarchy(
                    controller.GetType(), "target");
                object target = targetProperty?.GetValue(controller, null);
                if (target == null)
                    continue;

                FieldInfo ociField = FindFieldInHierarchy(target.GetType(), "ociChar");
                var targetOciChar = ociField?.GetValue(target) as OCIChar;
                if (ReferenceEquals(targetOciChar, ociChar))
                    return controller;
            }
            return null;
        }

        private static object GetBlendRenderer(object editor,
            SkinnedMeshRenderer renderer)
        {
            FieldInfo field = FindFieldInHierarchy(
                editor.GetType(), "_blenderRenderbySkinnedRenderer");
            IDictionary map = field?.GetValue(editor) as IDictionary;
            return map != null && map.Contains(renderer) ? map[renderer] : null;
        }

        private static IDictionary GetDirtyBlendDictionary(object blendRenderer)
        {
            if (blendRenderer == null)
                return null;

            FieldInfo field = FindFieldInHierarchy(
                blendRenderer.GetType(), "_dirtyBlends");
            return field?.GetValue(blendRenderer) as IDictionary;
        }

        private static int BuildTopologyFingerprint(OCIChar ociChar)
        {
            unchecked
            {
                int hash = 17;
                ChaControl chaControl = ociChar?.charInfo;
                if (chaControl == null)
                    return hash;

                hash = hash * 31 + chaControl.fileStatus.coordinateType;
                foreach (var renderer in BlendShapeUtilities.EnumerateRenderers(
                             chaControl.transform))
                {
                    if (renderer == null)
                        continue;

                    hash = hash * 31 + renderer.GetInstanceID();
                    hash = hash * 31 + (renderer.sharedMesh == null
                        ? 0
                        : renderer.sharedMesh.GetInstanceID());
                }
                return hash;
            }
        }

        private static string MakeRecordId(BlendShapeRecord record)
        {
            return (int)record.TargetScope + "|" + record.Coordinate + "|" +
                   record.RendererPath + "|" + record.MeshName + "|" +
                   record.ShapeName;
        }

        private static bool IsStateAlive(SyncState state)
        {
            return state != null &&
                   state.OciChar?.charInfo != null &&
                   state.Controller != null &&
                   SyncStates.TryGetValue(state.Key, out SyncState current) &&
                   ReferenceEquals(current, state);
        }

        private static void RemoveDeadStates()
        {
            foreach (int key in SyncStates
                         .Where(x => x.Value?.OciChar?.charInfo == null ||
                                     x.Value.Controller == null)
                         .Select(x => x.Key)
                         .ToList())
            {
                SyncStates.Remove(key);
            }
        }

        private static FieldInfo FindFieldInHierarchy(Type type, string name)
        {
            while (type != null)
            {
                FieldInfo field = type.GetField(name,
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
                type = type.BaseType;
            }
            return null;
        }

        private static PropertyInfo FindPropertyInHierarchy(Type type, string name)
        {
            while (type != null)
            {
                PropertyInfo property = type.GetProperty(name,
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.DeclaredOnly);
                if (property != null)
                    return property;
                type = type.BaseType;
            }
            return null;
        }

        private static MethodInfo FindMethodInHierarchy(Type type, string name,
            Type[] parameterTypes)
        {
            while (type != null)
            {
                MethodInfo method = type.GetMethod(name,
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.DeclaredOnly,
                    null, parameterTypes, null);
                if (method != null)
                    return method;
                type = type.BaseType;
            }
            return null;
        }

        private enum OwnedStatus
        {
            Missing,
            Owned,
            Modified
        }

        private sealed class SyncState
        {
            public int Key;
            public OCIChar OciChar;
            public BlendShapeSyncController Controller;
            public Coroutine Coroutine;
            public readonly List<OwnedEntry> OwnedEntries =
                new List<OwnedEntry>();
        }

        private sealed class OwnedEntry
        {
            public string Id;
            public BlendShapeTargetScope Scope;
            public int Coordinate;
            public string ShapeName;
            public object BlendRenderer;
            public object DirtyData;
            public float InjectedWeight;
        }

        private sealed class SyncTarget
        {
            public string Id;
            public BlendShapeRecord Record;
            public SkinnedMeshRenderer Renderer;
            public int BlendShapeIndex;
        }
    }
}
