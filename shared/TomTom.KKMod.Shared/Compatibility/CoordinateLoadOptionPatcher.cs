using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;

namespace TomTom.KKMod.Shared
{
    public static class CoordinateLoadOptionPatcher
    {
        private static Action<ChaControl> _beforeExtraction;

        public static int Install(Harmony harmony, string pluginGuid,
            IEnumerable<string> typeNames, Action<ChaControl> beforeExtraction,
            ManualLogSource log)
        {
            if (harmony == null || beforeExtraction == null ||
                !Chainloader.PluginInfos.TryGetValue(pluginGuid, out var pluginInfo))
                return 0;

            var assembly = pluginInfo.Instance?.GetType().Assembly;
            if (assembly == null)
                return 0;

            _beforeExtraction = beforeExtraction;
            var prefix = new HarmonyMethod(AccessTools.Method(
                typeof(CoordinateLoadOptionPatcher), nameof(BeforeGetDataFromController)));
            var patched = new HashSet<MethodBase>();
            foreach (string typeName in typeNames)
            {
                var type = assembly.GetType(typeName, false);
                var target = type == null
                    ? null
                    : AccessTools.Method(type, "GetDataFromController", new[] { typeof(ChaControl) });
                if (target == null || !patched.Add(target))
                    continue;
                harmony.Patch(target, prefix: prefix);
                log?.LogDebug("Patched Coordinate Load Option extraction: " + type.FullName);
            }
            return patched.Count;
        }

        private static void BeforeGetDataFromController(ChaControl __0)
        {
            if (__0 != null)
                _beforeExtraction?.Invoke(__0);
        }
    }
}
