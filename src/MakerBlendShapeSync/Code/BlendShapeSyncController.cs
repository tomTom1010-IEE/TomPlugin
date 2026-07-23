using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ExtensibleSaveFormat;
using KKAPI;
using KKAPI.Chara;
using KKAPI.Maker;
using MessagePack;
using UnityEngine;

namespace MakerBlendShapeSync
{
    public sealed class BlendShapeSyncController : CharaCustomFunctionController
    {
        private const string DataKey = "BlendShapeSyncData";
        private const int DataVersion = 5;
        private const float TopologyPollInterval = 0.2f;
        private const int StableApplyFrames = 3;
        private const float StableApplyDuration = 0.15f;
        private const float ApplyTimeout = 3f;

        private sealed class BaselineState
        {
            internal SkinnedMeshRenderer Renderer;
            internal Mesh Mesh;
            internal string ShapeName;
            internal int ShapeIndex;
            internal int Coordinate;
            internal float Weight;
        }

        internal sealed class RendererMeshReplacementState
        {
            internal BlendShapeSyncController Controller;
            internal SkinnedMeshRenderer Destination;
            internal string OldMeshName;
            internal readonly List<int> ShapeIndices = new List<int>();
            internal readonly List<string> ShapeNames = new List<string>();
        }

        internal readonly List<BlendShapeRecord> Records = new List<BlendShapeRecord>();
        internal readonly List<RendererScopeOverride> PerCoordinateRenderers =
            new List<RendererScopeOverride>();

        private readonly Dictionary<string, BaselineState> _baselines =
            new Dictionary<string, BaselineState>();
        private Coroutine _scheduledApply;
        private float _nextTopologyPoll;
        private int _topologyFingerprint = int.MinValue;
        private int _observedCoordinate = -1;
        private int _rendererGeneration;
        private int _applyRevision;

        public int CurrentCoordinateIndex => ChaControl.fileStatus.coordinateType;
        internal int RendererGeneration => _rendererGeneration;

        private ChaFile LoadedChaFile =>
            MakerAPI.InsideMaker && MakerAPI.LastLoadedChaFile != null
                ? MakerAPI.LastLoadedChaFile
                : ChaControl.chaFile;

        protected override void OnReload(GameMode currentGameMode, bool maintainState)
        {
            CancelScheduledApply();
            if (!maintainState)
            {
                RestoreAllBaselines();

                LoadFromExtendedData();
                _observedCoordinate = CurrentCoordinateIndex;
                _topologyFingerprint = int.MinValue;
                _rendererGeneration++;
            }

            ScheduleApply();
            base.OnReload(currentGameMode, maintainState);
        }

        protected override void OnCardBeingSaved(GameMode currentGameMode)
        {
            SaveToExtendedData();
        }

        protected override void OnCoordinateBeingSaved(ChaFileCoordinate coordinate)
        {
            SaveCoordinateRecordsTo(coordinate, CurrentCoordinateIndex);
            base.OnCoordinateBeingSaved(coordinate);
        }

        protected override void OnCoordinateBeingLoaded(ChaFileCoordinate coordinate)
        {
            CancelScheduledApply();
            if (MakerAPI.InsideMaker && _observedCoordinate >= 0)
                RestoreCoordinateBaselines(_observedCoordinate);

            var loadFlags = MakerAPI.GetCoordinateLoadFlags();
            bool loadClothing = loadFlags == null || loadFlags.Clothes;
            bool loadAccessories = loadFlags == null || loadFlags.Accessories;
            LoadCoordinateRecordsFrom(coordinate, CurrentCoordinateIndex,
                loadClothing, loadAccessories);
            ScheduleApply();
            base.OnCoordinateBeingLoaded(coordinate);
        }

        protected override void OnDestroy()
        {
            CancelScheduledApply();
            _baselines.Clear();
            base.OnDestroy();
        }

