using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KKAPI;
using KKAPI.Chara;
using UnityEngine;

namespace FaceWeightBinder
{
    public sealed class FaceWeightBinderController : CharaCustomFunctionController
    {
        private const float TopologyPollInterval = 0.25f;
        private const float RetryInterval = 0.2f;
        private const int RetryCount = 8;

        private static readonly FieldInfo HeadWeightsField =
            AccessTools.Field(typeof(ChaControl), "aaWeightsHead");

        private sealed class RendererBindingState
        {
            public SkinnedMeshRenderer Renderer;
            public Mesh SourceMesh;
            public Mesh CorrectedMesh;
            public Transform[] SourceBones;
            public Transform[] TargetBones;
            public SkinnedMeshRenderer ReferenceRenderer;
            public Mesh ReferenceMesh;
            public Transform SourceMeshSpaceReference;
            public bool UsesReferenceBindposes;
            public bool DiagnosticsLogged;
        }

        private readonly HashSet<string> _loggedFailures = new HashSet<string>();
        private readonly HashSet<int> _loggedReferenceSearch = new HashSet<int>();
        private readonly Dictionary<int, RendererBindingState> _rendererBindingStates =
            new Dictionary<int, RendererBindingState>();
        private Coroutine _scheduledRebind;
        private float _nextTopologyPoll;
        private int _topologyFingerprint = int.MinValue;

        protected override void OnReload(GameMode currentGameMode, bool maintainState)
        {
            if (!maintainState)
            {
                _loggedFailures.Clear();
                _loggedReferenceSearch.Clear();
                _topologyFingerprint = int.MinValue;
            }
            ScheduleRebind();
            base.OnReload(currentGameMode, maintainState);
        }

        protected override void OnCoordinateBeingLoaded(ChaFileCoordinate coordinate)
        {
            ScheduleRebind();
            base.OnCoordinateBeingLoaded(coordinate);
        }

        protected override void OnCardBeingSaved(GameMode currentGameMode)
        {
        }

        protected override void OnDestroy()
        {
            if (_scheduledRebind != null)
                StopCoroutine(_scheduledRebind);
            ReleaseRuntimeMeshes();
            base.OnDestroy();
        }

        protected override void Update()
        {
            base.Update();
            if (ChaControl == null || Time.unscaledTime < _nextTopologyPoll)
                return;

            _nextTopologyPoll = Time.unscaledTime + TopologyPollInterval;
            PruneRuntimeMeshes();
            int fingerprint = TomTom.KKMod.Shared.TopologyFingerprint.Build(
                EnumerateAssetRoots());
            if (fingerprint == _topologyFingerprint)
                return;

            _topologyFingerprint = fingerprint;
            ScheduleRebind();
        }

        internal void ScheduleRebind()
        {
            if (_scheduledRebind != null)
                StopCoroutine(_scheduledRebind);
            _scheduledRebind = StartCoroutine(DelayedRebind());
        }

        internal bool RebindImmediatelyForCompatibility()
        {
            if (_scheduledRebind != null)
            {
                StopCoroutine(_scheduledRebind);
                _scheduledRebind = null;
            }

            AccessoryBoneBinderBridge.RebindNowIfAvailable(ChaControl);
            return BindMarkedRenderers().Changed;
        }

        private IEnumerator DelayedRebind()
        {
            yield return null;
            yield return null;

            AccessoryBoneBinderBridge.RebindNowIfAvailable(ChaControl);
            yield return null;

            for (int attempt = 0; attempt < RetryCount; attempt++)
            {
                var report = BindMarkedRenderers();
                if (report.MarkerCount == 0 || report.FailedRendererCount == 0)
                    break;
                yield return new WaitForSecondsRealtime(RetryInterval);
            }

            _scheduledRebind = null;
        }

        private FaceWeightBindingReport BindMarkedRenderers()
        {
            var report = new FaceWeightBindingReport();
            if (ChaControl == null)
                return report;

            var markers = FindMarkers();
            report.MarkerCount = markers.Count;
            if (markers.Count == 0)
                return report;

            var headBones = GetHeadBoneDictionary();
            if (headBones == null || headBones.Count == 0)
            {
                report.FailedRendererCount = markers.Count;
                return report;
            }

            foreach (var marker in markers)
                BindMarker(marker, headBones, report);

            if (report.Changed)
            {
                FaceWeightBinderPlugin.Log?.LogInfo(
                    "Bound " + report.BoundRendererCount + " face-weighted renderer(s) on " +
                    ChaControl.name + ".");
                BlendShapeSyncBridge.ScheduleApplyIfAvailable(ChaControl);
                FaceWeightBinderApi.RaiseBindingsUpdated(ChaControl, report);
            }

            return report;
        }

