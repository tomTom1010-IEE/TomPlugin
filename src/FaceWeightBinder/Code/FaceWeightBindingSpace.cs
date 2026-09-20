using System.Collections.Generic;
using UnityEngine;

namespace FaceWeightBinder
{
    internal static class FaceWeightBindingSpace
    {
        internal static bool IsAssetRenderer(SkinnedMeshRenderer renderer,
            IEnumerable<GameObject> assetRoots)
        {
            foreach (var root in assetRoots)
                if (root != null && (renderer.transform == root.transform ||
                                     renderer.transform.IsChildOf(root.transform)))
                    return true;

            // Also reject marked assets that are temporarily outside the slot arrays.
            for (var current = renderer.transform; current != null; current = current.parent)
                if (current.GetComponent<FaceWeightProcess>() != null)
                    return true;
            return false;
        }

        internal static Matrix4x4 SourceToReference(Transform assetRoot,
            Transform renderer, Transform reference)
        {
            // Compose only inside the asset. Character motion and accessory mounting
            // transforms cancel exactly instead of introducing world-matrix roundoff.
            return ToAssetRoot(assetRoot, reference).inverse * ToAssetRoot(assetRoot, renderer);
        }

        private static Matrix4x4 ToAssetRoot(Transform root, Transform child)
        {
            var matrix = Matrix4x4.identity;
            for (var current = child; current != root; current = current.parent)
            {
                if (current == null)
                    throw new System.InvalidOperationException("Face renderer/reference is outside its asset root.");
                matrix = Matrix4x4.TRS(current.localPosition, current.localRotation,
                    current.localScale) * matrix;
            }
            return matrix;
        }

        internal static bool Approximately(Matrix4x4 left, Matrix4x4 right)
        {
            for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++)
                {
                    float a = left[row, column];
                    float b = right[row, column];
                    if (float.IsNaN(a) || float.IsNaN(b) ||
                        float.IsInfinity(a) || float.IsInfinity(b) ||
                        Mathf.Abs(a - b) > 0.000001f *
                        Mathf.Max(1f, Mathf.Max(Mathf.Abs(a), Mathf.Abs(b))))
                        return false;
                }
            return true;
        }
    }
}
