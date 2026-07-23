using BepInEx.Bootstrap;
using System.Collections.Generic;

namespace AccessoryBoneBinder
{
    internal static class AbmxBridge
    {
        private const string AbmxGuid = "KKABMX.Core";

        public static bool RequestRefreshIfAvailable(ChaControl chaControl,
            IDictionary<string, AccessoryBindingTarget> reboundBones)
        {
            return IsAvailable(chaControl) &&
                   AbmxBridgeImpl.RequestRefreshIfAvailable(chaControl, reboundBones);
        }

        public static bool ClearSlotCurrentCoordinateIfAvailable(ChaControl chaControl, int slot, int coordinate)
        {
            return IsAvailable(chaControl) &&
                   AbmxBridgeImpl.ClearSlotCurrentCoordinateIfAvailable(chaControl, slot, coordinate);
        }

        public static bool CopySlotCurrentCoordinateIfAvailable(ChaControl chaControl, int sourceSlot,
            int destinationSlot, int coordinate)
        {
            return IsAvailable(chaControl) &&
                   AbmxBridgeImpl.CopySlotCurrentCoordinateIfAvailable(
                       chaControl, sourceSlot, destinationSlot, coordinate);
        }

        public static bool CopySlotsAcrossCoordinatesIfAvailable(ChaControl chaControl, int sourceCoordinate,
            int destinationCoordinate, IEnumerable<int> copiedSlots)
        {
            return IsAvailable(chaControl) &&
                   AbmxBridgeImpl.CopySlotsAcrossCoordinatesIfAvailable(
                       chaControl, sourceCoordinate, destinationCoordinate, copiedSlots);
        }

        private static bool IsAvailable(ChaControl chaControl)
        {
            return chaControl != null && Chainloader.PluginInfos.ContainsKey(AbmxGuid);
        }
    }
}