        private List<FaceWeightProcess> FindMarkers()
        {
            var result = new List<FaceWeightProcess>();
            var seenMarkers = new HashSet<int>();
            foreach (var root in EnumerateAssetRoots())
            {
                if (root == null)
                    continue;
                foreach (var marker in root.GetComponentsInChildren<FaceWeightProcess>(true))
                {
                    if (marker != null && seenMarkers.Add(marker.GetInstanceID()))
                        result.Add(marker);
                }
            }
            return result;
        }

        private void BindMarker(FaceWeightProcess marker,
            IDictionary<string, GameObject> headBones, FaceWeightBindingReport report)
        {
            var bindings = GetBindings(marker);
            for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
            {
                var binding = bindings[bindingIndex];
                if (binding == null)
                    continue;

                report.RendererCount++;
                var renderer = ResolveRenderer(marker, binding);
                if (renderer == null)
                {
                    report.FailedRendererCount++;
                    LogFailureOnce(marker, bindingIndex, "renderer was not found");
                    continue;
                }

                var boneNames = binding.boneNames;
                if (boneNames == null || boneNames.Length == 0)
                {
                    boneNames = CaptureCurrentBoneNames(renderer);
                    if (boneNames == null)
                    {
                        report.FailedRendererCount++;
                        LogFailureOnce(marker, bindingIndex,
                            "no baked bone names were available");
                        continue;
                    }
                }

                var sourceBones = binding.sourceBones ?? new Transform[0];
                var mappedBones = new Transform[boneNames.Length];
                string missingBone = null;
                for (int boneIndex = 0; boneIndex < boneNames.Length; boneIndex++)
                {
                    string boneName = boneNames[boneIndex];
                    if (!string.IsNullOrEmpty(boneName) &&
                        headBones.TryGetValue(boneName, out var headBone) &&
                        headBone != null)
                    {
                        mappedBones[boneIndex] = headBone.transform;
                        continue;
                    }

                    var sourceBone = boneIndex < sourceBones.Length
                        ? sourceBones[boneIndex]
                        : null;
                    if (CanKeepCustomSourceBone(marker, sourceBone, boneName))
                    {
                        mappedBones[boneIndex] = sourceBone;
                        continue;
                    }

                    missingBone = string.IsNullOrEmpty(boneName)
                        ? "(unnamed bone at index " + boneIndex + ")"
                        : boneName;
                    break;
                }

                string rootBoneName = !string.IsNullOrEmpty(binding.rootBoneName)
                    ? binding.rootBoneName
                    : renderer.rootBone == null ? string.Empty : renderer.rootBone.name;
                if (missingBone != null || string.IsNullOrEmpty(rootBoneName) ||
                    !headBones.TryGetValue(rootBoneName, out var rootBoneObject) ||
                    rootBoneObject == null)
                {
                    report.FailedRendererCount++;
                    LogFailureOnce(marker, bindingIndex,
                        missingBone != null
                            ? "head bone '" + missingBone + "' was not found"
                            : "root bone '" + rootBoneName + "' was not found");
                    continue;
                }

                var rootBone = rootBoneObject.transform;
                var referenceRenderer = FindCharacterHeadRenderer(
                    rootBoneName, boneNames, renderer);
                if (referenceRenderer == null &&
                    _loggedReferenceSearch.Add(renderer.GetInstanceID()))
                {
                    FaceWeightBinderPlugin.Log?.LogInfo(
                        FaceWeightDiagnostics.BuildReferenceSearchReport(
                            ChaControl, marker, renderer, rootBoneName,
                            boneNames));
                }
                if (!TryPrepareCorrectedMesh(renderer, marker, binding, boneNames,
                        mappedBones, referenceRenderer,
                        out bool meshChanged, out string correctionFailure))
                {
                    report.FailedRendererCount++;
                    LogFailureOnce(marker, bindingIndex, correctionFailure);
                    continue;
                }

                bool bonesChanged = !IsAlreadyBound(renderer, mappedBones, rootBone);
                if (!bonesChanged && !meshChanged)
                {
                    report.UnchangedRendererCount++;
                    continue;
                }

                if (bonesChanged)
                {
                    renderer.bones = mappedBones;
                    renderer.rootBone = rootBone;
                }
                if (marker.copyCharacterHeadBounds)
                {
                    if (referenceRenderer != null)
                        renderer.localBounds = referenceRenderer.localBounds;
                }

                report.BoundRendererCount++;
            }
        }

