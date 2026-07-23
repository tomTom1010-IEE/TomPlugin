using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using KKAPI;
using KKAPI.Chara;
using KKAPI.Maker;
using TomTom.KKMod.Shared;

namespace FaceWeightBinder
{
    [BepInPlugin(PluginGuid, Name, Version)]
    [BepInDependency(KoikatuAPI.GUID)]
    [BepInDependency(AccessoryBoneBinderBridge.PluginGuid,
        BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(AccessoryBoneBinderBridge.LegacyPluginGuid,
        BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(BlendShapeSyncBridge.PluginGuid,
        BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(BlendShapeSyncBridge.LegacyPluginGuid,
        BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.rclcircuit.bepinex.modboneimplantor",
        BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(CoordinateLoadOptionGuid,
        BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class FaceWeightBinderPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "tomtom.faceweightbinder";

#if KK
        public const string Name = "KK_FaceWeightBinder";
        internal const string CoordinateLoadOptionGuid =
            "com.jim60105.kk.coordinateloadoption";
#else
        public const string Name = "KKS_FaceWeightBinder";
        internal const string CoordinateLoadOptionGuid =
            "com.jim60105.kks.coordinateloadoption";
#endif

        [System.Obsolete("Use PluginGuid.")]
        public const string GUID = PluginGuid;

        public const string Version = "0.1.7.0";

        internal static ManualLogSource Log;
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            CharacterApi.RegisterExtraBehaviour<FaceWeightBinderController>(null);

            _harmony = new Harmony(PluginGuid);
            FaceWeightHooks.Install(_harmony);
            CoordinateLoadOptionPatcher.Install(
                _harmony,
                CoordinateLoadOptionGuid,
                new[]
                {
#if KK
                    "KK_CoordinateLoadOption.ABMX_CCFCSupport"
#else
                    "CoordinateLoadOption.ABMX",
                    "CoordinateLoadOption.OtherPlugin.CharaCustomFunctionController.ABMX"
#endif
                },
                RebindBeforeCoordinateExtraction,
                Log);

            AccessoriesApi.AccessoryKindChanged += AccessoriesApi_AccessoryKindChanged;
            AccessoriesApi.AccessoryTransferred += AccessoriesApi_AccessoryTransferred;
            AccessoriesApi.AccessoriesCopied += AccessoriesApi_AccessoriesCopied;
        }

        private void OnDestroy()
        {
            AccessoriesApi.AccessoryKindChanged -= AccessoriesApi_AccessoryKindChanged;
            AccessoriesApi.AccessoryTransferred -= AccessoriesApi_AccessoryTransferred;
            AccessoriesApi.AccessoriesCopied -= AccessoriesApi_AccessoriesCopied;
            _harmony?.UnpatchSelf();
        }

        private static void RebindBeforeCoordinateExtraction(ChaControl chaControl)
        {
            FaceWeightBinderApi.RebindNow(chaControl);
        }

        private static FaceWeightBinderController GetMakerController()
        {
            var chaControl = MakerAPI.GetCharacterControl();
            return chaControl == null
                ? null
                : chaControl.GetComponent<FaceWeightBinderController>();
        }

        private static void AccessoriesApi_AccessoryKindChanged(
            object sender, AccessorySlotEventArgs args)
        {
            GetMakerController()?.ScheduleRebind();
        }

        private static void AccessoriesApi_AccessoryTransferred(
            object sender, AccessoryTransferEventArgs args)
        {
            GetMakerController()?.ScheduleRebind();
        }

        private static void AccessoriesApi_AccessoriesCopied(
            object sender, AccessoryCopyEventArgs args)
        {
            GetMakerController()?.ScheduleRebind();
        }
    }
}
