using System.Collections;
using System.Collections.Generic;
using System.Linq;
using KKAPI;
using KKAPI.Chara;
using ModBoneImplantor;
using UnityEngine;

namespace AccessoryBoneBinder
{
    public sealed class AccessoryBoneBinderController : CharaCustomFunctionController
    {
        private readonly List<BoundAccessoryBone> _boundBones = new List<BoundAccessoryBone>();
        private readonly Dictionary<string, AccessoryBindingTarget> _pendingAbmxRefreshBones =
            new Dictionary<string, AccessoryBindingTarget>();
        private Coroutine _scheduledRebind;
        private Coroutine _scheduledAbmxRefresh;

        protected override void OnReload(GameMode currentGameMode, bool maintainState)
        {
            if (!maintainState)
                ReleaseBoundBones("character reload");
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
            if (_scheduledAbmxRefresh != null)
                StopCoroutine(_scheduledAbmxRefresh);
            ReleaseBoundBones("controller destroy");
            base.OnDestroy();
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

            return RebindAccessories();
        }

        internal void AccessoryKindChangedEvent(int slot)
        {
            AbmxBridge.ClearSlotCurrentCoordinateIfAvailable(ChaControl, slot, CurrentCoordinateIndex);
            ScheduleRebind();
        }

        internal void AccessoryTransferredEvent(int sourceSlot, int destinationSlot)
        {
            AbmxBridge.CopySlotCurrentCoordinateIfAvailable(
                ChaControl, sourceSlot, destinationSlot, CurrentCoordinateIndex);
            ScheduleRebind();
        }

        internal void AccessoriesCopiedEvent(int sourceCoordinate, int destinationCoordinate,
            IEnumerable<int> copiedSlots)
        {
            AbmxBridge.CopySlotsAcrossCoordinatesIfAvailable(
                ChaControl, sourceCoordinate, destinationCoordinate, copiedSlots);
            if (CurrentCoordinateIndex == destinationCoordinate)
                ScheduleRebind();
        }

        private int CurrentCoordinateIndex => ChaControl == null ? 0 : ChaControl.fileStatus.coordinateType;

        private IEnumerator DelayedRebind()
        {
            yield return null;
            yield return null;
            _scheduledRebind = null;
            RebindAccessories();
        }

        private bool RebindAccessories()
        {
            if (ChaControl == null) return false;
            var accessories = ChaControl.objAccessory;
            if (accessories == null) return false;

            CleanupDeadBindings(accessories);
            var bodyBones = BuildBodyBoneDictionary();
            if (bodyBones.Count == 0)
            {
                AccessoryBoneBinderPlugin.Log?.LogWarning($"No body bones found for {ChaControl.name}; accessory bones cannot be bound.");
                return false;
            }

            bool anyBoundOrRebound = false;
            var changedBones = new Dictionary<string, AccessoryBindingTarget>();
            for (int slot = 0; slot < accessories.Length; slot++)
            {
                var accessoryRoot = accessories[slot];
                if (accessoryRoot == null) continue;
                anyBoundOrRebound |= BindAccessory(slot, accessoryRoot, bodyBones, changedBones);
            }

            if (anyBoundOrRebound)
                ScheduleAbmxRefresh(changedBones);

            return anyBoundOrRebound;
        }

