namespace AccessoryBoneBinder
{
    public static class AccessoryBoneBinderApi
    {
        public const int ApiVersion = 1;

        public static bool RebindNow(ChaControl chaControl)
        {
            if (chaControl == null)
                return false;
            var controller = chaControl.GetComponent<AccessoryBoneBinderController>();
            return controller != null && controller.RebindImmediatelyForCompatibility();
        }

        public static bool ScheduleRebind(ChaControl chaControl)
        {
            if (chaControl == null)
                return false;
            var controller = chaControl.GetComponent<AccessoryBoneBinderController>();
            if (controller == null)
                return false;
            controller.ScheduleRebind();
            return true;
        }
    }
}
