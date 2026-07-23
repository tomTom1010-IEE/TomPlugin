using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using KKAPI.Chara;
using KKAPI.Maker;
using KKAPI.Maker.UI.Sidebar;
using KKAPI.Studio;
using UniRx;
using UnityEngine;

namespace MakerBlendShapeSync
{
    [BepInPlugin(PluginGuid, Name, Version)]
    [BepInDependency("marco.kkapi", "1.28")]
    [BepInDependency("com.bepis.bepinex.extendedsave")]
    [BepInDependency("com.joan6694.kkplugins.kkpe", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(UncensorSelectorGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class MakerBlendShapeSyncPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "tomtom.makerblendshapesync";

        [System.Obsolete("Use PluginGuid.")]
        public const string GUID = PluginGuid;

        public const string Name = "MakerBlendShapeSync";
        public const string Version = "0.5.4.0";

        internal const string DataId = "MakerBlendShapeSync";
        private const string UncensorSelectorGuid = "com.deathweasel.bepinex.uncensorselector";
        private const string UncensorSelectorControllerType =
            "KK_Plugins.UncensorSelector+UncensorSelectorController";
        internal static ManualLogSource Log;
        internal static MakerBlendShapeWindow MakerWindow;
        internal static SidebarToggle MakerSidebarToggle;
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
        }

        private void Start()
        {
            CharacterApi.RegisterExtraBehaviour<BlendShapeSyncController>(DataId);
            _harmony = new Harmony(PluginGuid);
            MakerCopyHooks.Init(_harmony);
            InitUncensorSelectorBridge();
            InitMakerDataHooks();
            InitMakerUi();
            InitStudioBridge();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        private void InitStudioBridge()
        {
            if (!StudioAPI.InsideStudio)
                return;

            StudioPoseEditorBridge.Init(_harmony);
        }

        private void InitUncensorSelectorBridge()
        {
            var controllerType = AccessTools.TypeByName(UncensorSelectorControllerType);
            var target = controllerType == null
                ? null
                : AccessTools.Method(controllerType, "UpdateMeshRenderer",
                    new[] { typeof(SkinnedMeshRenderer), typeof(SkinnedMeshRenderer), typeof(bool) });
            if (target == null)
            {
                Log?.LogDebug("Uncensor Selector mesh replacement hook is not available.");
                return;
            }

            _harmony.Patch(target,
                prefix: new HarmonyMethod(typeof(MakerBlendShapeSyncPlugin),
                    nameof(UncensorSelectorUpdateMeshRendererPrefix)),
                postfix: new HarmonyMethod(typeof(MakerBlendShapeSyncPlugin),
                    nameof(UncensorSelectorUpdateMeshRendererPostfix)));
            Log?.LogDebug("Installed Uncensor Selector blendshape reset hook.");
        }

        private static void UncensorSelectorUpdateMeshRendererPrefix(
            SkinnedMeshRenderer __1,
            out BlendShapeSyncController.RendererMeshReplacementState __state)
        {
            __state = null;
            if (__1 == null)
                return;

            var chaControl = __1.GetComponentInParent<ChaControl>();
            var controller = chaControl == null
                ? null
                : chaControl.GetComponent<BlendShapeSyncController>();
            if (controller != null)
                __state = controller.PrepareRendererMeshReplacement(__1);
        }

        private static void UncensorSelectorUpdateMeshRendererPostfix(
            SkinnedMeshRenderer __0,
            SkinnedMeshRenderer __1,
            BlendShapeSyncController.RendererMeshReplacementState __state)
        {
            __state?.Controller.CompleteRendererMeshReplacement(__state, __0, __1);
        }

        private static void InitMakerDataHooks()
        {
            AccessoriesApi.AccessoryKindChanged += (sender, args) =>
                GetMakerController()?.AccessoryKindChangedEvent(args.SlotIndex);
            AccessoriesApi.AccessoryTransferred += (sender, args) =>
                GetMakerController()?.AccessoryTransferredEvent(
                    args.SourceSlotIndex, args.DestinationSlotIndex);
            AccessoriesApi.AccessoriesCopied += (sender, args) =>
                GetMakerController()?.AccessoriesCopiedEvent(
                    (int)args.CopySource, (int)args.CopyDestination, args.CopiedSlotIndexes);
        }

        internal static BlendShapeSyncController GetMakerController()
        {
            var chaControl = MakerAPI.GetCharacterControl();
            return chaControl == null ? null : chaControl.GetComponent<BlendShapeSyncController>();
        }

        private void InitMakerUi()
        {
            MakerAPI.RegisterCustomSubCategories += (sender, args) =>
            {
                MakerWindow = gameObject.AddComponent<MakerBlendShapeWindow>();
                MakerSidebarToggle = args.AddSidebarControl(new SidebarToggle("BlendShapes", false, this));
                MakerSidebarToggle.ValueChanged.Subscribe(value =>
                {
                    if (MakerWindow != null)
                        MakerWindow.enabled = value;
                });
            };

            MakerAPI.MakerExiting += (sender, args) =>
            {
                if (MakerWindow != null)
                    Destroy(MakerWindow);
                MakerWindow = null;
                MakerSidebarToggle = null;
            };
        }
    }
}


