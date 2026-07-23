using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using KKAPI;
using KKAPI.Chara;
using KKAPI.Maker;

namespace AccessoryBoneBinder
{
    [BepInPlugin(PluginGuid, Name, Version)]
    [BepInDependency(KoikatuAPI.GUID)]
    [BepInDependency("com.rclcircuit.bepinex.modboneimplantor")]
    [BepInDependency("KKABMX.Core", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(CoordinateLoadOptionBridge.PluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class AccessoryBoneBinderPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "tomtom.accessorybonebinder";

#if KK
        public const string Name = "KK_AccessoryBoneBinder";
#else
        public const string Name = "KKS_AccessoryBoneBinder";
#endif

        [System.Obsolete("Use PluginGuid.")]
        public const string GUID = PluginGuid;

        public const string Version = "0.2.2.0";

        internal static ManualLogSource Log;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            CharacterApi.RegisterExtraBehaviour<AccessoryBoneBinderController>(null);
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(AccessoryHooks));
            CoordinateLoadOptionBridge.TryPatch(_harmony);
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

        private static AccessoryBoneBinderController GetMakerController()
        {
            var chaControl = MakerAPI.GetCharacterControl();
            return chaControl == null ? null : chaControl.GetComponent<AccessoryBoneBinderController>();
        }

        private static void AccessoriesApi_AccessoryKindChanged(object sender, AccessorySlotEventArgs e)
        {
            GetMakerController()?.AccessoryKindChangedEvent(e.SlotIndex);
        }

        private static void AccessoriesApi_AccessoryTransferred(object sender, AccessoryTransferEventArgs e)
        {
            GetMakerController()?.AccessoryTransferredEvent(e.SourceSlotIndex, e.DestinationSlotIndex);
        }

        private static void AccessoriesApi_AccessoriesCopied(object sender, AccessoryCopyEventArgs e)
        {
            GetMakerController()?.AccessoriesCopiedEvent(
                (int)e.CopySource, (int)e.CopyDestination, e.CopiedSlotIndexes);
        }
    }
}
