namespace FaceWeightBinder
{
    public sealed class FaceWeightBindingReport
    {
        public int MarkerCount { get; internal set; }
        public int RendererCount { get; internal set; }
        public int BoundRendererCount { get; internal set; }
        public int UnchangedRendererCount { get; internal set; }
        public int FailedRendererCount { get; internal set; }

        public bool Changed => BoundRendererCount > 0;
    }
}
