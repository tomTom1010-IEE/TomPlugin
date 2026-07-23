using System;
using System.Collections;

namespace TomTom.KKMod.Shared
{
    public static class CoroutineUtility
    {
        public static IEnumerator AfterFrames(int frameCount, Action action)
        {
            for (int i = 0; i < frameCount; i++)
                yield return null;
            action?.Invoke();
        }
    }
}
