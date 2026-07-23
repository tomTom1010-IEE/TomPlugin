using System;
using System.Reflection;
using TomTom.KKMod.Shared;

namespace FaceWeightBinder
{
    internal static class BlendShapeSyncBridge
    {
        internal const string PluginGuid = "tomtom.makerblendshapesync";

#if KK
        internal const string LegacyPluginGuid = "tomtom.kk.makerblendshapesync";
#else
        internal const string LegacyPluginGuid = "tomtom.kks.makerblendshapesync";
#endif

        private const string ApiTypeName = "MakerBlendShapeSync.MakerBlendShapeSyncApi";
        private static MethodInfo _scheduleMethod;
        private static bool _resolved;

        internal static void ScheduleApplyIfAvailable(ChaControl chaControl)
        {
            if (chaControl == null)
                return;
            Resolve();
            if (_scheduleMethod == null)
                return;
            try
            {
                _scheduleMethod.Invoke(null, new object[] { chaControl });
            }
            catch (Exception ex)
            {
                FaceWeightBinderPlugin.Log?.LogWarning(
                    "MakerBlendShapeSync compatibility call failed: " + ex);
            }
        }

        private static void Resolve()
        {
            if (_resolved)
                return;
            _resolved = true;
            _scheduleMethod = PluginProbe.FindStaticMethod(
                PluginGuid, ApiTypeName, "ScheduleApply", new[] { typeof(ChaControl) });
            if (_scheduleMethod == null)
                _scheduleMethod = PluginProbe.FindStaticMethod(
                    LegacyPluginGuid, ApiTypeName, "ScheduleApply", new[] { typeof(ChaControl) });
        }
    }
}
