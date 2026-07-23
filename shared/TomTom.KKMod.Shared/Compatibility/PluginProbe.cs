using System;
using System.Reflection;
using BepInEx.Bootstrap;

namespace TomTom.KKMod.Shared
{
    public static class PluginProbe
    {
        public static bool IsLoaded(string guid)
        {
            return !string.IsNullOrEmpty(guid) && Chainloader.PluginInfos.ContainsKey(guid);
        }

        public static Type FindType(string guid, string fullTypeName)
        {
            if (string.IsNullOrEmpty(guid) || string.IsNullOrEmpty(fullTypeName) ||
                !Chainloader.PluginInfos.TryGetValue(guid, out var pluginInfo))
                return null;
            return pluginInfo.Instance?.GetType().Assembly.GetType(fullTypeName, false);
        }

        public static MethodInfo FindStaticMethod(string guid, string fullTypeName,
            string methodName, Type[] parameterTypes)
        {
            var type = FindType(guid, fullTypeName);
            return type?.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null, parameterTypes ?? Type.EmptyTypes, null);
        }
    }
}