        protected override void Update()
        {
            base.Update();
            if (!MakerAPI.InsideMaker || ChaControl == null || Time.unscaledTime < _nextTopologyPoll)
                return;

            _nextTopologyPoll = Time.unscaledTime + TopologyPollInterval;

            int coordinate = CurrentCoordinateIndex;
            if (_observedCoordinate < 0)
            {
                _observedCoordinate = coordinate;
            }
            else if (_observedCoordinate != coordinate)
            {
                RestoreCoordinateBaselines(_observedCoordinate);
                _observedCoordinate = coordinate;
                ScheduleApply();
            }

            int fingerprint = BuildTopologyFingerprint();
            if (fingerprint == _topologyFingerprint)
                return;

            _topologyFingerprint = fingerprint;
            _rendererGeneration++;
            ScheduleApply();
        }

        internal void ScheduleApply()
        {
            CancelScheduledApply();
            int revision = _applyRevision;

            _scheduledApply = StartCoroutine(MakerAPI.InsideMaker
                ? ApplyWhenMakerStable(revision)
                : DelayedApply(revision));
        }

        private void CancelScheduledApply()
        {
            _applyRevision++;
            if (_scheduledApply != null)
            {
                StopCoroutine(_scheduledApply);
                _scheduledApply = null;
            }
        }

        private bool IsApplyRevisionCurrent(int revision)
        {
            return revision == _applyRevision;
        }

        private IEnumerator DelayedApply(int revision)
        {
            yield return null;
            if (!IsApplyRevisionCurrent(revision))
                yield break;

            yield return null;
            if (!IsApplyRevisionCurrent(revision))
                yield break;

            ApplyCurrentCoordinate();
            StudioPoseEditorBridge.ScheduleForCharacter(ChaControl);
            if (IsApplyRevisionCurrent(revision))
                _scheduledApply = null;
        }

        private IEnumerator ApplyWhenMakerStable(int revision)
        {
            float startedAt = Time.realtimeSinceStartup;
            float stableSince = -1f;
            int previousFingerprint = BuildTopologyFingerprint();
            int stableFrames = 0;
            bool applied = false;

            while (Time.realtimeSinceStartup - startedAt < ApplyTimeout)
            {
                yield return new WaitForEndOfFrame();
                if (!IsApplyRevisionCurrent(revision))
                    yield break;

                int fingerprint = BuildTopologyFingerprint();
                if (fingerprint != previousFingerprint)
                {
                    previousFingerprint = fingerprint;
                    stableFrames = 0;
                    stableSince = -1f;
                    _topologyFingerprint = fingerprint;
                    _rendererGeneration++;
                    continue;
                }

                stableFrames++;
                if (stableFrames < StableApplyFrames)
                    continue;

                float now = Time.realtimeSinceStartup;
                if (stableSince < 0f)
                    stableSince = now;

                if (now - stableSince >= StableApplyDuration)
                {
                    ApplyCurrentCoordinate();
                    applied = true;
                    break;
                }
            }

            if (!IsApplyRevisionCurrent(revision))
                yield break;

            if (!applied)
                ApplyCurrentCoordinate();

            _observedCoordinate = CurrentCoordinateIndex;
            _scheduledApply = null;
        }

        internal void UpsertRecord(BlendShapeRecord record)
        {
            if (record == null)
                return;

            if (record.TargetScope == BlendShapeTargetScope.Character)
                record.Coordinate = -1;
            else if (record.TargetScope == BlendShapeTargetScope.Clothing ||
                     record.TargetScope == BlendShapeTargetScope.Accessory ||
                     record.TargetScope == BlendShapeTargetScope.CharacterCoordinate)
                record.Coordinate = CurrentCoordinateIndex;

            RemoveMatchingRecord(record);
            Records.Add(record);
        }