        private bool BindAccessory(int slot, GameObject accessoryRoot, Dictionary<string, Transform> bodyBones,
            Dictionary<string, AccessoryBindingTarget> changedBones)
        {
            var implants = accessoryRoot.GetComponentsInChildren<BoneImplantProcess>(true);
            if (implants == null || implants.Length == 0) return false;

            bool changed = false;
            for (int implantIndex = 0; implantIndex < implants.Length; implantIndex++)
            {
                var implant = implants[implantIndex];
                if (implant == null) continue;
                var source = implant.trfSrc;
                var destination = implant.trfDst;
                if (source == null || destination == null || source == destination)
                {
                    AccessoryBoneBinderPlugin.Log?.LogWarning($"Invalid BoneImplantProcess in slot {slot + 1}: trfSrc/trfDst is null or identical.");
                    continue;
                }

                string targetName = destination.name;
                if (!bodyBones.TryGetValue(targetName, out var bodyParent) || bodyParent == null)
                {
                    AccessoryBoneBinderPlugin.Log?.LogWarning($"Body bone '{targetName}' was not found for accessory slot {slot + 1}, source '{source.name}'.");
                    continue;
                }

                if (TryRefreshExistingBinding(source, accessoryRoot, slot, bodyParent,
                        out var existingBinding))
                {
                    changed = true;
                    changedBones[existingBinding.AliasBoneName] = existingBinding.ToTarget();
                    continue;
                }

                string originalBoneName = source.name;
                string sourceRelativePath = AccessoryBindingIdentity.GetRelativePath(
                    accessoryRoot.transform, source);
                string aliasBoneName = AccessoryBindingIdentity.CreateAlias(
                    slot, sourceRelativePath, originalBoneName, targetName, implantIndex);

                CleanupDuplicateBodyChildren(aliasBoneName, source, bodyParent);
                source.name = aliasBoneName;

                var boundBone = new BoundAccessoryBone
                {
                    Slot = slot,
                    ImplantIndex = implantIndex,
                    AccessoryRoot = accessoryRoot,
                    SourceRoot = source,
                    SourceRelativePath = sourceRelativePath,
                    OriginalBoneName = originalBoneName,
                    AliasBoneName = aliasBoneName,
                    OriginalParent = source.parent,
                    OriginalLocalPosition = source.localPosition,
                    OriginalLocalRotation = source.localRotation,
                    OriginalLocalScale = source.localScale,
                    BodyParent = bodyParent,
                    TargetBoneName = targetName
                };
                _boundBones.Add(boundBone);

                source.SetParent(bodyParent, false);
                changed = true;
                changedBones[aliasBoneName] = boundBone.ToTarget();
                AccessoryBoneBinderPlugin.Log?.LogInfo(
                    $"Bound accessory slot {slot + 1}: {originalBoneName} -> {targetName} as {aliasBoneName}");
            }

            return changed;
        }

        private bool TryRefreshExistingBinding(Transform source, GameObject accessoryRoot, int slot,
            Transform bodyParent, out BoundAccessoryBone existingBinding)
        {
            existingBinding = null;
            foreach (var bound in _boundBones)
            {
                if (bound.SourceRoot == source)
                {
                    existingBinding = bound;
                    source.name = bound.AliasBoneName;
                    if (source.parent != bodyParent)
                    {
                        source.SetParent(bodyParent, false);
                        bound.BodyParent = bodyParent;
                        AccessoryBoneBinderPlugin.Log?.LogInfo(
                            $"Rebound accessory slot {slot + 1}: {bound.OriginalBoneName} -> {bodyParent.name}");
                    }
                    bound.AccessoryRoot = accessoryRoot;
                    bound.Slot = slot;
                    return true;
                }
            }

            return false;
        }

        private void ReleaseBoundBones(string reason)
        {
            if (_scheduledAbmxRefresh != null)
            {
                StopCoroutine(_scheduledAbmxRefresh);
                _scheduledAbmxRefresh = null;
            }
            _pendingAbmxRefreshBones.Clear();

            for (int i = _boundBones.Count - 1; i >= 0; i--)
            {
                var bound = _boundBones[i];
                if (bound.SourceRoot != null)
                {
                    AccessoryBoneBinderPlugin.Log?.LogDebug($"Removing bound accessory bone {bound.SourceRoot.name} during {reason}.");
                    Destroy(bound.SourceRoot.gameObject);
                }
                _boundBones.RemoveAt(i);
            }
        }