        private FaceWeightRendererBinding[] GetBindings(FaceWeightProcess marker)
        {
            if (marker.bindings != null && marker.bindings.Length > 0)
                return marker.bindings;

            var renderers = marker.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var bindings = new FaceWeightRendererBinding[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                bindings[i] = new FaceWeightRendererBinding
                {
                    renderer = renderer,
                    rendererPath = TomTom.KKMod.Shared.TransformPath.GetRelativePath(
                        marker.transform, renderer.transform),
                    sourceBones = renderer.bones,
                    boneNames = CaptureCurrentBoneNames(renderer) ?? new string[0],
                    rootBoneName = renderer.rootBone == null
                        ? string.Empty
                        : renderer.rootBone.name
                };
            }
            return bindings;
        }

        private static SkinnedMeshRenderer ResolveRenderer(FaceWeightProcess marker,
            FaceWeightRendererBinding binding)
        {
            if (binding.renderer != null)
                return binding.renderer;
            var transform = TomTom.KKMod.Shared.TransformPath.Find(
                marker.transform, binding.rendererPath);
            return transform == null ? null : transform.GetComponent<SkinnedMeshRenderer>();
        }

        private static string[] CaptureCurrentBoneNames(SkinnedMeshRenderer renderer)
        {
            if (renderer == null)
                return null;
            var bones = renderer.bones;
            if (bones == null || bones.Length == 0 || bones.Any(x => x == null))
                return null;
            return bones.Select(x => x.name).ToArray();
        }