        internal void RemoveRecord(SkinnedMeshRenderer renderer, string rendererPath, string shapeName)
        {
            if (renderer == null || renderer.sharedMesh == null)
                return;

            BlendShapeTargetScope scope = GetEffectiveTargetScope(renderer);
            int coordinate = scope == BlendShapeTargetScope.Character ? -1 : CurrentCoordinateIndex;
            RestoreBaseline(renderer, shapeName, coordinate);

            string meshName = renderer.sharedMesh.name;
            Records.RemoveAll(x => x.TargetScope == scope &&
                                   x.Coordinate == coordinate &&
                                   x.RendererPath == rendererPath &&
                                   x.MeshName == meshName &&
                                   x.ShapeName == shapeName);
        }

        internal BlendShapeTargetScope GetEffectiveTargetScope(SkinnedMeshRenderer renderer)
        {
            if (renderer == null)
                return BlendShapeTargetScope.Unknown;

            if (BlendShapeUtilities.IsBodyRenderer(renderer))
                return IsRendererPerCoordinate(renderer)
                    ? BlendShapeTargetScope.CharacterCoordinate
                    : BlendShapeTargetScope.Character;

            return BlendShapeUtilities.GetTargetScope(ChaControl, renderer);
        }

        internal bool IsRendererPerCoordinate(SkinnedMeshRenderer renderer)
        {
            if (renderer == null || renderer.sharedMesh == null || ChaControl == null)
                return false;

            string path = BlendShapeUtilities.GetRelativePath(ChaControl.transform, renderer.transform);
            string meshName = renderer.sharedMesh.name;
            return PerCoordinateRenderers.Any(x =>
                x.RendererPath == path && x.MeshName == meshName);
        }

        internal void SetRendererPerCoordinate(SkinnedMeshRenderer renderer, bool enabled)
        {
            if (renderer == null || renderer.sharedMesh == null || ChaControl == null ||
                !BlendShapeUtilities.IsBodyRenderer(renderer) ||
                IsRendererPerCoordinate(renderer) == enabled)
                return;

            string path = BlendShapeUtilities.GetRelativePath(ChaControl.transform, renderer.transform);
            string meshName = renderer.sharedMesh.name;
            int currentCoordinate = CurrentCoordinateIndex;
            int coordinateCount = GetCoordinateCount();
            var matching = Records.Where(x =>
                    x.RendererPath == path && x.MeshName == meshName)
                .ToList();

            Records.RemoveAll(x => x.RendererPath == path && x.MeshName == meshName);
            PerCoordinateRenderers.RemoveAll(x =>
                x.RendererPath == path && x.MeshName == meshName);

            if (enabled)
            {
                PerCoordinateRenderers.Add(new RendererScopeOverride
                {
                    RendererPath = path,
                    MeshName = meshName
                });

                foreach (var shapeGroup in matching.GroupBy(x => x.ShapeName))
                {
                    var global = shapeGroup.LastOrDefault(x =>
                        x.TargetScope == BlendShapeTargetScope.Character || x.Coordinate < 0);
                    for (int coordinate = 0; coordinate < coordinateCount; coordinate++)
                    {
                        var source = shapeGroup.LastOrDefault(x => x.Coordinate == coordinate) ?? global;
                        if (source == null)
                            continue;
                        AddNormalizedRecord(Records,
                            CloneBodyRecord(source, BlendShapeTargetScope.CharacterCoordinate, coordinate));
                    }
                }
            }
            else
            {
                foreach (var shapeGroup in matching.GroupBy(x => x.ShapeName))
                {
                    var source = shapeGroup.LastOrDefault(x => x.Coordinate == currentCoordinate) ??
                                 shapeGroup.LastOrDefault(x =>
                                     x.TargetScope == BlendShapeTargetScope.Character) ??
                                 shapeGroup.LastOrDefault();
                    if (source != null)
                        AddNormalizedRecord(Records,
                            CloneBodyRecord(source, BlendShapeTargetScope.Character, -1));
                }
            }

            NormalizeRecords();
            _rendererGeneration++;
            ScheduleApply();
        }

        internal void AccessoryKindChangedEvent(int slot)
        {
            int coordinate = CurrentCoordinateIndex;
            NormalizeRecords();
            Records.RemoveAll(x => x.TargetScope == BlendShapeTargetScope.Accessory &&
                                   x.Coordinate == coordinate && x.Slot == slot);
            ScheduleApply();
        }

