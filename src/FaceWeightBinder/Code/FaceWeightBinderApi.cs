using System;
using UnityEngine;

namespace FaceWeightBinder
{
    public static class FaceWeightBinderApi
    {
        public const int ApiVersion = 1;

        public static event Action<ChaControl, FaceWeightBindingReport> BindingsUpdated;

        public static bool RebindNow(ChaControl chaControl)
        {
            if (chaControl == null)
                return false;
            var controller = chaControl.GetComponent<FaceWeightBinderController>();
            return controller != null && controller.RebindImmediatelyForCompatibility();
        }

        public static bool ScheduleRebind(ChaControl chaControl)
        {
            if (chaControl == null)
                return false;
            var controller = chaControl.GetComponent<FaceWeightBinderController>();
            if (controller == null)
                return false;
            controller.ScheduleRebind();
            return true;
        }

        internal static void RaiseBindingsUpdated(ChaControl chaControl,
            FaceWeightBindingReport report)
        {
            var handlers = BindingsUpdated;
            if (handlers == null)
                return;
            foreach (Action<ChaControl, FaceWeightBindingReport> handler
                     in handlers.GetInvocationList())
            {
                try
                {
                    handler(chaControl, report);
                }
                catch (Exception ex)
                {
                    FaceWeightBinderPlugin.Log?.LogWarning(
                        "A FaceWeightBinder API subscriber failed: " + ex);
                }
            }
        }
    }
}