        private bool TryPrepareCorrectedMesh(SkinnedMeshRenderer renderer,
            FaceWeightProcess marker, FaceWeightRendererBinding binding,
            string[] boneNames, Transform[] mappedBones,
            SkinnedMeshRenderer referenceRenderer, out bool changed,
            out string failureReason)
        {
            changed = false;
            failureReason = null;
            int rendererId = renderer.GetInstanceID();

            if (_rendererBindingStates.TryGetValue(rendererId, out var existing) &&
                existing.Renderer != renderer)
            {
                ReleaseRendererState(existing, false);
                _rendererBindingStates.Remove(rendererId);
                existing = null;
            }

            if (existing != null && renderer.sharedMesh != existing.SourceMesh &&
                renderer.sharedMesh != existing.CorrectedMesh)
            {
                ReleaseRendererState(existing, false);
                _rendererBindingStates.Remove(rendererId);
                existing = null;
            }

            if (existing == null)
            {
                var sourceMesh = renderer.sharedMesh;
                if (sourceMesh == null)
                {
                    failureReason = "renderer has no mesh";
                    return false;
                }

                var bindposes = sourceMesh.bindposes;
                if (bindposes == null || bindposes.Length != mappedBones.Length)
                {
                    failureReason = "mesh bindpose count " +
                                    (bindposes == null ? 0 : bindposes.Length) +
                                    " does not match bone count " + mappedBones.Length;
                    return false;
                }

                var currentBones = renderer.bones ?? new Transform[0];
                var bakedSourceBones = binding.sourceBones ?? new Transform[0];
                var sourceBones = new Transform[mappedBones.Length];
                for (int i = 0; i < sourceBones.Length; i++)
                {
                    sourceBones[i] = i < bakedSourceBones.Length &&
                                     bakedSourceBones[i] != null
                        ? bakedSourceBones[i]
                        : i < currentBones.Length ? currentBones[i] : null;
                    if (sourceBones[i] != null)
                        continue;

                    failureReason = "source bone at index " + i + " is missing";
                    return false;
                }

                Mesh correctedMesh;
                try
                {
                    correctedMesh = Instantiate(sourceMesh);
                    correctedMesh.name = sourceMesh.name + " (FaceWeightBinder)";
                    correctedMesh.hideFlags = HideFlags.DontSave;
                }
                catch (Exception ex)
                {
                    failureReason = "mesh clone failed: " + ex.Message;
                    return false;
                }

                existing = new RendererBindingState
                {
                    Renderer = renderer,
                    SourceMesh = sourceMesh,
                    CorrectedMesh = correctedMesh,
                    SourceBones = sourceBones
                };
                _rendererBindingStates.Add(rendererId, existing);
            }

            var referenceMesh = referenceRenderer == null
                ? null
                : referenceRenderer.sharedMesh;
            var sourceMeshSpaceReference = FindSourceMeshSpaceReference(
                marker, renderer, referenceRenderer);
            bool targetChanged = !HaveSameBones(existing.TargetBones, mappedBones) ||
                                 existing.ReferenceRenderer != referenceRenderer ||
                                 existing.ReferenceMesh != referenceMesh ||
                                 existing.SourceMeshSpaceReference !=
                                 sourceMeshSpaceReference;
            if (targetChanged)
            {
                Matrix4x4[] correctedBindposes;
                Matrix4x4 sourceToReference = Matrix4x4.identity;
                bool usedReferenceBindposes = false;
                try
                {
                    if (referenceRenderer != null)
                    {
                        if (sourceMeshSpaceReference == null)
                        {
                            failureReason = "source face mesh-space reference is unavailable";
                            return false;
                        }
                        if (!TryBuildReferenceBindposes(renderer, boneNames,
                                referenceRenderer, sourceMeshSpaceReference,
                                out correctedBindposes, out sourceToReference,
                                out failureReason))
                            return false;
                        usedReferenceBindposes = true;
                    }
                    else
                    {
                        if (!TryBuildPosePreservingBindposes(existing, mappedBones,
                                out correctedBindposes, out failureReason))
                            return false;
                    }

                    existing.CorrectedMesh.bindposes = correctedBindposes;
                }
                catch (Exception ex)
                {
                    failureReason = "bindpose correction failed: " + ex.Message;
                    return false;
                }

                existing.TargetBones = mappedBones.ToArray();
                existing.ReferenceRenderer = referenceRenderer;
                existing.ReferenceMesh = referenceMesh;
                existing.SourceMeshSpaceReference = sourceMeshSpaceReference;
                if (usedReferenceBindposes && !existing.UsesReferenceBindposes)
                {
                    FaceWeightBinderPlugin.Log?.LogDebug(
                        "Using character head bindposes for renderer '" +
                        renderer.name + "'.");
                }
                existing.UsesReferenceBindposes = usedReferenceBindposes;
                if (usedReferenceBindposes && !existing.DiagnosticsLogged)
                {
                    try
                    {
                        FaceWeightBinderPlugin.Log?.LogInfo(
                            FaceWeightDiagnostics.BuildReport(
                                ChaControl, marker, renderer, existing.SourceMesh,
                                existing.SourceBones, boneNames, mappedBones,
                                referenceRenderer, sourceMeshSpaceReference,
                                sourceToReference, correctedBindposes));
                    }
                    catch (Exception ex)
                    {
                        FaceWeightBinderPlugin.Log?.LogWarning(
                            "Face-weight coordinate diagnostics failed: " + ex);
                    }
                    existing.DiagnosticsLogged = true;
                }
                changed = true;
            }

            if (renderer.sharedMesh != existing.CorrectedMesh)
            {
                renderer.sharedMesh = existing.CorrectedMesh;
                changed = true;
            }
            return true;
        }

