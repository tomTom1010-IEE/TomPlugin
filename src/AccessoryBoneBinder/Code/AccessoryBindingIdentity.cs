using System;
using System.Collections.Generic;
using UnityEngine;

namespace AccessoryBoneBinder
{
    internal sealed class AccessoryBindingTarget
    {
        public int Slot;
        public int ImplantIndex;
        public string SourceRelativePath;
        public string OriginalBoneName;
        public string TargetBoneName;
        public string AliasBoneName;
        public Transform SourceTransform;
    }

    internal static class AccessoryBindingIdentity
    {
        private const string AliasPrefix = "ABB_S";

        internal static string CreateAlias(int slot, string sourceRelativePath, string originalBoneName,
            string targetBoneName, int implantIndex)
        {
            string readableName = Sanitize(originalBoneName);
            string identity = (sourceRelativePath ?? "") + "|" + (originalBoneName ?? "") + "|" +
                              (targetBoneName ?? "") + "|" + implantIndex;
            return GetSlotPrefix(slot) + readableName + "_" + ComputeFnv1a(identity).ToString("X8");
        }

        internal static string GetSlotPrefix(int slot)
        {
            return AliasPrefix + (slot + 1).ToString("D2") + "_";
        }

        internal static bool IsAliasForSlot(string boneName, int slot)
        {
            return !string.IsNullOrEmpty(boneName) &&
                   boneName.StartsWith(GetSlotPrefix(slot), StringComparison.Ordinal);
        }

        internal static string RemapAliasToSlot(string alias, int sourceSlot, int destinationSlot)
        {
            string sourcePrefix = GetSlotPrefix(sourceSlot);
            if (string.IsNullOrEmpty(alias) || !alias.StartsWith(sourcePrefix, StringComparison.Ordinal))
                return null;
            return GetSlotPrefix(destinationSlot) + alias.Substring(sourcePrefix.Length);
        }

        internal static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null)
                return target == null ? "" : target.name;
            if (root == target)
                return "";

            var parts = new Stack<string>();
            var current = target;
            while (current != null && current != root)
            {
                parts.Push(current.name);
                current = current.parent;
            }

            return current == root ? string.Join("/", parts.ToArray()) : target.name;
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "bone";

            var chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_')
                    chars[i] = '_';
            }

            string result = new string(chars);
            return result.Length <= 40 ? result : result.Substring(0, 40);
        }

        private static uint ComputeFnv1a(string value)
        {
            uint hash = 2166136261;
            foreach (char character in value)
            {
                hash ^= character;
                hash *= 16777619;
            }
            return hash;
        }
    }
}
