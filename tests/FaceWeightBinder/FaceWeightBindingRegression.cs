// Run in an empty Unity 5.6.2f1 project, under Assets/Editor, alongside
// production FaceWeightBindingSpace.cs. Put Shared/FaceWeightProcess.cs in
// Assets (outside Editor), so its MonoBehaviour can be attached in the test.
// Unity.exe -batchmode -nographics -quit -projectPath <project>
//           -executeMethod FaceWeightBindingRegression.Run -logFile <log>
using System;
using FaceWeightBinder;
using UnityEngine;

public static class FaceWeightBindingRegression
{
    public static void Run()
    {
        var character = new GameObject("character");
        try
        {
            var face = Child(character.transform, "cf_O_face").AddComponent<SkinnedMeshRenderer>();
            var clothing = Child(character.transform, "clothing");
            var accessory = Child(character.transform, "accessory");
            var roots = new[] { clothing, accessory };
            var clothingFace = Child(clothing.transform, "cf_O_face").AddComponent<SkinnedMeshRenderer>();
            var accessoryFace = accessory.AddComponent<SkinnedMeshRenderer>();
            Check(!FaceWeightBindingSpace.IsAssetRenderer(face, roots), "real face outside objHead remains eligible");
            Check(FaceWeightBindingSpace.IsAssetRenderer(clothingFace, roots), "nested clothing face excluded despite name");
            Check(FaceWeightBindingSpace.IsAssetRenderer(accessoryFace, roots), "slot-root renderer excluded");
            var detached = Child(character.transform, "detached mask");
            detached.AddComponent<FaceWeightProcess>();
            var marked = Child(detached.transform, "cf_O_face").AddComponent<SkinnedMeshRenderer>();
            Check(FaceWeightBindingSpace.IsAssetRenderer(marked, roots), "marked asset outside slot arrays excluded");

            var mesh = Child(detached.transform, "mesh").transform;
            var reference = Child(detached.transform, "cf_O_face_space").transform;
            mesh.localPosition = new Vector3(0.03f, 0.05f, -0.02f);
            mesh.localRotation = Quaternion.Euler(3f, 7f, 11f);
            mesh.localScale = new Vector3(1.1f, 0.9f, 1.05f);
            reference.localPosition = new Vector3(0f, 0.1f, 0f);
            var initial = FaceWeightBindingSpace.SourceToReference(detached.transform, mesh, reference);
            Check(FaceWeightBindingSpace.Approximately(initial,
                reference.worldToLocalMatrix * mesh.localToWorldMatrix), "same conversion as prior formula");

            character.transform.position = new Vector3(10000f, -3000f, 17f);
            character.transform.rotation = Quaternion.Euler(41f, 23f, 85f);
            detached.transform.localScale = new Vector3(2f, 3f, 4f);
            Check(FaceWeightBindingSpace.Approximately(initial,
                FaceWeightBindingSpace.SourceToReference(detached.transform, mesh, reference)),
                "character and mounting transforms do not invalidate cache");
            var targetBone = Child(character.transform, "cf_J_NoseBase").transform;
            targetBone.localPosition = new Vector3(0f, 0f, 0.02f);
            targetBone.localScale = new Vector3(1f, 2f, 1f);
            Check(FaceWeightBindingSpace.Approximately(initial,
                FaceWeightBindingSpace.SourceToReference(detached.transform, mesh, reference)),
                "target facial deformation does not invalidate cache");

            var originalPosition = mesh.localPosition;
            mesh.localPosition += new Vector3(0.001f, 0f, 0f);
            Changed(initial, detached.transform, mesh, reference, "local translation invalidates cache");
            mesh.localPosition = originalPosition;
            var originalRotation = mesh.localRotation;
            mesh.localRotation *= Quaternion.Euler(0f, 1f, 0f);
            Changed(initial, detached.transform, mesh, reference, "local rotation invalidates cache");
            mesh.localRotation = originalRotation;
            var originalScale = mesh.localScale;
            mesh.localScale *= 1.01f;
            Changed(initial, detached.transform, mesh, reference, "local scale invalidates cache");
            mesh.localScale = originalScale;
            reference.localPosition += new Vector3(0f, 0.001f, 0f);
            Changed(initial, detached.transform, mesh, reference, "reference motion invalidates cache");

            var noise = initial;
            noise.m03 += 0.0000001f;
            Check(FaceWeightBindingSpace.Approximately(initial, noise), "roundoff ignored");
            Debug.Log("FACE_WEIGHT_REGRESSION_PASS (12 checks)");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(character);
        }
    }

    private static GameObject Child(Transform parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child;
    }

    private static void Changed(Matrix4x4 previous, Transform root, Transform mesh,
        Transform reference, string description)
    {
        Check(!FaceWeightBindingSpace.Approximately(previous,
            FaceWeightBindingSpace.SourceToReference(root, mesh, reference)), description);
    }

    private static void Check(bool passed, string description)
    {
        if (!passed) throw new Exception("FaceWeightBinder regression: " + description);
        Debug.Log("PASS: " + description);
    }
}
