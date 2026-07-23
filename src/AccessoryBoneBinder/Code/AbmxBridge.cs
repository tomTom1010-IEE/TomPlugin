using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KKABMX.Core;
using UnityEngine;

namespace AccessoryBoneBinder
{
    internal static class AbmxBridgeImpl
    {
        private static readonly PropertyInfo BoneTransformProperty = typeof(BoneModifier).GetProperty(
            nameof(BoneModifier.BoneTransform), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly PropertyInfo BoneLocationProperty = typeof(BoneModifier).GetProperty(
            nameof(BoneModifier.BoneLocation), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public static bool RequestRefreshIfAvailable(ChaControl chaControl,
            IDictionary<string, AccessoryBindingTarget> reboundBones)
        {
            var abmx = GetController(chaControl);
            if (abmx == null)
                return false;

            var targets = reboundBones?.Values
                .Where(x => x != null && !string.IsNullOrEmpty(x.AliasBoneName) && x.SourceTransform != null)
                .GroupBy(x => x.AliasBoneName)
                .Select(x => x.Last())
                .ToList();
            if (targets == null || targets.Count == 0)
                return false;

            int coordinateCount = GetCoordinateCount(chaControl);
            foreach (var target in targets)
                PrepareModifier(abmx, target, coordinateCount);

            RequestFullRefresh(abmx);
            return true;
        }

        public static bool ClearSlotCurrentCoordinateIfAvailable(ChaControl chaControl, int slot, int coordinate)
        {
            var abmx = GetController(chaControl);
            if (abmx == null)
                return false;

            bool changed = false;
            int coordinateCount = GetCoordinateCount(chaControl);
            foreach (var modifier in abmx.GetAllModifiers()
                         .Where(x => AccessoryBindingIdentity.IsAliasForSlot(x.BoneName, slot)).ToList())
            {
                EnsureCoordinateSpecific(modifier, coordinateCount);
                changed |= ClearCoordinate(modifier, coordinate);
                modifier.ClearBaseline();
            }

            if (changed)
                RequestFullRefresh(abmx);
            return changed;
        }

        public static bool CopySlotCurrentCoordinateIfAvailable(ChaControl chaControl, int sourceSlot,
            int destinationSlot, int coordinate)
        {
            if (sourceSlot == destinationSlot)
                return false;
            var abmx = GetController(chaControl);
            if (abmx == null)
                return false;

            int coordinateCount = GetCoordinateCount(chaControl);
            var allModifiers = abmx.GetAllModifiers().ToList();
            foreach (var destination in allModifiers
                         .Where(x => AccessoryBindingIdentity.IsAliasForSlot(x.BoneName, destinationSlot)))
            {
                EnsureCoordinateSpecific(destination, coordinateCount);
                ClearCoordinate(destination, coordinate);
                destination.ClearBaseline();
            }

            bool changed = false;
            foreach (var source in allModifiers
                         .Where(x => AccessoryBindingIdentity.IsAliasForSlot(x.BoneName, sourceSlot)))
            {
                EnsureCoordinateSpecific(source, coordinateCount);
                var sourceData = GetCoordinateData(source, coordinate);
                if (sourceData == null || sourceData.IsEmpty())
                    continue;

                string destinationAlias = AccessoryBindingIdentity.RemapAliasToSlot(
                    source.BoneName, sourceSlot, destinationSlot);
                if (string.IsNullOrEmpty(destinationAlias))
                    continue;

                var destination = GetOrAddExactModifier(abmx, destinationAlias, BoneLocation.BodyTop);
                EnsureCoordinateSpecific(destination, coordinateCount);
                sourceData.CopyTo(GetCoordinateData(destination, coordinate));
                destination.ClearBaseline();
                changed = true;
            }

            if (changed)
                RequestFullRefresh(abmx);
            return changed;
        }

        public static bool CopySlotsAcrossCoordinatesIfAvailable(ChaControl chaControl, int sourceCoordinate,
            int destinationCoordinate, IEnumerable<int> copiedSlots)
        {
            if (sourceCoordinate == destinationCoordinate)
                return false;
            var abmx = GetController(chaControl);
            if (abmx == null || copiedSlots == null)
                return false;

            int coordinateCount = GetCoordinateCount(chaControl);
            bool changed = false;
            var modifiers = abmx.GetAllModifiers().ToList();
            foreach (int slot in copiedSlots.Distinct())
            {
                foreach (var modifier in modifiers
                             .Where(x => AccessoryBindingIdentity.IsAliasForSlot(x.BoneName, slot)))
                {
                    EnsureCoordinateSpecific(modifier, coordinateCount);
                    var sourceData = GetCoordinateData(modifier, sourceCoordinate);
                    var destinationData = GetCoordinateData(modifier, destinationCoordinate);
                    if (sourceData == null || destinationData == null)
                        continue;

                    sourceData.CopyTo(destinationData);
                    modifier.ClearBaseline();
                    changed = true;
                }
            }

            if (changed)
                RequestFullRefresh(abmx);
            return changed;
        }

        private static void PrepareModifier(BoneController abmx, AccessoryBindingTarget target,
            int coordinateCount)
        {
            var aliasModifier = GetOrAddExactModifier(abmx, target.AliasBoneName, BoneLocation.BodyTop);
            var legacyModifiers = abmx.GetAllModifiers()
                .Where(x => !ReferenceEquals(x, aliasModifier) &&
                            x.BoneName == target.OriginalBoneName).ToList();
            var legacyModifier = legacyModifiers.FirstOrDefault(x => !x.IsEmpty());

            if (aliasModifier.IsEmpty() && legacyModifier != null)
            {
                aliasModifier.CoordinateModifiers = legacyModifier.CoordinateModifiers
                    .Select(x => x.Clone()).ToArray();
                AccessoryBoneBinderPlugin.Log?.LogDebug(
                    $"Migrated legacy ABMX modifier '{target.OriginalBoneName}' to '{target.AliasBoneName}'.");
            }

            foreach (var legacy in legacyModifiers)
                abmx.RemoveModifier(legacy);

            EnsureCoordinateSpecific(aliasModifier, coordinateCount);
            ForceAssignBodyModifier(aliasModifier, target.SourceTransform);

            foreach (var duplicate in abmx.GetAllModifiers()
                         .Where(x => !ReferenceEquals(x, aliasModifier) &&
                                     x.BoneName == target.AliasBoneName).ToList())
            {
                abmx.RemoveModifier(duplicate);
            }

            AccessoryBoneBinderPlugin.Log?.LogDebug(
                $"Prepared ABMX modifier '{target.AliasBoneName}' for slot {target.Slot + 1}.");
        }

        private static BoneModifier GetOrAddExactModifier(BoneController abmx, string boneName,
            BoneLocation location)
        {
            var modifier = abmx.GetAllModifiers(location).FirstOrDefault(x => x.BoneName == boneName);
            if (modifier != null)
                return modifier;

            modifier = new BoneModifier(boneName, location);
            abmx.AddModifier(modifier);
            return modifier;
        }

        private static void EnsureCoordinateSpecific(BoneModifier modifier, int coordinateCount)
        {
            if (modifier == null)
                return;
            if (modifier.CoordinateModifiers == null || modifier.CoordinateModifiers.Length == 0)
                modifier.CoordinateModifiers = new[] { new BoneModifierData() };
            if (coordinateCount > 1)
                modifier.MakeCoordinateSpecific(coordinateCount);
        }

        private static BoneModifierData GetCoordinateData(BoneModifier modifier, int coordinate)
        {
            if (modifier?.CoordinateModifiers == null || modifier.CoordinateModifiers.Length == 0)
                return null;
            if (modifier.CoordinateModifiers.Length == 1)
                return modifier.CoordinateModifiers[0];
            return coordinate >= 0 && coordinate < modifier.CoordinateModifiers.Length
                ? modifier.CoordinateModifiers[coordinate]
                : null;
        }

        private static bool ClearCoordinate(BoneModifier modifier, int coordinate)
        {
            var data = GetCoordinateData(modifier, coordinate);
            if (data == null || data.IsEmpty())
                return false;
            data.Clear();
            return true;
        }

        private static int GetCoordinateCount(ChaControl chaControl)
        {
            return chaControl?.chaFile?.coordinate == null
                ? 1
                : Mathf.Max(chaControl.chaFile.coordinate.Length, 1);
        }

        private static BoneController GetController(ChaControl chaControl)
        {
            return chaControl == null ? null : chaControl.GetComponent<BoneController>();
        }

        private static void RequestFullRefresh(BoneController abmx)
        {
            abmx.BoneSearcher?.ClearCache(false);
            abmx.NeedsFullRefresh = true;
        }

        private static void ForceAssignBodyModifier(BoneModifier modifier, Transform transform)
        {
            BoneLocationProperty?.SetValue(modifier, BoneLocation.BodyTop, null);
            BoneTransformProperty?.SetValue(modifier, transform, null);
            modifier.ClearBaseline();
        }

        private static void ClearModifierData(BoneModifier modifier)
        {
            if (modifier?.CoordinateModifiers == null)
                return;
            modifier.CoordinateModifiers = modifier.CoordinateModifiers
                .Select(_ => new BoneModifierData()).ToArray();
            modifier.ClearBaseline();
        }
    }
}
