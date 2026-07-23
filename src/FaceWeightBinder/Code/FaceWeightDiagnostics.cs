using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FaceWeightBinder
{
    internal static class FaceWeightDiagnostics
    {
        private static readonly string[] SampleBoneNames =
        {
            "cf_J_FaceRoot",
            "cf_J_FaceBase",
            "cf_J_FaceLow_tz",
            "cf_J_NoseBase",
            "cf_J_Eye_tz",
            "cf_J_Chin_s"
        };

        internal static string BuildReport(ChaControl chaControl,
            FaceWeightProcess marker, SkinnedMeshRenderer renderer,
            Mesh sourceMesh, Transform[] sourceBones, string[] boneNames,
            Transform[] targetBones, SkinnedMeshRenderer referenceRenderer,
            Transform sourceMeshSpaceReference, Matrix4x4 sourceToReference,
            Matrix4x4[] correctedBindposes)
        {
            var report = new StringBuilder(8192);
            var referenceMesh = referenceRenderer.sharedMesh;
            var referenceBones = referenceRenderer.bones ?? new Transform[0];
            var referenceBindposes = referenceMesh.bindposes;
            var sourceBindposes = sourceMesh.bindposes;
            var referenceByName = BuildBindposeMap(referenceBones,
                referenceBindposes);

            report.AppendLine("[FaceWeightDiagnostics] BEGIN");
            report.AppendLine("character=" + SafeName(chaControl) +
                              " marker=" + GetPath(null, marker.transform));
            report.AppendLine("sourceRenderer=" +
                              GetPath(marker.transform, renderer.transform) +
                              " sourceMesh=" + sourceMesh.name +
                              " vertices=" + sourceMesh.vertexCount +
                              " bones=" + sourceBones.Length +
                              " bindposes=" + sourceBindposes.Length);
            report.AppendLine("sourceMeshSpaceReference=" +
                              GetPath(marker.transform,
                                  sourceMeshSpaceReference));
            report.AppendLine("targetRenderer=" +
                              GetPath(chaControl.objHead == null
                                      ? null
                                      : chaControl.objHead.transform,
                                  referenceRenderer.transform) +
                              " targetMesh=" + referenceMesh.name +
                              " vertices=" + referenceMesh.vertexCount +
                              " bones=" + referenceBones.Length +
                              " bindposes=" + referenceBindposes.Length);

            AppendTransform(report, "markerWorld", marker.transform.localToWorldMatrix);
            AppendTransform(report, "sourceRendererWorld",
                renderer.transform.localToWorldMatrix);
            if (sourceMeshSpaceReference != null)
            {
                AppendTransform(report, "sourceReferenceWorld",
                    sourceMeshSpaceReference.localToWorldMatrix);
            }
            else
            {
                report.AppendLine("sourceReferenceWorld=(unavailable)");
            }
            AppendTransform(report, "targetRendererWorld",
                referenceRenderer.transform.localToWorldMatrix);
            if (chaControl.objHead != null)
                AppendTransform(report, "targetHeadWorld",
                    chaControl.objHead.transform.localToWorldMatrix);

            AppendTransform(report, "sourceMeshToReference", sourceToReference);
            report.AppendLine("sourceMeshToReference.matrix=" +
                              FormatMatrix(sourceToReference));

            Bounds transformedSourceBounds = TransformBounds(
                sourceMesh.bounds, sourceToReference);
            report.AppendLine("sourceMesh.bounds=" + FormatBounds(sourceMesh.bounds));
            report.AppendLine("sourceBounds.inReference=" +
                              FormatBounds(transformedSourceBounds));
            report.AppendLine("targetMesh.bounds=" +
                              FormatBounds(referenceMesh.bounds));
            report.AppendLine("sourceRenderer.localBounds=" +
                              FormatBounds(renderer.localBounds));
            report.AppendLine("targetRenderer.localBounds=" +
                              FormatBounds(referenceRenderer.localBounds));

            Vector3 rawBoundsScaleRatio = DivideComponents(
                referenceMesh.bounds.size, sourceMesh.bounds.size);
            Vector3 transformedBoundsScaleRatio = DivideComponents(
                referenceMesh.bounds.size, transformedSourceBounds.size);
            report.AppendLine("CHECK targetToRawSourceBoundsSizeRatio=" +
                              FormatVector(rawBoundsScaleRatio));
            report.AppendLine("CHECK targetToTransformedSourceBoundsSizeRatio=" +
                              FormatVector(transformedBoundsScaleRatio));

            int faceRootIndex = FindFaceRootIndex(boneNames);
            Transform sourceRoot = faceRootIndex >= 0 &&
                                   faceRootIndex < sourceBones.Length
                ? sourceBones[faceRootIndex]
                : null;
            Transform targetRoot = faceRootIndex >= 0 &&
                                   faceRootIndex < targetBones.Length
                ? targetBones[faceRootIndex]
                : null;
            Transform targetModelRoot = chaControl.objHead == null
                ? null
                : chaControl.objHead.transform;
            if (sourceRoot != null && targetRoot != null &&
                targetModelRoot != null)
            {
                Matrix4x4 sourceRootRelative =
                    marker.transform.worldToLocalMatrix *
                    sourceRoot.localToWorldMatrix;
                Matrix4x4 targetRootRelative =
                    targetModelRoot.worldToLocalMatrix *
                    targetRoot.localToWorldMatrix;
                AppendTransform(report, "sourceFaceRoot.inSourceModel",
                    sourceRootRelative);
                AppendTransform(report, "targetFaceRoot.inTargetModel",
                    targetRootRelative);
                AppendTransform(report, "sourceVsTargetFaceRoot.delta",
                    targetRootRelative.inverse * sourceRootRelative);
            }
            else
            {
                report.AppendLine("CHECK face-root comparison unavailable: index=" +
                                  faceRootIndex + " sourceRoot=" +
                                  (sourceRoot == null ? "null" : sourceRoot.name) +
                                  " targetRoot=" +
                                  (targetRoot == null ? "null" : targetRoot.name) +
                                  " targetModelRoot=" +
                                  (targetModelRoot == null
                                      ? "null"
                                      : targetModelRoot.name));
            }

            float errorSum = 0f;
            float maxError = 0f;
            string worstBone = string.Empty;
            int comparedCount = 0;
            var impliedByName = new Dictionary<string, Matrix4x4>(
                StringComparer.Ordinal);
            int bindposeCount = Math.Min(boneNames.Length,
                sourceBindposes.Length);
            for (int i = 0; i < bindposeCount; i++)
            {
                string boneName = boneNames[i];
                if (string.IsNullOrEmpty(boneName) ||
                    !referenceByName.TryGetValue(boneName,
                        out var referenceBindpose))
                    continue;

                Matrix4x4 implied = referenceBindpose.inverse *
                                    sourceBindposes[i];
                impliedByName[boneName] = implied;
                float error = MatrixMaxAbsDifference(implied,
                    sourceToReference);
                errorSum += error;
                comparedCount++;
                if (error > maxError)
                {
                    maxError = error;
                    worstBone = boneName;
                }
            }

            report.AppendLine("bindpose.impliedConversion compared=" +
                              comparedCount + " averageError=" +
                              FormatFloat(comparedCount == 0
                                  ? 0f
                                  : errorSum / comparedCount) +
                              " maxError=" + FormatFloat(maxError) +
                              " worstBone=" +
                              (string.IsNullOrEmpty(worstBone)
                                  ? "(none)"
                                  : worstBone));

            foreach (string sampleName in SampleBoneNames)
            {
                int index = Array.FindIndex(boneNames,
                    x => string.Equals(x, sampleName,
                        StringComparison.Ordinal));
                if (index < 0)
                    continue;

                if (impliedByName.TryGetValue(sampleName, out var implied))
                {
                    report.AppendLine("bone[" + sampleName +
                                      "].impliedMeshConversion=" +
                                      FormatTransform(implied) +
                                      " errorToChosen=" +
                                      FormatFloat(MatrixMaxAbsDifference(
                                          implied, sourceToReference)));
                }

                if (sourceRoot != null && targetRoot != null &&
                    index < sourceBones.Length && index < targetBones.Length &&
                    sourceBones[index] != null && targetBones[index] != null)
                {
                    Matrix4x4 sourceBoneRelative = sourceRoot.worldToLocalMatrix *
                                                   sourceBones[index]
                                                       .localToWorldMatrix;
                    Matrix4x4 targetBoneRelative = targetRoot.worldToLocalMatrix *
                                                   targetBones[index]
                                                       .localToWorldMatrix;
                    report.AppendLine("bone[" + sampleName +
                                      "].sourceRootRelative=" +
                                      FormatTransform(sourceBoneRelative));
                    report.AppendLine("bone[" + sampleName +
                                      "].targetRootRelative=" +
                                      FormatTransform(targetBoneRelative));
                }
            }

            float centerDistance = Vector3.Distance(
                transformedSourceBounds.center, referenceMesh.bounds.center);
            float referenceExtent = Mathf.Max(referenceMesh.bounds.size.x,
                referenceMesh.bounds.size.y, referenceMesh.bounds.size.z);
            Vector3 conversionPosition = GetPosition(sourceToReference);
            Vector3 conversionScale = GetScale(sourceToReference);
            float conversionRotation = Quaternion.Angle(Quaternion.identity,
                GetRotation(sourceToReference));

            report.AppendLine("CHECK conversionTranslation=" +
                              FormatVector(conversionPosition) +
                              " conversionRotationDegrees=" +
                              FormatFloat(conversionRotation) +
                              " conversionScale=" +
                              FormatVector(conversionScale));
            report.AppendLine("CHECK boundsCenterDistance=" +
                              FormatFloat(centerDistance) +
                              " relativeToTargetSize=" +
                              FormatFloat(referenceExtent <= 0f
                                  ? 0f
                                  : centerDistance / referenceExtent));
            if (conversionPosition.magnitude > 0.001f)
                report.AppendLine("CHECK WARNING chosen mesh conversion contains material translation.");
            if (conversionRotation > 0.1f)
                report.AppendLine("CHECK WARNING chosen mesh conversion contains material rotation.");
            if (maxError > 0.01f)
                report.AppendLine("CHECK NOTE raw source bindposes do not encode the same model-space conversion selected from renderer transforms.");
            if (referenceExtent > 0f && centerDistance / referenceExtent > 0.25f)
                report.AppendLine("CHECK WARNING transformed source bounds are far from the target face bounds.");
            if (correctedBindposes == null ||
                correctedBindposes.Length != boneNames.Length)
                report.AppendLine("CHECK WARNING corrected bindpose count is invalid.");
            report.Append("[FaceWeightDiagnostics] END");
            return report.ToString();
        }

        internal static string BuildReferenceSearchReport(ChaControl chaControl,
            FaceWeightProcess marker, SkinnedMeshRenderer sourceRenderer,
            string requestedRootBoneName, IEnumerable<string> requiredBoneNames)
        {
            var report = new StringBuilder(4096);
            var required = new HashSet<string>(
                requiredBoneNames.Where(x => !string.IsNullOrEmpty(x)),
                StringComparer.Ordinal);
            var characterRoot = chaControl == null ? null : chaControl.transform;
            var headRoot = chaControl == null || chaControl.objHead == null
                ? null
                : chaControl.objHead.transform;

            report.AppendLine("[FaceWeightReferenceSearch] BEGIN");
            report.AppendLine("character=" + SafeName(chaControl) +
                              " objHead=" + GetPath(characterRoot, headRoot) +
                              " requestedRootBone=" + requestedRootBoneName +
                              " requiredBones=" + required.Count);
            report.AppendLine("sourceRenderer=" +
                              GetPath(marker == null ? null : marker.transform,
                                  sourceRenderer == null
                                      ? null
                                      : sourceRenderer.transform));

            if (chaControl == null)
            {
                report.Append("[FaceWeightReferenceSearch] END");
                return report.ToString();
            }

            var renderers = chaControl
                .GetComponentsInChildren<SkinnedMeshRenderer>(true);
            report.AppendLine("characterSkinnedRenderers=" + renderers.Length);
            foreach (var candidate in renderers)
            {
                if (candidate == null)
                    continue;
                var candidateBones = candidate.bones ?? new Transform[0];
                var candidateNames = new HashSet<string>(
                    candidateBones.Where(x => x != null).Select(x => x.name),
                    StringComparer.Ordinal);
                int overlap = required.Count(candidateNames.Contains);
                bool primaryName = string.Equals(candidate.name, "cf_O_face",
                                       StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(candidate.name, "cm_O_face",
                                       StringComparison.OrdinalIgnoreCase);
                bool rootMatches = candidate.rootBone != null &&
                                   candidate.rootBone.name == requestedRootBoneName;
                if (!primaryName && !rootMatches && overlap == 0)
                    continue;

                var missing = required.Where(x => !candidateNames.Contains(x))
                    .Take(8).ToArray();
                Mesh mesh = candidate.sharedMesh;
                report.AppendLine("candidate path=" +
                                  GetPath(characterRoot, candidate.transform) +
                                  " name=" + candidate.name +
                                  " inObjHead=" +
                                  (headRoot != null &&
                                   (candidate.transform == headRoot ||
                                    candidate.transform.IsChildOf(headRoot))) +
                                  " rootBone=" +
                                  (candidate.rootBone == null
                                      ? "(null)"
                                      : candidate.rootBone.name) +
                                  " bones=" + candidateBones.Length +
                                  " bindposes=" +
                                  (mesh == null ? 0 : mesh.bindposes.Length) +
                                  " overlap=" + overlap + "/" + required.Count +
                                  " missing=" +
                                  (missing.Length == 0
                                      ? "(none)"
                                      : string.Join(",", missing)));
            }

            var faceTransforms = chaControl
                .GetComponentsInChildren<Transform>(true)
                .Where(x => x != null &&
                            (string.Equals(x.name, "cf_O_face",
                                 StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(x.name, "cm_O_face",
                                 StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            report.AppendLine("namedFaceTransforms=" + faceTransforms.Length);
            foreach (var transform in faceTransforms)
            {
                var skinned = transform.GetComponent<SkinnedMeshRenderer>();
                report.AppendLine("faceTransform path=" +
                                  GetPath(characterRoot, transform) +
                                  " hasSkinnedRenderer=" + (skinned != null));
            }
            report.Append("[FaceWeightReferenceSearch] END");
            return report.ToString();
        }

        private static Dictionary<string, Matrix4x4> BuildBindposeMap(
            Transform[] bones, Matrix4x4[] bindposes)
        {
            var result = new Dictionary<string, Matrix4x4>(
                StringComparer.Ordinal);
            int count = Math.Min(bones.Length, bindposes.Length);
            for (int i = 0; i < count; i++)
            {
                if (bones[i] != null && !result.ContainsKey(bones[i].name))
                    result.Add(bones[i].name, bindposes[i]);
            }
            return result;
        }

        private static int FindFaceRootIndex(string[] boneNames)
        {
            return Array.FindIndex(boneNames, x =>
                string.Equals(x, "cf_J_FaceRoot",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x, "cm_J_FaceRoot",
                    StringComparison.OrdinalIgnoreCase));
        }

        private static Vector3 DivideComponents(Vector3 numerator,
            Vector3 denominator)
        {
            return new Vector3(
                Mathf.Abs(denominator.x) < 1e-12f
                    ? 0f
                    : numerator.x / denominator.x,
                Mathf.Abs(denominator.y) < 1e-12f
                    ? 0f
                    : numerator.y / denominator.y,
                Mathf.Abs(denominator.z) < 1e-12f
                    ? 0f
                    : numerator.z / denominator.z);
        }

        private static void AppendTransform(StringBuilder report,
            string label, Matrix4x4 matrix)
        {
            report.AppendLine(label + "=" + FormatTransform(matrix));
        }

        private static string FormatTransform(Matrix4x4 matrix)
        {
            return "position=" + FormatVector(GetPosition(matrix)) +
                   " rotation=" + FormatVector(GetRotation(matrix).eulerAngles) +
                   " scale=" + FormatVector(GetScale(matrix)) +
                   " determinant=" + FormatFloat(matrix.determinant);
        }

        private static Vector3 GetPosition(Matrix4x4 matrix)
        {
            return new Vector3(matrix.m03, matrix.m13, matrix.m23);
        }

        private static Vector3 GetScale(Matrix4x4 matrix)
        {
            return new Vector3(
                new Vector3(matrix.m00, matrix.m10, matrix.m20).magnitude,
                new Vector3(matrix.m01, matrix.m11, matrix.m21).magnitude,
                new Vector3(matrix.m02, matrix.m12, matrix.m22).magnitude);
        }

        private static Quaternion GetRotation(Matrix4x4 matrix)
        {
            Vector3 forward = new Vector3(matrix.m02, matrix.m12, matrix.m22);
            Vector3 up = new Vector3(matrix.m01, matrix.m11, matrix.m21);
            if (forward.sqrMagnitude < 1e-12f || up.sqrMagnitude < 1e-12f)
                return Quaternion.identity;
            return Quaternion.LookRotation(forward, up);
        }

        private static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
        {
            Vector3 center = matrix.MultiplyPoint3x4(bounds.center);
            Vector3 extents = bounds.extents;
            Vector3 axisX = matrix.MultiplyVector(new Vector3(extents.x, 0f, 0f));
            Vector3 axisY = matrix.MultiplyVector(new Vector3(0f, extents.y, 0f));
            Vector3 axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, extents.z));
            var transformedExtents = new Vector3(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
            return new Bounds(center, transformedExtents * 2f);
        }

        private static float MatrixMaxAbsDifference(Matrix4x4 left,
            Matrix4x4 right)
        {
            float maximum = 0f;
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                    maximum = Mathf.Max(maximum,
                        Mathf.Abs(left[row, column] - right[row, column]));
            }
            return maximum;
        }

        private static string FormatMatrix(Matrix4x4 matrix)
        {
            var rows = new string[4];
            for (int row = 0; row < 4; row++)
            {
                rows[row] = "[" + FormatFloat(matrix[row, 0]) + "," +
                            FormatFloat(matrix[row, 1]) + "," +
                            FormatFloat(matrix[row, 2]) + "," +
                            FormatFloat(matrix[row, 3]) + "]";
            }
            return string.Join(";", rows);
        }

        private static string FormatBounds(Bounds bounds)
        {
            return "center=" + FormatVector(bounds.center) +
                   " size=" + FormatVector(bounds.size);
        }

        private static string FormatVector(Vector3 value)
        {
            return "(" + FormatFloat(value.x) + "," +
                   FormatFloat(value.y) + "," +
                   FormatFloat(value.z) + ")";
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("G9", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string GetPath(Transform root, Transform target)
        {
            if (target == null)
                return "(null)";
            var parts = new Stack<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                parts.Push(current.name);
                current = current.parent;
            }
            if (root != null && current != root)
                return "(outside root)/" + target.name;
            return parts.Count == 0 ? target.name : string.Join("/", parts.ToArray());
        }

        private static string SafeName(UnityEngine.Object value)
        {
            return value == null ? "(null)" : value.name;
        }
    }
}