        internal void AccessoryTransferredEvent(int sourceSlot, int destinationSlot)
        {
            int coordinate = CurrentCoordinateIndex;
            NormalizeRecords();
            var copies = Records.Where(x => x.TargetScope == BlendShapeTargetScope.Accessory &&
                                             x.Coordinate == coordinate && x.Slot == sourceSlot)
                .Select(x => CloneForSlot(x, coordinate, destinationSlot)).ToList();

            Records.RemoveAll(x => x.TargetScope == BlendShapeTargetScope.Accessory &&
                                   x.Coordinate == coordinate && x.Slot == destinationSlot);
            foreach (var copy in copies)
            {
                BlendShapeUtilities.RemapRecordToSlot(ChaControl, copy, destinationSlot);
                AddNormalizedRecord(Records, copy);
            }

            ScheduleApply();
        }

        internal void AccessoriesCopiedEvent(int sourceCoordinate, int destinationCoordinate,
            IEnumerable<int> copiedSlots)
        {
            NormalizeRecords();
            foreach (int slot in copiedSlots.Distinct())
            {
                var copies = Records.Where(x => x.TargetScope == BlendShapeTargetScope.Accessory &&
                                                 x.Coordinate == sourceCoordinate && x.Slot == slot)
                    .Select(x => CloneForSlot(x, destinationCoordinate, slot)).ToList();
                Records.RemoveAll(x => x.TargetScope == BlendShapeTargetScope.Accessory &&
                                       x.Coordinate == destinationCoordinate && x.Slot == slot);
                foreach (var copy in copies)
                    AddNormalizedRecord(Records, copy);
            }

            ScheduleApply();
        }

        internal void ClothingCopiedEvent(int sourceCoordinate, int destinationCoordinate,
            IEnumerable<int> copiedSlots)
        {
            NormalizeRecords();
            foreach (int slot in copiedSlots.Distinct())
            {
                var copies = Records.Where(x => x.TargetScope == BlendShapeTargetScope.Clothing &&
                                                 x.Coordinate == sourceCoordinate && x.Slot == slot)
                    .Select(x => CloneForSlot(x, destinationCoordinate, slot)).ToList();
                Records.RemoveAll(x => x.TargetScope == BlendShapeTargetScope.Clothing &&
                                       x.Coordinate == destinationCoordinate && x.Slot == slot);
                foreach (var copy in copies)
                    AddNormalizedRecord(Records, copy);
            }

            ScheduleApply();
        }

        internal BlendShapeRecord GetRecord(BlendShapeTargetScope scope, int coordinate,
            string rendererPath, string meshName, string shapeName)
        {
            return Records.LastOrDefault(x => x.TargetScope == scope &&
                                              x.Coordinate == coordinate &&
                                              x.RendererPath == rendererPath &&
                                              x.MeshName == meshName &&
                                              x.ShapeName == shapeName);
        }

        internal void CaptureBaseline(SkinnedMeshRenderer renderer, string shapeName)
        {
            if (renderer == null || renderer.sharedMesh == null)
                return;

            int index = BlendShapeUtilities.FindBlendShapeIndex(renderer, shapeName);
            if (index < 0)
                return;

            BlendShapeTargetScope scope = GetEffectiveTargetScope(renderer);
            int coordinate = scope == BlendShapeTargetScope.Character ? -1 : CurrentCoordinateIndex;
            CaptureBaseline(renderer, shapeName, coordinate, index);
        }

        internal void ApplyCurrentCoordinate()
        {
            if (ChaControl == null)
                return;

            PruneInvalidBaselines();
            NormalizeRecords();
            int coordinate = CurrentCoordinateIndex;
            MakerBlendShapeSyncPlugin.Log?.LogDebug(
                $"Applying {Records.Count} blendshape record(s) for {ChaControl.name}, coordinate {coordinate}");

            foreach (var record in Records.Where(x =>
                         x.TargetScope == BlendShapeTargetScope.Character ||
                         (x.TargetScope == BlendShapeTargetScope.CharacterCoordinate &&
                          x.Coordinate == coordinate) ||
                         ((x.TargetScope == BlendShapeTargetScope.Clothing ||
                           x.TargetScope == BlendShapeTargetScope.Accessory) &&
                          x.Coordinate == coordinate)))
            {
                TryApply(record);
            }
        }

