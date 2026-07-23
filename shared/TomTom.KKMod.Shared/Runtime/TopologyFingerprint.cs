using System.Collections.Generic;
using UnityEngine;

namespace TomTom.KKMod.Shared
{
    public static class TopologyFingerprint
    {
        public static int Build(IEnumerable<GameObject> roots)
        {
            unchecked
            {
                int hash = 17;
                if (roots == null)
                    return hash;

                foreach (var root in roots)
                {
                    hash = hash * 31 + (root == null ? 0 : root.GetInstanceID());
                    if (root == null)
                        continue;
                    foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        hash = hash * 31 + renderer.GetInstanceID();
                        hash = hash * 31 + (renderer.sharedMesh == null
                            ? 0
                            : renderer.sharedMesh.GetInstanceID());
                    }
                }
                return hash;
            }
        }
    }
}
