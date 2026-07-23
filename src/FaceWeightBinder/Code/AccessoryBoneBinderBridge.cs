using System;
using System.Reflection;
using TomTom.KKMod.Shared;

namespace FaceWeightBinder
{
    internal static class AccessoryBoneBinderBridge
    {
#if KK
        private const string PluginGuid = "tomtom.kk.accessorybonebinder";
#else
        private const string PluginGuid = "tomtom.kks.accessorybonebinder";
#endif
        private const string ApiTypeName = "AccessoryBoneBinder.AccessoryBoneBinderApi";
        private static MethodInfo _rebindMethod;
        private static bool _resolved;

        internal static bool RebindNowIfAvailable(ChaControl chaControl)
        {
            if (chaControl == null)
                return false;
            Resolve();
            if (_rebindMethod == null)
                return false;
            try
            {
                return (bool)_rebindMethod.Invoke(null, new object[] { chaControl });
            }
            catch (Exception ex)
            {
                FaceWeightBinderPlugin.Log?.LogWarning(
                    "AccessoryBoneBinder compatibility call failed: " + ex);
                return false;
            }
        }

        private static void Resolve()
        {
            if (_resolved)
                return;
            _resolved = true;
            _rebindMethod = PluginProbe.FindStaticMethod(
                PluginGuid, ApiTypeName, "RebindNow", new[] { typeof(ChaControl) });
        }
    }
}
