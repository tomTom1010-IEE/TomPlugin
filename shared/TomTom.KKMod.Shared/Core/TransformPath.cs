using System.Collections.Generic;
using UnityEngine;

namespace TomTom.KKMod.Shared
{
    public static class TransformPath
    {
        public static string GetRelativePath(Transform root, Transform target)
        {
            if (target == null)
                return string.Empty;
            if (root == null)
                return target.name;
            if (root == target)
                return string.Empty;

            var parts = new Stack<string>();
            var current = target;
            while (current != null && current != root)
            {
                parts.Push(current.name);
                current = current.parent;
            }
            return current == root ? string.Join("/", parts.ToArray()) : target.name;
        }

        public static Transform Find(Transform root, string relativePath)
        {
            if (root == null)
                return null;
            if (string.IsNullOrEmpty(relativePath))
                return root;
            return root.Find(relativePath);
        }
    }
}