        private void CleanupDuplicateBodyChildren(string aliasBoneName, Transform source, Transform bodyParent)
        {
            if (source == null || bodyParent == null)
                return;

            for (int i = bodyParent.childCount - 1; i >= 0; i--)
            {
                var child = bodyParent.GetChild(i);
                if (child == null || child == source || child.name != aliasBoneName)
                    continue;
                if (_boundBones.Any(x => x.SourceRoot == child))
                    continue;

                AccessoryBoneBinderPlugin.Log?.LogDebug(
                    $"Removing stale duplicate accessory bone {child.name} under {bodyParent.name}.");
                Destroy(child.gameObject);
            }
        }

        private void ScheduleAbmxRefresh(IDictionary<string, AccessoryBindingTarget> changedBones)
        {
            foreach (var bone in changedBones)
                _pendingAbmxRefreshBones[bone.Key] = bone.Value;
            RequestAbmxRefresh();
            if (_scheduledAbmxRefresh != null)
                StopCoroutine(_scheduledAbmxRefresh);
            _scheduledAbmxRefresh = StartCoroutine(DelayedAbmxRefresh());
        }

        private IEnumerator DelayedAbmxRefresh()
        {
            yield return null;
            yield return null;
            _scheduledAbmxRefresh = null;
            RequestAbmxRefresh();
            _pendingAbmxRefreshBones.Clear();
        }

        private void RequestAbmxRefresh()
        {
            if (_pendingAbmxRefreshBones.Count == 0)
                return;

            if (AbmxBridge.RequestRefreshIfAvailable(ChaControl, _pendingAbmxRefreshBones))
                AccessoryBoneBinderPlugin.Log?.LogDebug($"Requested ABMX refresh for {ChaControl.name} after accessory bone binding.");
        }

        private Dictionary<string, Transform> BuildBodyBoneDictionary()
        {
            var result = new Dictionary<string, Transform>();
            var root = ChaControl.objBodyBone != null ? ChaControl.objBodyBone.transform : ChaControl.transform;
            foreach (var bone in root.GetComponentsInChildren<Transform>(true))
            {
                if (bone == null || string.IsNullOrEmpty(bone.name) || result.ContainsKey(bone.name))
                    continue;
                result.Add(bone.name, bone);
            }
            return result;
        }

        private void CleanupDeadBindings(GameObject[] accessories)
        {
            for (int i = _boundBones.Count - 1; i >= 0; i--)
            {
                var bound = _boundBones[i];
                bool sourceAlive = bound.SourceRoot != null;
                bool slotStillSame = bound.Slot >= 0 &&
                                     bound.Slot < accessories.Length &&
                                     accessories[bound.Slot] == bound.AccessoryRoot &&
                                     bound.AccessoryRoot != null;

                if (sourceAlive && !slotStillSame)
                {
                    AccessoryBoneBinderPlugin.Log?.LogDebug($"Removing detached accessory bone {bound.SourceRoot.name} from old slot {bound.Slot + 1}.");
                    Destroy(bound.SourceRoot.gameObject);
                    sourceAlive = false;
                }

                if (!sourceAlive || !slotStillSame)
                    _boundBones.RemoveAt(i);
            }
        }

        private sealed class BoundAccessoryBone
        {
            public int Slot;
            public int ImplantIndex;
            public GameObject AccessoryRoot;
            public Transform SourceRoot;
            public string SourceRelativePath;
            public string OriginalBoneName;
            public string AliasBoneName;
            public Transform OriginalParent;
            public Vector3 OriginalLocalPosition;
            public Quaternion OriginalLocalRotation;
            public Vector3 OriginalLocalScale;
            public Transform BodyParent;
            public string TargetBoneName;

            public AccessoryBindingTarget ToTarget()
            {
                return new AccessoryBindingTarget
                {
                    Slot = Slot,
                    ImplantIndex = ImplantIndex,
                    SourceRelativePath = SourceRelativePath,
                    OriginalBoneName = OriginalBoneName,
                    TargetBoneName = TargetBoneName,
                    AliasBoneName = AliasBoneName,
                    SourceTransform = SourceRoot
                };
            }
        }
    }
}
