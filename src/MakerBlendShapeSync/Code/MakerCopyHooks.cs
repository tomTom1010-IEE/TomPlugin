using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ChaCustom;
using HarmonyLib;

namespace MakerBlendShapeSync
{
    internal static class MakerCopyHooks
    {
        private static readonly FieldInfo CoordinateDropdowns =
            AccessTools.Field(typeof(CvsClothesCopy), "ddCoordeType");
        private static readonly FieldInfo ClothingToggles =
            AccessTools.Field(typeof(CvsClothesCopy), "tglKind");

        internal static void Init(Harmony harmony)
        {
            var target = AccessTools.Method(typeof(CvsClothesCopy), "CopyClothes");
            var postfix = AccessTools.Method(typeof(MakerCopyHooks), nameof(CopyClothesPostfix));
            if (target == null || postfix == null)
            {
                MakerBlendShapeSyncPlugin.Log?.LogWarning(
                    "CvsClothesCopy.CopyClothes was not found; clothing BlendShape copy is disabled.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
        }

        private static void CopyClothesPostfix(CvsClothesCopy __instance)
        {
            try
            {
                var dropdowns = CoordinateDropdowns?.GetValue(__instance) as IList;
                var toggles = ClothingToggles?.GetValue(__instance) as IList;
                if (dropdowns == null || dropdowns.Count < 2 || toggles == null)
                    return;

                int destination = ReadIntProperty(dropdowns[0], "value");
                int source = ReadIntProperty(dropdowns[1], "value");
                var copiedSlots = new List<int>();
                for (int i = 0; i < toggles.Count; i++)
                {
                    if (ReadBoolProperty(toggles[i], "isOn"))
                        copiedSlots.Add(i);
                }

                MakerBlendShapeSyncPlugin.GetMakerController()?.ClothingCopiedEvent(
                    source, destination, copiedSlots);
            }
            catch (Exception ex)
            {
                MakerBlendShapeSyncPlugin.Log?.LogError(
                    $"Failed to copy clothing BlendShape records: {ex}");
            }
        }

        private static int ReadIntProperty(object instance, string propertyName)
        {
            if (instance == null)
                return 0;
            var property = instance.GetType().GetProperty(propertyName);
            return property == null ? 0 : (int)property.GetValue(instance, null);
        }

        private static bool ReadBoolProperty(object instance, string propertyName)
        {
            if (instance == null)
                return false;
            var property = instance.GetType().GetProperty(propertyName);
            return property != null && (bool)property.GetValue(instance, null);
        }
    }
}