        private void TryApply(BlendShapeRecord record)
        {
            var renderer = BlendShapeUtilities.FindRenderer(ChaControl.transform, record);
            if (renderer == null || renderer.sharedMesh == null)
            {
                MakerBlendShapeSyncPlugin.Log?.LogDebug(
                    $"Renderer not found for blendshape {record.RendererPath} / {record.RendererName}");
                return;
            }

            int index = BlendShapeUtilities.FindBlendShapeIndex(renderer, record.ShapeName);
            if (index < 0)
            {
                MakerBlendShapeSyncPlugin.Log?.LogDebug(
                    $"Blendshape not found: {record.ShapeName} on {renderer.name}");
                return;
            }

            int coordinate = record.TargetScope == BlendShapeTargetScope.Character
                ? -1
                : CurrentCoordinateIndex;
            CaptureBaseline(renderer, record.ShapeName, coordinate, index);
            renderer.SetBlendShapeWeight(index, record.Weight);
        }

        private void RestoreCoordinateBaselines(int coordinate)
        {
            if (coordinate < 0)
                return;

            int restored = 0;
            var matches = _baselines
                .Where(x => x.Value.Coordinate == coordinate)
                .ToList();
            foreach (var match in matches)
            {
                if (TryRestoreBaseline(match.Value))
                    restored++;
                _baselines.Remove(match.Key);
            }

            LogBaselineRestore("coordinate " + coordinate, restored, matches.Count);
        }

        private void RestoreAllBaselines()
        {
            int restored = 0;
            var matches = _baselines.ToList();
            foreach (var match in matches)
            {
                if (TryRestoreBaseline(match.Value))
                    restored++;
            }

            _baselines.Clear();
            LogBaselineRestore("character reload", restored, matches.Count);
        }

        private void RestoreBaseline(SkinnedMeshRenderer renderer, string shapeName, int coordinate)
        {
            if (renderer == null)
                return;

            string key = MakeBaselineKey(renderer, shapeName, coordinate);
            if (!_baselines.TryGetValue(key, out var state))
                return;

            TryRestoreBaseline(state);
            _baselines.Remove(key);
        }

        private void CaptureBaseline(SkinnedMeshRenderer renderer, string shapeName,
            int coordinate, int shapeIndex)
        {
            string key = MakeBaselineKey(renderer, shapeName, coordinate);
            if (_baselines.ContainsKey(key))
                return;

            _baselines.Add(key, new BaselineState
            {
                Renderer = renderer,
                Mesh = renderer.sharedMesh,
                ShapeName = shapeName,
                ShapeIndex = shapeIndex,
                Coordinate = coordinate,
                Weight = renderer.GetBlendShapeWeight(shapeIndex)
            });
        }

        internal RendererMeshReplacementState PrepareRendererMeshReplacement(
            SkinnedMeshRenderer destination)
        {
            if (destination == null)
                return null;

            var matches = _baselines
                .Where(x => x.Value != null && x.Value.Renderer == destination)
                .ToList();
            if (matches.Count == 0)
                return null;

            CancelScheduledApply();
            var state = new RendererMeshReplacementState
            {
                Controller = this,
                Destination = destination,
                OldMeshName = destination.sharedMesh == null ? "" : destination.sharedMesh.name
            };

            int restored = 0;
            foreach (var match in matches)
            {
                var baseline = match.Value;
                if (TryRestoreBaseline(baseline))
                    restored++;
                if (baseline.ShapeIndex >= 0)
                    state.ShapeIndices.Add(baseline.ShapeIndex);
                if (!string.IsNullOrEmpty(baseline.ShapeName))
                    state.ShapeNames.Add(baseline.ShapeName);
                _baselines.Remove(match.Key);
            }

            MakerBlendShapeSyncPlugin.Log?.LogDebug(
                $"Prepared {matches.Count} blendshape baseline(s) for renderer mesh replacement " +
                $"on {destination.name}; restored {restored} before replacing {state.OldMeshName}.");
            return state;
        }

