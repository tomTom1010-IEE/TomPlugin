using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using UnityEngine;
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

        public const string Version = "0.2.0.0";

        internal static ManualLogSource Log;
        private Harmony _harmony;
        private ConfigEntry<KeyboardShortcut> _snapshotShortcut;
        private static ConfigEntry<bool> _diagnosticLogging;
        private static ConfigEntry<bool> _snapshotEnabled;
        internal static bool DiagnosticLoggingEnabled => _diagnosticLogging != null && _diagnosticLogging.Value;
        internal static bool SnapshotEnabled => _snapshotEnabled != null && _snapshotEnabled.Value;

        private void Awake()
        {
            Log = Logger;
            _diagnosticLogging = Config.Bind("Diagnostics", "Verbose logging", false,
                "Enable detailed binding scans and coordinate reports. Normal binding failures remain logged when disabled.");
            _snapshotEnabled = Config.Bind("Diagnostics", "Enable snapshots", false,
                "Allow diagnostic mesh snapshots via shortcut or API. Disabled by default; exporting may briefly pause the game.");
            _snapshotShortcut = Config.Bind("Diagnostics", "Snapshot shortcut",
                new KeyboardShortcut(KeyCode.F8, KeyCode.LeftControl, KeyCode.LeftShift),
                "Export the Maker character's face binding meshes at end of frame. Output: BepInEx/FaceWeightSnapshots.");
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
                DiagnosticLoggingEnabled ? Log : null);

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

        private void Update()
        {
            if (!SnapshotEnabled || !_snapshotShortcut.Value.IsDown())
                return;
            var controller = GetMakerController();
            if (controller == null)
                Log.LogWarning("[FaceWeightSnapshot] Open Maker and equip a FaceWeightProcess asset first.");
            else
                controller.RequestDiagnosticSnapshot();
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
