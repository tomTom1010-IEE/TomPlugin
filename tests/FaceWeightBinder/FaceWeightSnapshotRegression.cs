// In an isolated Unity 5.6 editor test project, compile with the production
// FaceWeightSnapshot.cs and test-only stubs for BepInEx.Paths / plugin Version.
using System;
using System.IO;
using System.Xml;
using FaceWeightBinder;
using UnityEngine;

public static class FaceWeightSnapshotRegression
{
    public static void Run()
    {
        var character = new GameObject("snapshot test");
        var mesh = new Mesh();
        try
        {
            character.transform.position = new Vector3(1f, 2f, 3f);
            character.transform.rotation = Quaternion.Euler(12f, 23f, 34f);
            character.transform.localScale = new Vector3(0.8f, 1.2f, 0.9f);
            var bone = new GameObject("bone").transform;
            bone.SetParent(character.transform, false);
            bone.localPosition = new Vector3(0.1f, 0.2f, 0.3f);
            var asset = new GameObject("asset");
            asset.transform.SetParent(character.transform, false);
            asset.transform.localPosition = new Vector3(3f, 4f, 5f);
            asset.transform.localScale = new Vector3(100f, 50f, 90f);
            var renderer = asset.AddComponent<SkinnedMeshRenderer>();
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.bindposes = new[] { Matrix4x4.identity };
            mesh.boneWeights = new[] {
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 0, weight0 = 1f } };
            var deltas = new[] { Vector3.forward * 0.1f, Vector3.forward * 0.1f, Vector3.forward * 0.1f };
            mesh.AddBlendShapeFrame("test_shape", 100f, deltas, new Vector3[3], new Vector3[3]);
            renderer.sharedMesh = mesh; renderer.bones = new[] { bone }; renderer.rootBone = bone;
            renderer.SetBlendShapeWeight(0, 50f);
            var matrix = character.transform.worldToLocalMatrix * bone.localToWorldMatrix;
            var result = FaceWeightSnapshot.BakeInHeadSpace(renderer, character.transform.worldToLocalMatrix);
            for (int i = 0; i < 3; i++)
            {
                var expected = matrix.MultiplyPoint3x4(mesh.vertices[i] + deltas[i] * 0.5f);
                if (Vector3.Distance(result[i], expected) > 0.0001f)
                    throw new Exception("Bake scale/space mismatch: " + result[i] + " expected " + expected);
            }
            string path = FaceWeightSnapshot.Export(character.transform, character.transform,
                new[] { new FaceWeightSnapshot.Entry { Renderer = renderer, SourceMesh = mesh,
                    Reference = renderer, SourceToReference = Matrix4x4.identity } });
            var xml = new XmlDocument(); xml.Load(Path.Combine(path, "snapshot.xml"));
            if (xml.SelectNodes("//renderer").Count != 2 || xml.SelectNodes("//shape[@weight='50']").Count != 2)
                throw new Exception("Snapshot manifest missing renderer or blendshape data");
            string[] rows = File.ReadAllLines(Path.Combine(path, "asset-0/vertices.csv"));
            if (rows.Length != 4) throw new Exception("Vertex rows missing");
            foreach (string row in rows) if (row.Split(',').Length != 23) throw new Exception("Invalid vertex CSV columns");
            if (renderer.sharedMesh != mesh || renderer.GetBlendShapeWeight(0) != 50f || renderer.bones[0] != bone)
                throw new Exception("Snapshot changed live renderer state");
            Debug.Log("FACE_WEIGHT_SNAPSHOT_PASS " + path);
        }
        finally { UnityEngine.Object.DestroyImmediate(character); UnityEngine.Object.DestroyImmediate(mesh); }
    }
}
