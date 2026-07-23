namespace MakerBlendShapeSync
{
    public static class MakerBlendShapeSyncApi
    {
        public const int ApiVersion = 1;

        public static bool ScheduleApply(ChaControl chaControl)
        {
            if (chaControl == null)
                return false;
            var controller = chaControl.GetComponent<BlendShapeSyncController>();
            if (controller == null)
                return false;
            controller.ScheduleApply();
            return true;
        }
    }
}
