using System;
using System.Collections;
using HarmonyLib;

namespace AccessoryBoneBinder
{
    internal static class AccessoryHooks
    {
        [HarmonyPostfix]
#if KK
        [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.ChangeAccessory), new[] { typeof(bool) })]
#else
        [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.ChangeAccessory), new[] { typeof(bool), typeof(bool) })]
#endif
        private static void ChangeAccessoryAll_Postfix(ChaControl __instance)
        {
            Schedule(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.ChangeAccessory), new[] { typeof(int), typeof(int), typeof(int), typeof(string), typeof(bool) })]
        private static void ChangeAccessory_Postfix(ChaControl __instance)
        {
            Schedule(__instance);
        }

#if KKS
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.ChangeAccessoryNoAsync), new[] { typeof(int), typeof(int), typeof(int), typeof(string), typeof(bool), typeof(bool) })]
        private static void ChangeAccessoryNoAsync_Postfix(ChaControl __instance)
        {
            Schedule(__instance);
        }
#endif

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.ChangeAccessoryAsync), new[] { typeof(bool) })]
        private static void ChangeAccessoryAsyncAll_Postfix(ChaControl __instance, ref IEnumerator __result)
        {
            __result = BindAfter(__instance, __result);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.ChangeAccessoryAsync), new[] { typeof(int), typeof(int), typeof(int), typeof(string), typeof(bool), typeof(bool) })]
        private static void ChangeAccessoryAsync_Postfix(ChaControl __instance, ref IEnumerator __result)
        {
            __result = BindAfter(__instance, __result);
        }

        private static IEnumerator BindAfter(ChaControl chaControl, IEnumerator original)
        {
            while (original != null && original.MoveNext())
                yield return original.Current;
            Schedule(chaControl);
        }

        private static void Schedule(ChaControl chaControl)
        {
            if (chaControl == null) return;
            var controller = chaControl.GetComponent<AccessoryBoneBinderController>();
            controller?.ScheduleRebind();
        }
    }
}
