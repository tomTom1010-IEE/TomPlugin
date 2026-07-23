using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace AccessoryBoneBinder
{
    internal static class CoordinateLoadOptionBridge
    {
#if KK
        internal const string PluginGuid = "com.jim60105.kk.coordinateloadoption";
#else
        internal const string PluginGuid = "com.jim60105.kks.coordinateloadoption";
#endif

        private static readonly string[] AbmxTypeNames =
        {
#if KK
            "KK_CoordinateLoadOption.ABMX_CCFCSupport"
#else
            "CoordinateLoadOption.ABMX",
            "CoordinateLoadOption.OtherPlugin.CharaCustomFunctionController.ABMX"
#endif
        };

        internal static void TryPatch(Harmony harmony)
        {
            if (harmony == null || !Chainloader.PluginInfos.TryGetValue(PluginGuid, out var pluginInfo))
                return;

            try
            {
                var assembly = pluginInfo.Instance?.GetType().Assembly;
                if (assembly == null)
                {
                    AccessoryBoneBinderPlugin.Log?.LogWarning(
                        "Coordinate Load Option is installed, but its assembly could not be resolved.");
                    return;
                }

                var prefix = new HarmonyMethod(AccessTools.Method(
                    typeof(CoordinateLoadOptionBridge), nameof(BeforeGetDataFromController)));
                var patchedMethods = new HashSet<MethodBase>();

                foreach (string typeName in AbmxTypeNames)
                {
                    var type = assembly.GetType(typeName, false);
                    var target = type == null
                        ? null
                        : AccessTools.Method(type, "GetDataFromController", new[] { typeof(ChaControl) });
                    if (target == null || !patchedMethods.Add(target))
                        continue;

                    harmony.Patch(target, prefix: prefix);
                    AccessoryBoneBinderPlugin.Log?.LogInfo(
                        $"Enabled Coordinate Load Option compatibility for {type.FullName}.GetDataFromController.");
                }

                if (patchedMethods.Count == 0)
                {
                    AccessoryBoneBinderPlugin.Log?.LogWarning(
                        "Coordinate Load Option was found, but no supported ABMX extraction method was found.");
                }
            }
            catch (Exception ex)
            {
                AccessoryBoneBinderPlugin.Log?.LogWarning(
                    $"Failed to enable Coordinate Load Option compatibility: {ex}");
            }
        }

        private static void BeforeGetDataFromController(ChaControl __0)
        {
            if (__0 == null)
                return;

            try
            {
                var controller = __0.GetComponent<AccessoryBoneBinderController>();
                if (controller != null && controller.RebindImmediatelyForCompatibility())
                {
                    AccessoryBoneBinderPlugin.Log?.LogDebug(
                        $"Synchronously rebound accessory bones for Coordinate Load Option on {__0.name}.");
                }
            }
            catch (Exception ex)
            {
                AccessoryBoneBinderPlugin.Log?.LogWarning(
                    $"Coordinate Load Option pre-extraction rebind failed for {__0.name}: {ex}");
            }
        }
    }
}
