using System.Collections.Generic;
using System.Linq;
using FaceWeightBinder;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(FaceWeightProcess))]
public sealed class FaceWeightProcessEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();

        var process = (FaceWeightProcess)target;
        if (GUILayout.Button("Capture Renderer Bindings"))
            CaptureBindings(process);
        if (GUILayout.Button("Validate Bindings"))
            ValidateBindings(process, true);
    }

    private static void CaptureBindings(FaceWeightProcess process)
    {
        Undo.RecordObject(process, "Capture Face Weight Bindings");
        if (process.skeletonRoot == null)
        {
            process.skeletonRoot = FindChildByName(
                process.transform, "cf_J_N_FaceRoot") ??
                FindChildByName(process.transform, "cm_J_N_FaceRoot");
        }

        var renderers = process.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(x => x.sharedMesh != null).ToArray();
        var preferredRootBone = FindChildByName(process.transform, "cf_J_FaceRoot") ??
                                FindChildByName(process.transform, "cm_J_FaceRoot");
        var bindings = new FaceWeightRendererBinding[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (preferredRootBone != null &&
                (renderer.rootBone == null ||
                 (renderer.rootBone.name != "cf_J_FaceRoot" &&
                  renderer.rootBone.name != "cm_J_FaceRoot")))
            {
                Undo.RecordObject(renderer, "Assign Face Renderer Root Bone");
                renderer.rootBone = preferredRootBone;
                EditorUtility.SetDirty(renderer);
            }
            var bones = renderer.bones ?? new Transform[0];
            bindings[i] = new FaceWeightRendererBinding
            {
                renderer = renderer,
                rendererPath = GetRelativePath(process.transform, renderer.transform),
                sourceBones = bones,
                boneNames = bones.Select(x => x == null ? string.Empty : x.name).ToArray(),
                rootBoneName = renderer.rootBone == null
                    ? string.Empty
                    : renderer.rootBone.name
            };
        }

        process.schemaVersion = 1;
        process.bindings = bindings;
        EditorUtility.SetDirty(process);
        PrefabUtility.RecordPrefabInstancePropertyModifications(process);
        ValidateBindings(process, true);
    }

    private static bool ValidateBindings(FaceWeightProcess process, bool writeLog)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (process.skeletonRoot == null)
            errors.Add("skeletonRoot is not assigned.");
        if (process.bindings == null || process.bindings.Length == 0)
            errors.Add("No renderer bindings were captured.");

        if (process.bindings != null)
        {
            for (int i = 0; i < process.bindings.Length; i++)
            {
                var binding = process.bindings[i];
                if (binding == null || binding.renderer == null)
                {
                    errors.Add("Binding " + i + " has no renderer.");
                    continue;
                }

                var mesh = binding.renderer.sharedMesh;
                if (mesh == null)
                {
                    errors.Add("Binding " + i + " has no mesh.");
                    continue;
                }

                int boneCount = binding.boneNames == null ? 0 : binding.boneNames.Length;
                if (boneCount == 0)
                    errors.Add("Binding " + i + " has no baked bone names.");
                if (binding.sourceBones == null || binding.sourceBones.Length != boneCount)
                    errors.Add("Binding " + i + " sourceBones and boneNames differ in length.");
                if (mesh.bindposes == null || mesh.bindposes.Length != boneCount)
                    warnings.Add("Binding " + i + " bind pose count differs from bone count.");
                if (string.IsNullOrEmpty(binding.rootBoneName))
                    errors.Add("Binding " + i + " has no root bone name.");
                else if (binding.rootBoneName != "cf_J_FaceRoot" &&
                         binding.rootBoneName != "cm_J_FaceRoot")
                    errors.Add("Binding " + i + " root bone is " +
                               binding.rootBoneName +
                               "; expected cf_J_FaceRoot or cm_J_FaceRoot.");
                if (binding.boneNames != null && binding.boneNames.Any(string.IsNullOrEmpty))
                    errors.Add("Binding " + i + " contains an unnamed or missing bone.");

                var weights = mesh.boneWeights;
                int unweighted = weights.Count(x =>
                    x.weight0 + x.weight1 + x.weight2 + x.weight3 <= 0.0001f);
                if (unweighted > 0)
                    errors.Add("Binding " + i + " has " + unweighted +
                               " unweighted vertices.");
                if (mesh.blendShapeCount == 0)
                    warnings.Add("Binding " + i +
                                 " has no BlendShapes; only bone deformation will follow.");
            }
        }

        if (writeLog)
        {
            foreach (string error in errors)
                Debug.LogError("[FaceWeightBinder] " + error, process);
            foreach (string warning in warnings)
                Debug.LogWarning("[FaceWeightBinder] " + warning, process);
            if (errors.Count == 0)
                Debug.Log("[FaceWeightBinder] Validation passed for " +
                          (process.bindings == null ? 0 : process.bindings.Length) +
                          " renderer(s).", process);
        }
        return errors.Count == 0;
    }

    private static Transform FindChildByName(Transform root, string name)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform.name == name)
                return transform;
        }
        return null;
    }

    private static string GetRelativePath(Transform root, Transform target)
    {
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
}
