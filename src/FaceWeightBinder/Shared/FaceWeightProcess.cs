using System;
using UnityEngine;

namespace FaceWeightBinder
{
    [DisallowMultipleComponent]
    public sealed class FaceWeightProcess : MonoBehaviour
    {
        public int schemaVersion = 1;
        public Transform skeletonRoot;
        public FaceWeightRendererBinding[] bindings = new FaceWeightRendererBinding[0];
        public bool copyCharacterHeadBounds = true;
    }

    [Serializable]
    public sealed class FaceWeightRendererBinding
    {
        public SkinnedMeshRenderer renderer;
        public string rendererPath = string.Empty;
        public Transform[] sourceBones = new Transform[0];
        public string[] boneNames = new string[0];
        public string rootBoneName = string.Empty;
    }
}
