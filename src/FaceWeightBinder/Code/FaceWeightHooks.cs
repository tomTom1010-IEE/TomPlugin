using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace FaceWeightBinder
{
    internal static class FaceWeightHooks
    {
        internal static void Install(Harmony harmony)
        {
            var synchronousPostfix = new HarmonyMethod(
                AccessTools.Method(typeof(FaceWeightHooks), nameof(SynchronousPostfix)));
            var asynchronousPostfix = new HarmonyMethod(
                AccessTools.Method(typeof(FaceWeightHooks), nameof(AsynchronousPostfix)));
            var patched = new HashSet<MethodBase>();

            foreach (var method in AccessTools.GetDeclaredMethods(typeof(ChaControl))
                         .Where(x => x.IsPublic && IsRelevantChangeMethod(x.Name)))
            {
                if (!patched.Add(method))
                    continue;
                if (method.ReturnType == typeof(void))
                    harmony.Patch(method, postfix: synchronousPostfix);
                else if (typeof(IEnumerator).IsAssignableFrom(method.ReturnType))
                    harmony.Patch(method, postfix: asynchronousPostfix);
            }
        }

        private static bool IsRelevantChangeMethod(string methodName)
        {
            return methodName.StartsWith("ChangeAccessory", StringComparison.Ordinal) ||
                   methodName.StartsWith("ChangeClothes", StringComparison.Ordinal) ||
                   methodName.StartsWith("ChangeHead", StringComparison.Ordinal);
        }

        private static void SynchronousPostfix(ChaControl __instance)
        {
            Schedule(__instance);
        }

        private static void AsynchronousPostfix(ChaControl __instance, ref IEnumerator __result)
        {
            __result = ScheduleAfter(__instance, __result);
        }

        private static IEnumerator ScheduleAfter(ChaControl chaControl, IEnumerator original)
        {
            while (original != null && original.MoveNext())
                yield return original.Current;
            Schedule(chaControl);
        }

        private static void Schedule(ChaControl chaControl)
        {
            if (chaControl == null)
                return;
            chaControl.GetComponent<FaceWeightBinderController>()?.ScheduleRebind();
        }
    }
}