        internal void CompleteRendererMeshReplacement(RendererMeshReplacementState state,
            SkinnedMeshRenderer source, SkinnedMeshRenderer destination)
        {
            if (state == null || state.Controller != this || state.Destination != destination ||
                source == null || source.sharedMesh == null ||
                destination == null || destination.sharedMesh == null)
                return;

            int blendShapeCount = Mathf.Min(
                source.sharedMesh.blendShapeCount,
                destination.sharedMesh.blendShapeCount);
            var resetIndices = new HashSet<int>(
                state.ShapeIndices.Where(x => x >= 0 && x < blendShapeCount));
            foreach (string shapeName in state.ShapeNames.Distinct())
            {
                int index = BlendShapeUtilities.FindBlendShapeIndex(destination, shapeName);
                if (index >= 0 && index < blendShapeCount)
                    resetIndices.Add(index);
            }

            foreach (int index in resetIndices)
                destination.SetBlendShapeWeight(index, source.GetBlendShapeWeight(index));

            MakerBlendShapeSyncPlugin.Log?.LogDebug(
                $"Reset {resetIndices.Count} inherited blendshape slot(s) on {destination.name} " +
                $"after Uncensor Selector changed {state.OldMeshName} to {destination.sharedMesh.name}.");
            ScheduleApply();
        }

        private static bool TryRestoreBaseline(BaselineState state)
        {
            if (state == null || state.Renderer == null || state.Mesh == null ||
                state.Renderer.sharedMesh != state.Mesh)
                return false;

            int index = BlendShapeUtilities.FindBlendShapeIndex(state.Renderer, state.ShapeName);
            if (index < 0)
                return false;

            state.Renderer.SetBlendShapeWeight(index, state.Weight);
            return true;
        }

        private void PruneInvalidBaselines()
        {
            var invalidKeys = _baselines
                .Where(x => x.Value == null || x.Value.Renderer == null ||
                            x.Value.Mesh == null || x.Value.Renderer.sharedMesh != x.Value.Mesh)
                .Select(x => x.Key)
                .ToList();
            foreach (string key in invalidKeys)
                _baselines.Remove(key);
        }

        private static void LogBaselineRestore(string reason, int restored, int total)
        {
            if (total <= 0)
                return;

            MakerBlendShapeSyncPlugin.Log?.LogDebug(
                $"Restored {restored}/{total} blendshape baseline(s) for {reason}.");
        }