        private static Transform FindSourceMeshSpaceReference(
            FaceWeightProcess marker, SkinnedMeshRenderer renderer,
            SkinnedMeshRenderer referenceRenderer)
        {
            if (marker == null || referenceRenderer == null)
                return null;

            var transforms = marker.GetComponentsInChildren<Transform>(true)
                .Where(x => x != null && x != renderer.transform &&
                            x.GetComponent<SkinnedMeshRenderer>() == null)
                .ToArray();
            var exact = transforms.FirstOrDefault(x =>
                string.Equals(x.name, referenceRenderer.name,
                    StringComparison.Ordinal));
            if (exact != null)
                return exact;

            return transforms.FirstOrDefault(x =>
                string.Equals(x.name, "cf_O_face",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.name, "cm_O_face",
                    StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryBuildReferenceBindposes(
            SkinnedMeshRenderer renderer, string[] boneNames,
            SkinnedMeshRenderer referenceRenderer,
            Transform sourceMeshSpaceReference,
            out Matrix4x4[] correctedBindposes,
            out Matrix4x4 sourceToReference,
            out string failureReason)
        {
            correctedBindposes = null;
            sourceToReference = Matrix4x4.identity;
            failureReason = null;
            var referenceMesh = referenceRenderer.sharedMesh;
            var referenceBones = referenceRenderer.bones ?? new Transform[0];
            var referenceBindposes = referenceMesh == null
                ? null
                : referenceMesh.bindposes;
            if (referenceBindposes == null ||
                referenceBindposes.Length != referenceBones.Length)
            {
                failureReason = "character head reference has mismatched bones and bindposes";
                return false;
            }

            var bindposesByName = new Dictionary<string, Matrix4x4>(
                StringComparer.Ordinal);
            for (int i = 0; i < referenceBones.Length; i++)
            {
                var bone = referenceBones[i];
                if (bone != null && !bindposesByName.ContainsKey(bone.name))
                    bindposesByName.Add(bone.name, referenceBindposes[i]);
            }

            sourceToReference = sourceMeshSpaceReference.worldToLocalMatrix *
                                renderer.transform.localToWorldMatrix;

            correctedBindposes = new Matrix4x4[boneNames.Length];
            for (int i = 0; i < boneNames.Length; i++)
            {
                string boneName = boneNames[i];
                if (string.IsNullOrEmpty(boneName) ||
                    !bindposesByName.TryGetValue(boneName, out var bindpose))
                {
                    failureReason = "character head reference has no bindpose for bone '" +
                                    (string.IsNullOrEmpty(boneName)
                                        ? "(unnamed bone at index " + i + ")"
                                        : boneName) + "'";
                    return false;
                }
                correctedBindposes[i] = bindpose * sourceToReference;
            }
            return true;
        }

        private static bool TryBuildPosePreservingBindposes(
            RendererBindingState state, Transform[] mappedBones,
            out Matrix4x4[] correctedBindposes, out string failureReason)
        {
            failureReason = null;
            var sourceBindposes = state.SourceMesh.bindposes;
            if (sourceBindposes == null ||
                sourceBindposes.Length != state.SourceBones.Length)
            {
                correctedBindposes = null;
                failureReason = "source mesh bindposes changed after binding";
                return false;
            }

            correctedBindposes = new Matrix4x4[sourceBindposes.Length];
            for (int i = 0; i < correctedBindposes.Length; i++)
            {
                var sourceBone = state.SourceBones[i];
                var targetBone = mappedBones[i];
                if (sourceBone == null || targetBone == null)
                {
                    failureReason = "source or target bone at index " + i +
                                    " was destroyed";
                    return false;
                }

                correctedBindposes[i] = targetBone.worldToLocalMatrix *
                                        sourceBone.localToWorldMatrix *
                                        sourceBindposes[i];
            }
            return true;
        }

        private static bool HaveSameBones(Transform[] left, Transform[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return false;
            }
            return true;
        }

        private void PruneRuntimeMeshes()
        {
            if (_rendererBindingStates.Count == 0)
                return;

            var deadIds = new List<int>();
            foreach (var pair in _rendererBindingStates)
            {
                if (pair.Value.Renderer == null)
                {
                    ReleaseRendererState(pair.Value, false);
                    deadIds.Add(pair.Key);
                }
            }
            foreach (int rendererId in deadIds)
                _rendererBindingStates.Remove(rendererId);
        }

        private void ReleaseRuntimeMeshes()
        {
            foreach (var state in _rendererBindingStates.Values)
                ReleaseRendererState(state, true);
            _rendererBindingStates.Clear();
        }

        private static void ReleaseRendererState(RendererBindingState state,
            bool restoreSourceMesh)
        {
            if (state == null)
                return;
            if (restoreSourceMesh && state.Renderer != null &&
                state.Renderer.sharedMesh == state.CorrectedMesh)
                state.Renderer.sharedMesh = state.SourceMesh;
            if (state.CorrectedMesh != null)
                Destroy(state.CorrectedMesh);
        }

        private static bool CanKeepCustomSourceBone(FaceWeightProcess marker,
            Transform sourceBone, string expectedName)
        {
            if (sourceBone == null)
                return false;
            if (marker.skeletonRoot != null &&
                (sourceBone == marker.skeletonRoot ||
                 sourceBone.IsChildOf(marker.skeletonRoot)))
                return false;
            return !LooksLikeVanillaHeadBone(expectedName);
        }

        private static bool LooksLikeVanillaHeadBone(string boneName)
        {
            return !string.IsNullOrEmpty(boneName) &&
                   (boneName.StartsWith("cf_", StringComparison.OrdinalIgnoreCase) ||
                    boneName.StartsWith("cm_", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsAlreadyBound(SkinnedMeshRenderer renderer,
            Transform[] mappedBones, Transform rootBone)
        {
            if (renderer.rootBone != rootBone)
                return false;
            var current = renderer.bones;
            if (current == null || current.Length != mappedBones.Length)
                return false;
            for (int i = 0; i < current.Length; i++)
            {
                if (current[i] != mappedBones[i])
                    return false;
            }
            return true;
        }

        private SkinnedMeshRenderer FindCharacterHeadRenderer(
            string rootBoneName, IEnumerable<string> requiredBoneNames,
            SkinnedMeshRenderer sourceRenderer)
        {
            if (ChaControl == null)
                return null;

            var requiredNames = new HashSet<string>(
                requiredBoneNames.Where(x => !string.IsNullOrEmpty(x)),
                StringComparer.Ordinal);
            var allCandidates = ChaControl
                .GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(x => x != null && x != sourceRenderer &&
                            x.sharedMesh != null && x.bones != null &&
                            x.sharedMesh.bindposes.Length == x.bones.Length)
                .ToArray();
            var compatibleCandidates = allCandidates
                .Where(x =>
                {
                    var names = new HashSet<string>(
                        x.bones.Where(bone => bone != null).Select(bone => bone.name),
                        StringComparer.Ordinal);
                    return requiredNames.All(names.Contains);
                })
                .ToArray();

            var compatible = compatibleCandidates
                .OrderByDescending(x =>
                    string.Equals(x.name, "cf_O_face",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x.name, "cm_O_face",
                        StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(x => x.rootBone != null &&
                                       x.rootBone.name == rootBoneName)
                .ThenByDescending(x => x.sharedMesh.vertexCount)
                .FirstOrDefault();
            if (compatible != null)
                return compatible;

            return allCandidates
                .Where(x => string.Equals(x.name, "cf_O_face",
                                StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(x.name, "cm_O_face",
                                StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.rootBone != null &&
                                        x.rootBone.name == rootBoneName)
                .ThenByDescending(x => x.sharedMesh.vertexCount)
                .FirstOrDefault();
        }

        private IDictionary<string, GameObject> GetHeadBoneDictionary()
        {
            try
            {
                var headWeights = HeadWeightsField?.GetValue(ChaControl)
                    as AssignedAnotherWeights;
                return headWeights?.dictBone;
            }
            catch (Exception ex)
            {
                FaceWeightBinderPlugin.Log?.LogWarning(
#if KK
                    "Could not read the KK head bone dictionary: " + ex);
#else
                    "Could not read the KKS head bone dictionary: " + ex);
#endif
                return null;
            }
        }

        private IEnumerable<GameObject> EnumerateAssetRoots()
        {
            if (ChaControl == null)
                yield break;

            var clothes = ChaControl.objClothes;
            if (clothes != null)
            {
                for (int i = 0; i < clothes.Length; i++)
                    if (clothes[i] != null)
                        yield return clothes[i];
            }

            var accessories = ChaControl.objAccessory;
            if (accessories != null)
            {
                for (int i = 0; i < accessories.Length; i++)
                    if (accessories[i] != null)
                        yield return accessories[i];
            }
        }

        private void LogFailureOnce(FaceWeightProcess marker, int bindingIndex,
            string reason)
        {
            string key = marker.GetInstanceID() + "|" + bindingIndex + "|" + reason;
            if (!_loggedFailures.Add(key))
                return;
            FaceWeightBinderPlugin.Log?.LogWarning(
                "FaceWeightProcess '" + marker.name + "', binding " + bindingIndex +
                " was skipped because " + reason + ".");
        }
    }
}
