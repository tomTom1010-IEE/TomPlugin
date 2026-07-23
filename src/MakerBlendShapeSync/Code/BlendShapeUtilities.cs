using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace MakerBlendShapeSync
{
    internal static class BlendShapeUtilities
    {
        internal static IEnumerable<SkinnedMeshRenderer> EnumerateRenderers(Transform root)
        {
            return root == null ? new SkinnedMeshRenderer[0] : root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        }

        internal static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null) return "";
            var parts = new Stack<string>();
            var cur = target;
            while (cur != null && cur != root)
            {
                parts.Push(cur.name);
                cur = cur.parent;
            }
            return string.Join("/", parts.ToArray());
        }

        internal static string GetDisplayName(Transform root, SkinnedMeshRenderer renderer)
        {
            if (renderer == null) return "";
            string path = GetRelativePath(root, renderer.transform);
            string leaf = renderer.transform != null ? renderer.transform.name : renderer.name;
            string scope = GetDisplayScope(path, renderer);
            return string.IsNullOrEmpty(scope) ? leaf : scope + "/" + leaf;
        }

        internal static BlendShapeTargetScope GetTargetScope(ChaControl chaControl, SkinnedMeshRenderer renderer)
        {
            if (chaControl == null || renderer == null)
                return BlendShapeTargetScope.Unknown;

            if (IsUnderAny(renderer.transform, chaControl.objAccessory))
                return BlendShapeTargetScope.Accessory;
            if (IsUnderAny(renderer.transform, chaControl.objClothes))
                return BlendShapeTargetScope.Clothing;

            var fallbackRecord = new BlendShapeRecord
            {
                RendererPath = GetRelativePath(chaControl.transform, renderer.transform),
                RendererName = renderer.name,
                MeshName = renderer.sharedMesh == null ? "" : renderer.sharedMesh.name
            };
            BlendShapeTargetScope fallbackScope = InferLegacyScope(fallbackRecord);
            if (fallbackScope == BlendShapeTargetScope.Clothing ||
                fallbackScope == BlendShapeTargetScope.Accessory)
                return fallbackScope;

            return BlendShapeTargetScope.Character;
        }

        internal static int GetTargetCoordinate(ChaControl chaControl, SkinnedMeshRenderer renderer)
        {
            return GetTargetScope(chaControl, renderer) == BlendShapeTargetScope.Character
                ? -1
                : chaControl?.fileStatus?.coordinateType ?? 0;
        }

        internal static bool IsBodyRenderer(SkinnedMeshRenderer renderer)
        {
            if (renderer == null)
                return false;

            string rendererName = (renderer.name ?? "").ToLowerInvariant();
            string meshName = renderer.sharedMesh == null
                ? ""
                : (renderer.sharedMesh.name ?? "").ToLowerInvariant();
            string names = rendererName + "/" + meshName;
            return names.Contains("cf_o_body") || names.Contains("cm_o_body") ||
                   names.Contains("o_body_") || rendererName == "o_body" || meshName == "o_body" ||
                   names.Contains("body_a") || names.Contains("body_b");
        }

        internal static void PopulateRecordIdentity(ChaControl chaControl, SkinnedMeshRenderer renderer,
            BlendShapeRecord record)
        {
            if (chaControl == null || renderer == null || record == null)
                return;

            record.RendererPath = GetRelativePath(chaControl.transform, renderer.transform);
            record.RendererName = renderer.name;
            record.MeshName = renderer.sharedMesh == null ? "" : renderer.sharedMesh.name;

            if (TryGetTargetSlot(chaControl, renderer.transform, out var scope, out int slot, out var slotRoot))
            {
                record.TargetScope = scope;
                record.Slot = slot;
                record.SlotRelativePath = GetRelativePath(slotRoot, renderer.transform);
            }
            else
            {
                record.TargetScope = GetTargetScope(chaControl, renderer);
                record.Slot = -1;
                record.SlotRelativePath = "";
            }
        }

        internal static bool TryPopulateSlotIdentity(ChaControl chaControl, BlendShapeRecord record)
        {
            if (chaControl == null || record == null)
                return false;

            var renderer = FindRenderer(chaControl.transform, record);
            if (renderer != null &&
                TryGetTargetSlot(chaControl, renderer.transform, out var scope, out int slot, out var slotRoot))
            {
                record.TargetScope = scope;
                record.Slot = slot;
                record.SlotRelativePath = GetRelativePath(slotRoot, renderer.transform);
                record.RendererPath = GetRelativePath(chaControl.transform, renderer.transform);
                return true;
            }

            return TryInferSlotFromPath(chaControl, record);
        }

        internal static Transform GetSlotRoot(ChaControl chaControl, BlendShapeTargetScope scope, int slot)
        {
            if (chaControl == null || slot < 0)
                return null;

            GameObject[] roots = scope == BlendShapeTargetScope.Accessory
                ? chaControl.objAccessory
                : scope == BlendShapeTargetScope.Clothing
                    ? chaControl.objClothes
                    : null;
            return roots != null && slot < roots.Length && roots[slot] != null
                ? roots[slot].transform
                : null;
        }

        internal static void RemapRecordToSlot(ChaControl chaControl, BlendShapeRecord record, int slot)
        {
            if (chaControl == null || record == null)
                return;

            record.Slot = slot;
            var root = GetSlotRoot(chaControl, record.TargetScope, slot);
            if (root == null)
                return;

            string rootPath = GetRelativePath(chaControl.transform, root);
            record.RendererPath = string.IsNullOrEmpty(record.SlotRelativePath)
                ? rootPath
                : rootPath + "/" + record.SlotRelativePath;
        }

        internal static BlendShapeTargetScope InferLegacyScope(BlendShapeRecord record)
        {
            if (record == null)
                return BlendShapeTargetScope.Unknown;

            string path = (record.RendererPath ?? "").ToLowerInvariant();
            string objectNames = ((record.RendererName ?? "") + "/" +
                                  (record.MeshName ?? "")).ToLowerInvariant();
            string combined = path + "/" + objectNames;

            if (combined.Contains("accessory") || combined.Contains("/acs") ||
                combined.Contains("_acs") || combined.Contains("slot"))
                return BlendShapeTargetScope.Accessory;

            if (!string.IsNullOrEmpty(TryGetClothesScope(objectNames)))
                return BlendShapeTargetScope.Clothing;

            if (objectNames.Contains("cf_o_body") || objectNames.Contains("cm_o_body") ||
                objectNames.Contains("face") || objectNames.Contains("head") ||
                objectNames.Contains("hair") || objectNames.Contains("cf_o_nose") ||
                objectNames.Contains("cf_o_mouth") || objectNames.Contains("cf_o_ey") ||
                objectNames.Contains("cf_o_cha") || objectNames.Contains("cf_o_tooth") ||
                objectNames.Contains("cf_o_tang"))
                return BlendShapeTargetScope.Character;

            if (!string.IsNullOrEmpty(TryGetClothesScope(path)) &&
                !path.Contains("bodytop/p_cf_body_bone"))
                return BlendShapeTargetScope.Clothing;

            return BlendShapeTargetScope.Unknown;
        }

        internal static void EatInputInRect(Rect rect)
        {
            TryCallKkapiEatInputInRect(rect);

            var evt = Event.current;
            if (evt == null || !rect.Contains(evt.mousePosition))
                return;

            switch (evt.type)
            {
                case EventType.MouseDown:
                case EventType.MouseDrag:
                case EventType.MouseUp:
                case EventType.ScrollWheel:
                    Input.ResetInputAxes();
                    evt.Use();
                    break;
            }
        }

        internal static SkinnedMeshRenderer FindRenderer(Transform root, BlendShapeRecord record)
        {
            var chaControl = root == null ? null : root.GetComponent<ChaControl>();
            if (chaControl != null && record != null && record.Slot >= 0 &&
                (record.TargetScope == BlendShapeTargetScope.Clothing ||
                 record.TargetScope == BlendShapeTargetScope.Accessory))
            {
                var slotRoot = GetSlotRoot(chaControl, record.TargetScope, record.Slot);
                if (slotRoot != null)
                {
                    foreach (var renderer in EnumerateRenderers(slotRoot))
                    {
                        if ((!string.IsNullOrEmpty(record.SlotRelativePath) &&
                             GetRelativePath(slotRoot, renderer.transform) == record.SlotRelativePath &&
                             MeshMatches(renderer, record.MeshName)) ||
                            (string.IsNullOrEmpty(record.SlotRelativePath) &&
                             renderer.name == record.RendererName && MeshMatches(renderer, record.MeshName)))
                            return renderer;
                    }
                }
            }

            foreach (var renderer in EnumerateRenderers(root))
            {
                if (!string.IsNullOrEmpty(record.RendererPath) &&
                    GetRelativePath(root, renderer.transform) == record.RendererPath &&
                    MeshMatches(renderer, record.MeshName))
                    return renderer;
                if (renderer.name == record.RendererName && MeshMatches(renderer, record.MeshName))
                    return renderer;
            }
            return null;
        }

        internal static int FindBlendShapeIndex(SkinnedMeshRenderer renderer, string shapeName)
        {
            if (renderer?.sharedMesh == null) return -1;
            for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
            {
                if (renderer.sharedMesh.GetBlendShapeName(i) == shapeName)
                    return i;
            }
            return -1;
        }

        private static bool TryGetTargetSlot(ChaControl chaControl, Transform target,
            out BlendShapeTargetScope scope, out int slot, out Transform slotRoot)
        {
            scope = BlendShapeTargetScope.Unknown;
            slot = -1;
            slotRoot = null;

            if (TryFindContainingRoot(target, chaControl.objAccessory, out slot, out slotRoot))
            {
                scope = BlendShapeTargetScope.Accessory;
                return true;
            }

            if (TryFindContainingRoot(target, chaControl.objClothes, out slot, out slotRoot))
            {
                scope = BlendShapeTargetScope.Clothing;
                return true;
            }

            return false;
        }

        private static bool TryInferSlotFromPath(ChaControl chaControl, BlendShapeRecord record)
        {
            if (string.IsNullOrEmpty(record.RendererPath))
                return false;

            if (TryMatchRootPath(chaControl, record.RendererPath, chaControl.objAccessory,
                    BlendShapeTargetScope.Accessory, record))
                return true;
            return TryMatchRootPath(chaControl, record.RendererPath, chaControl.objClothes,
                BlendShapeTargetScope.Clothing, record);
        }

        private static bool TryMatchRootPath(ChaControl chaControl, string rendererPath, GameObject[] roots,
            BlendShapeTargetScope scope, BlendShapeRecord record)
        {
            if (roots == null)
                return false;

            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null)
                    continue;

                string rootPath = GetRelativePath(chaControl.transform, roots[i].transform);
                if (rendererPath != rootPath && !rendererPath.StartsWith(rootPath + "/"))
                    continue;

                record.TargetScope = scope;
                record.Slot = i;
                record.SlotRelativePath = rendererPath == rootPath
                    ? ""
                    : rendererPath.Substring(rootPath.Length + 1);
                return true;
            }

            return false;
        }

        private static bool TryFindContainingRoot(Transform target, GameObject[] roots,
            out int slot, out Transform slotRoot)
        {
            slot = -1;
            slotRoot = null;
            if (target == null || roots == null)
                return false;

            for (int i = 0; i < roots.Length; i++)
            {
                var root = roots[i];
                if (root == null || (target != root.transform && !target.IsChildOf(root.transform)))
                    continue;

                slot = i;
                slotRoot = root.transform;
                return true;
            }

            return false;
        }

        private static string GetDisplayScope(string path, SkinnedMeshRenderer renderer)
        {
            string lowerPath = (path ?? "").ToLowerInvariant();
            string lowerRenderer = renderer == null ? "" : (renderer.name ?? "").ToLowerInvariant();
            string lowerMesh = renderer?.sharedMesh == null ? "" : (renderer.sharedMesh.name ?? "").ToLowerInvariant();
            string combined = lowerPath + "/" + lowerRenderer + "/" + lowerMesh;

            string accSlot = TryGetAccessorySlotScope(path);
            if (!string.IsNullOrEmpty(accSlot))
                return accSlot;
            if (combined.Contains("accessory") || combined.Contains("/acs") || combined.Contains("_acs"))
                return "acc";

            if (combined.Contains("face") || combined.Contains("head") || combined.Contains("cf_o_face") ||
                combined.Contains("cf_o_nose") || combined.Contains("cf_o_mouth") || combined.Contains("cf_o_ey") ||
                combined.Contains("cf_o_cha") || combined.Contains("cf_o_tooth") || combined.Contains("cf_o_tang"))
                return "face";

            string clothes = TryGetClothesScope(combined);
            if (!string.IsNullOrEmpty(clothes))
                return clothes;

            if (combined.Contains("body") || combined.Contains("cf_o_body") || combined.Contains("p_cf_body"))
                return "body";

            if (!string.IsNullOrEmpty(path))
                return path.Split('/').FirstOrDefault();
            return "";
        }

        private static string TryGetClothesScope(string combined)
        {
            var markers = new[]
            {
                new { Key = "ct_top", Label = "Top" },
                new { Key = "top", Label = "Top" },
                new { Key = "ct_bot", Label = "Bottom" },
                new { Key = "bottom", Label = "Bottom" },
                new { Key = "bot", Label = "Bottom" },
                new { Key = "bra", Label = "Bra" },
                new { Key = "shorts", Label = "Shorts" },
                new { Key = "short", Label = "Shorts" },
                new { Key = "gloves", Label = "Gloves" },
                new { Key = "glove", Label = "Gloves" },
                new { Key = "panst", Label = "Pantyhose" },
                new { Key = "pantyhose", Label = "Pantyhose" },
                new { Key = "socks", Label = "Socks" },
                new { Key = "sock", Label = "Socks" },
                new { Key = "shoes", Label = "Shoes" },
                new { Key = "shoe", Label = "Shoes" }
            };

            foreach (var marker in markers)
            {
                if (combined.Contains(marker.Key))
                    return marker.Label;
            }
            return "";
        }

        private static string TryGetAccessorySlotScope(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string lower = path.ToLowerInvariant();
            int marker = lower.IndexOf("slot");
            if (marker < 0)
                marker = lower.IndexOf("accessory");
            if (marker < 0)
                marker = lower.IndexOf("acs");
            if (marker < 0)
                return "";

            for (int i = marker; i < lower.Length; i++)
            {
                if (!char.IsDigit(lower[i])) continue;
                int start = i;
                while (i < lower.Length && char.IsDigit(lower[i]))
                    i++;
                string digits = lower.Substring(start, i - start);
                if (int.TryParse(digits, out int slot))
                    return "accslot" + (slot + 1);
                return "accslot" + digits;
            }
            return "acc";
        }

        private static bool IsUnderAny(Transform target, GameObject[] roots)
        {
            if (target == null || roots == null)
                return false;

            foreach (var root in roots)
            {
                if (root != null && (target == root.transform || target.IsChildOf(root.transform)))
                    return true;
            }
            return false;
        }

        private static bool MeshMatches(SkinnedMeshRenderer renderer, string meshName)
        {
            return renderer != null && renderer.sharedMesh != null &&
                   (string.IsNullOrEmpty(meshName) || renderer.sharedMesh.name == meshName);
        }

        private static void TryCallKkapiEatInputInRect(Rect rect)
        {
            var type = typeof(KKAPI.KoikatuAPI).Assembly.GetType("KKAPI.Utilities.IMGUIUtils");
            var method = type?.GetMethod("EatInputInRect", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null) return;
            method.Invoke(null, new object[] { rect });
        }
    }
}