        private void NormalizeRecords()
        {
            if (Records.Count == 0 || ChaControl == null)
                return;

            var normalized = new List<BlendShapeRecord>();
            int coordinateCount = GetCoordinateCount();

            foreach (var source in Records
                         .OrderBy(x => x.Coordinate == CurrentCoordinateIndex ? 1 : 0)
                         .ToList())
            {
                var renderer = BlendShapeUtilities.FindRenderer(ChaControl.transform, source);
                BlendShapeTargetScope scope = renderer != null
                    ? GetEffectiveTargetScope(renderer)
                    : source.TargetScope;

                if (scope == BlendShapeTargetScope.Unknown)
                    scope = BlendShapeUtilities.InferLegacyScope(source);

                if (scope == BlendShapeTargetScope.Character)
                {
                    var record = CloneRecord(source, scope, -1);
                    record.Slot = -1;
                    record.SlotRelativePath = "";
                    AddNormalizedRecord(normalized, record);
                }
                else if (scope == BlendShapeTargetScope.CharacterCoordinate)
                {
                    if (source.Coordinate < 0)
                    {
                        for (int coordinate = 0; coordinate < coordinateCount; coordinate++)
                            AddNormalizedRecord(normalized,
                                CloneBodyRecord(source, scope, coordinate));
                    }
                    else
                    {
                        AddNormalizedRecord(normalized,
                            CloneBodyRecord(source, scope, source.Coordinate));
                    }
                }
                else if (scope == BlendShapeTargetScope.Clothing ||
                         scope == BlendShapeTargetScope.Accessory)
                {
                    source.TargetScope = scope;
                    BlendShapeUtilities.TryPopulateSlotIdentity(ChaControl, source);
                    if (source.Coordinate < 0)
                    {
                        for (int coordinate = 0; coordinate < coordinateCount; coordinate++)
                            AddNormalizedRecord(normalized, CloneRecord(source, scope, coordinate));
                    }
                    else
                    {
                        AddNormalizedRecord(normalized, CloneRecord(source, scope, source.Coordinate));
                    }
                }
                else
                {
                    AddNormalizedRecord(normalized, CloneRecord(source, BlendShapeTargetScope.Unknown,
                        source.Coordinate));
                }
            }

            Records.Clear();
            Records.AddRange(normalized);
        }

        private static BlendShapeRecord CloneRecord(BlendShapeRecord source,
            BlendShapeTargetScope scope, int coordinate)
        {
            return new BlendShapeRecord
            {
                TargetScope = scope,
                Coordinate = coordinate,
                Slot = source.Slot,
                SlotRelativePath = source.SlotRelativePath,
                RendererPath = source.RendererPath,
                RendererName = source.RendererName,
                MeshName = source.MeshName,
                ShapeName = source.ShapeName,
                Weight = source.Weight
            };
        }

        private static void AddNormalizedRecord(List<BlendShapeRecord> records, BlendShapeRecord record)
        {
            records.RemoveAll(x => IsSameRecord(x, record));
            records.Add(record);
        }

        private void RemoveMatchingRecord(BlendShapeRecord record)
        {
            Records.RemoveAll(x => IsSameRecord(x, record));
        }

        private static bool IsSameRecord(BlendShapeRecord left, BlendShapeRecord right)
        {
            if (left.TargetScope != right.TargetScope || left.Coordinate != right.Coordinate ||
                left.MeshName != right.MeshName || left.ShapeName != right.ShapeName)
                return false;

            if (left.Slot >= 0 && right.Slot >= 0)
                return left.Slot == right.Slot && left.SlotRelativePath == right.SlotRelativePath;
            return left.RendererPath == right.RendererPath;
        }

        private static BlendShapeRecord CloneForSlot(BlendShapeRecord source, int coordinate, int slot)
        {
            var clone = CloneRecord(source, source.TargetScope, coordinate);
            clone.Slot = slot;
            return clone;
        }

        private static BlendShapeRecord CloneBodyRecord(BlendShapeRecord source,
            BlendShapeTargetScope scope, int coordinate)
        {
            var clone = CloneRecord(source, scope, coordinate);
            clone.Slot = -1;
            clone.SlotRelativePath = "";
            return clone;
        }

        private int GetCoordinateCount()
        {
            int count = ChaControl.chaFile?.coordinate?.Length ?? 0;
            return count > 0 ? count : Mathf.Max(CurrentCoordinateIndex + 1, 1);
        }

        private int BuildTopologyFingerprint()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (ChaControl == null ? -1 : CurrentCoordinateIndex);
                if (ChaControl == null)
                    return hash;

                foreach (var renderer in BlendShapeUtilities.EnumerateRenderers(ChaControl.transform))
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

        private static string MakeBaselineKey(SkinnedMeshRenderer renderer, string shapeName, int coordinate)
        {
            int meshId = renderer.sharedMesh == null ? 0 : renderer.sharedMesh.GetInstanceID();
            return renderer.GetInstanceID() + "|" + meshId + "|" + coordinate + "|" + shapeName;
        }

