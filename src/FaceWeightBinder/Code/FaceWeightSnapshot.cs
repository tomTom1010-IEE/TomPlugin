using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using UnityEngine;

namespace FaceWeightBinder
{
    internal static class FaceWeightSnapshot
    {
        internal sealed class Entry
        {
            public SkinnedMeshRenderer Renderer;
            public Mesh SourceMesh;
            public SkinnedMeshRenderer Reference;
            public Matrix4x4 SourceToReference;
        }

        internal static string Export(Transform character, Transform head, IList<Entry> entries,
            float[] faceShapeValues = null)
        {
            if (head == null) throw new InvalidOperationException("Character head is unavailable.");
            string directory = Path.Combine(BepInEx.Paths.BepInExRootPath, "FaceWeightSnapshots");
            directory = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) +
                "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(directory);
            var worldToHead = head.worldToLocalMatrix;
            var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
            // The completion manifest is moved into place only after every mesh succeeds.
            string pending = Path.Combine(directory, "snapshot.incomplete.xml");
            using (var xml = XmlWriter.Create(pending, settings))
            {
                xml.WriteStartElement("FaceWeightSnapshot");
                Attr(xml, "schema", "1"); Attr(xml, "plugin", FaceWeightBinderPlugin.Version);
                Attr(xml, "unity", Application.unityVersion); Attr(xml, "utc", DateTime.UtcNow.ToString("o"));
                Attr(xml, "frame", Time.frameCount.ToString(CultureInfo.InvariantCulture));
                Attr(xml, "character", character.name);
                Attr(xml, "space", "head-local; all renderers captured synchronously without yielding");
                Matrix(xml, "headLocalToWorld", head.localToWorldMatrix);
                Matrix(xml, "characterLocalToWorld", character.localToWorldMatrix);
                xml.WriteStartElement("faceShapeValues");
                if (faceShapeValues != null)
                    for (int i = 0; i < faceShapeValues.Length; i++)
                    {
                        xml.WriteStartElement("value"); Attr(xml, "index", i.ToString());
                        Attr(xml, "value", F(faceShapeValues[i])); xml.WriteEndElement();
                    }
                xml.WriteEndElement();
                var references = new Dictionary<int, string>();
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    string referenceId;
                    if (!references.TryGetValue(entry.Reference.GetInstanceID(), out referenceId))
                    {
                        referenceId = "reference-" + references.Count;
                        references.Add(entry.Reference.GetInstanceID(), referenceId);
                        WriteRenderer(xml, directory, referenceId, entry.Reference,
                            entry.Reference.sharedMesh, worldToHead, character);
                    }
                    string id = "asset-" + i;
                    xml.WriteStartElement("binding"); Attr(xml, "asset", id); Attr(xml, "reference", referenceId);
                    Matrix(xml, "sourceToReference", entry.SourceToReference);
                    xml.WriteEndElement();
                    WriteRenderer(xml, directory, id, entry.Renderer, entry.SourceMesh, worldToHead, character);
                }
                xml.WriteEndElement();
            }
            File.Move(pending, Path.Combine(directory, "snapshot.xml"));
            return directory;
        }

        private static void WriteRenderer(XmlWriter xml, string directory, string id,
            SkinnedMeshRenderer renderer, Mesh source, Matrix4x4 worldToHead, Transform character)
        {
            var mesh = renderer.sharedMesh;
            if (source == null || mesh == null) throw new InvalidOperationException("Missing mesh: " + id);
            var vertices = mesh.vertices;
            var sourceVertices = source.vertices;
            var normals = source.normals;
            var uv = source.uv;
            var weights = mesh.boneWeights;
            var bones = renderer.bones;
            var bindposes = mesh.bindposes;
            var originalBindposes = source.bindposes;
            if (sourceVertices.Length != vertices.Length || weights.Length != vertices.Length ||
                bindposes.Length != bones.Length || originalBindposes.Length != bones.Length)
                throw new InvalidOperationException("Inconsistent skinning arrays: " + id);
            string folder = Path.Combine(directory, id);
            Directory.CreateDirectory(folder);
            xml.WriteStartElement("renderer"); Attr(xml, "id", id); Attr(xml, "path", PathOf(character, renderer.transform));
            Attr(xml, "sourceMesh", source.name); Attr(xml, "activeMesh", mesh.name);
            Attr(xml, "vertices", vertices.Length.ToString()); Attr(xml, "bones", bones.Length.ToString());
            Attr(xml, "rootBone", PathOf(character, renderer.rootBone)); Attr(xml, "quality", renderer.quality.ToString());
#if KK || UNITY_5_6
            Attr(xml, "globalBlendWeights", QualitySettings.blendWeights.ToString());
#else
            Attr(xml, "globalBlendWeights", QualitySettings.skinWeights.ToString());
#endif
            Matrix(xml, "rendererLocalToWorld", renderer.transform.localToWorldMatrix);
            xml.WriteStartElement("blendShapes");
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                xml.WriteStartElement("shape"); Attr(xml, "index", i.ToString());
                Attr(xml, "name", mesh.GetBlendShapeName(i)); Attr(xml, "weight", F(renderer.GetBlendShapeWeight(i)));
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
            xml.WriteEndElement();

            var skin = new Matrix4x4[bones.Length];
            using (var writer = Csv(folder, "bones.csv", "index,name,path,source_bindpose_m00_to_m33,active_bindpose_m00_to_m33,bone_local_to_world_m00_to_m33"))
            {
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i] == null) throw new InvalidOperationException("Missing bone: " + id + " / " + i);
                    skin[i] = worldToHead * bones[i].localToWorldMatrix * bindposes[i];
                    writer.WriteLine(i + "," + Quote(bones[i].name) + "," + Quote(PathOf(character, bones[i])) + "," +
                        Quote(M(originalBindposes[i])) + "," + Quote(M(bindposes[i])) + "," + Quote(M(bones[i].localToWorldMatrix)));
                }
            }
            var baked = BakeInHeadSpace(renderer, worldToHead);
            if (baked.Length != vertices.Length) throw new InvalidOperationException("Bake vertex count changed: " + id);
            using (var writer = Csv(folder, "vertices.csv", "index,raw_x,raw_y,raw_z,normal_x,normal_y,normal_z,u,v,bone0,weight0,bone1,weight1,bone2,weight2,bone3,weight3,bone_only_head_x,bone_only_head_y,bone_only_head_z,baked_head_x,baked_head_y,baked_head_z"))
            {
                for (int i = 0; i < vertices.Length; i++)
                {
                    var w = weights[i];
                    var boneOnly = Influence(skin, w.boneIndex0, w.weight0, vertices[i]) +
                        Influence(skin, w.boneIndex1, w.weight1, vertices[i]) +
                        Influence(skin, w.boneIndex2, w.weight2, vertices[i]) +
                        Influence(skin, w.boneIndex3, w.weight3, vertices[i]);
                    writer.WriteLine(i + "," + V(sourceVertices[i]) + "," +
                        (normals.Length == vertices.Length ? V(normals[i]) : ",,") + "," +
                        (uv.Length == vertices.Length ? F(uv[i].x) + "," + F(uv[i].y) : ",") + "," +
                        w.boneIndex0 + "," + F(w.weight0) + "," + w.boneIndex1 + "," + F(w.weight1) + "," +
                        w.boneIndex2 + "," + F(w.weight2) + "," + w.boneIndex3 + "," + F(w.weight3) + "," +
                        V(boneOnly) + "," + V(baked[i]));
                }
            }
            using (var writer = Csv(folder, "triangles.csv", "submesh,triangle,v0,v1,v2"))
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var triangles = mesh.GetTriangles(sub);
                    for (int i = 0; i < triangles.Length; i += 3)
                        writer.WriteLine(sub + "," + (i / 3) + "," + triangles[i] + "," + triangles[i + 1] + "," + triangles[i + 2]);
                }
        }

        internal static Vector3[] BakeInHeadSpace(SkinnedMeshRenderer source, Matrix4x4 worldToHead)
        {
            // An identity renderer removes Unity-version-dependent BakeMesh scale handling.
            // It uses the same world-space bones and bindposes, so its baked local vertices
            // are world-space positions. It is disabled and never rendered.
            var temporary = new GameObject("FaceWeightSnapshotBake") { hideFlags = HideFlags.HideAndDontSave };
            var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var copy = temporary.AddComponent<SkinnedMeshRenderer>();
                copy.enabled = false;
                copy.sharedMesh = source.sharedMesh; copy.bones = source.bones;
                copy.rootBone = source.rootBone; copy.quality = source.quality;
                for (int i = 0; i < source.sharedMesh.blendShapeCount; i++)
                    copy.SetBlendShapeWeight(i, source.GetBlendShapeWeight(i));
                copy.BakeMesh(baked);
                var vertices = baked.vertices;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = worldToHead.MultiplyPoint3x4(vertices[i]);
                return vertices;
            }
            finally { UnityEngine.Object.DestroyImmediate(temporary); UnityEngine.Object.DestroyImmediate(baked); }
        }

        private static Vector3 Influence(Matrix4x4[] matrices, int index, float weight, Vector3 vertex)
        {
            if (weight == 0f) return Vector3.zero;
            if (index < 0 || index >= matrices.Length) throw new InvalidOperationException("Invalid weighted bone index.");
            return matrices[index].MultiplyPoint3x4(vertex) * weight;
        }
        private static StreamWriter Csv(string folder, string file, string header)
        {
            var writer = new StreamWriter(Path.Combine(folder, file), false, new UTF8Encoding(false));
            writer.WriteLine(header); return writer;
        }
        private static string F(float value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        private static string V(Vector3 value) { return F(value.x) + "," + F(value.y) + "," + F(value.z); }
        private static string M(Matrix4x4 value)
        {
            var cells = new string[16];
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) cells[r * 4 + c] = F(value[r, c]);
            return string.Join(" ", cells);
        }
        private static string Quote(string value) { return "\"" + value.Replace("\"", "\"\"") + "\""; }
        private static void Attr(XmlWriter xml, string key, string value) { xml.WriteAttributeString(key, value); }
        private static void Matrix(XmlWriter xml, string name, Matrix4x4 value) { xml.WriteElementString(name, M(value)); }
        private static string PathOf(Transform root, Transform node)
        {
            if (node == null) return "(null)";
            var parts = new List<string>();
            for (var current = node; current != null && current != root; current = current.parent) parts.Insert(0, current.name);
            return string.Join("/", parts.ToArray());
        }
    }
}