        private void LoadFromExtendedData()
        {
            Records.Clear();
            PerCoordinateRenderers.Clear();
            var data = ExtendedSave.GetExtendedDataById(LoadedChaFile, MakerBlendShapeSyncPlugin.DataId);
            var bytes = data?.data != null &&
                        data.data.TryGetValue(DataKey, out var value)
                ? value as byte[]
                : null;
            if (bytes != null)
            {
                var unpacked = MessagePackSerializer.Deserialize<BlendShapeSyncData>(bytes);
                if (unpacked?.Records != null)
                    Records.AddRange(unpacked.Records);
                if (unpacked?.PerCoordinateRenderers != null)
                    PerCoordinateRenderers.AddRange(unpacked.PerCoordinateRenderers);
            }

            NormalizeRecords();
            MakerBlendShapeSyncPlugin.Log?.LogDebug(
                $"Loaded {Records.Count} blendshape record(s) from character ExtendedData.");
        }

        private void SaveToExtendedData()
        {
            NormalizeRecords();
            if (Records.Count == 0 && PerCoordinateRenderers.Count == 0)
            {
                SetExtendedData(null);
                return;
            }

            var data = new PluginData { version = DataVersion };
            data.data[DataKey] = MessagePackSerializer.Serialize(
                new BlendShapeSyncData
                {
                    Records = Records,
                    PerCoordinateRenderers = PerCoordinateRenderers
                });
            SetExtendedData(data);
            MakerBlendShapeSyncPlugin.Log?.LogDebug(
                $"Saved {Records.Count} blendshape record(s) to character ExtendedData.");
        }

        private void SaveCoordinateRecordsTo(ChaFileCoordinate coordinate, int sourceCoordinate)
        {
            if (coordinate == null)
                return;

            NormalizeRecords();
            var records = Records.Where(x =>
                    (x.TargetScope == BlendShapeTargetScope.Clothing ||
                     x.TargetScope == BlendShapeTargetScope.Accessory) &&
                    x.Coordinate == sourceCoordinate)
                .Select(x => CloneRecord(x, x.TargetScope, 0)).ToList();

            if (records.Count == 0)
            {
                SetCoordinateExtendedData(coordinate, null);
                return;
            }

            var data = new PluginData { version = DataVersion };
            data.data[DataKey] = MessagePackSerializer.Serialize(
                new BlendShapeSyncData { Records = records });
            SetCoordinateExtendedData(coordinate, data);
        }

        private void LoadCoordinateRecordsFrom(ChaFileCoordinate coordinate, int targetCoordinate,
            bool loadClothing, bool loadAccessories)
        {
            var loaded = ReadCoordinateRecords(coordinate);
            Records.RemoveAll(x => x.Coordinate == targetCoordinate &&
                ((loadClothing && x.TargetScope == BlendShapeTargetScope.Clothing) ||
                 (loadAccessories && x.TargetScope == BlendShapeTargetScope.Accessory)));

            if (loaded != null)
            {
                foreach (var source in loaded)
                {
                    if ((source.TargetScope == BlendShapeTargetScope.Clothing && !loadClothing) ||
                        (source.TargetScope == BlendShapeTargetScope.Accessory && !loadAccessories) ||
                        (source.TargetScope != BlendShapeTargetScope.Clothing &&
                         source.TargetScope != BlendShapeTargetScope.Accessory))
                        continue;

                    AddNormalizedRecord(Records,
                        CloneRecord(source, source.TargetScope, targetCoordinate));
                }
            }

            NormalizeRecords();
        }

        private List<BlendShapeRecord> ReadCoordinateRecords(ChaFileCoordinate coordinate)
        {
            if (coordinate == null)
                return null;

            var data = GetCoordinateExtendedData(coordinate);
            if (data?.data == null || !data.data.TryGetValue(DataKey, out var value) ||
                !(value is byte[] bytes))
                return null;

            var unpacked = MessagePackSerializer.Deserialize<BlendShapeSyncData>(bytes);
            return unpacked?.Records ?? new List<BlendShapeRecord>();
        }
    }
}
