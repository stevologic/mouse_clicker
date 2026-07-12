using System.Diagnostics;
using System.Threading;

namespace ClickForge
{
    // Thread.Sleep only resolves to ~15 ms, which is useless for tight click
    // timing. This blends a coarse sleep with a short spin so small delays
    // (hold times, sub-frame intervals) are reasonably accurate.
    internal static class PrecisionSleep
    {
        public static void Sleep(int milliseconds)
        {
            if (milliseconds <= 0)
                return;

            if (milliseconds > 30)
            {
                // Sleep most of it coarsely, spin the remainder for accuracy.
                Thread.Sleep(milliseconds - 15);
                SpinFor(15);
            }
            else
            {
                SpinFor(milliseconds);
            }
        }

        private static void SpinFor(int milliseconds)
        {
            var sw = Stopwatch.StartNew();
            long target = milliseconds;
            while (sw.ElapsedMilliseconds < target)
            {
                Thread.SpinWait(80);
            }
        }
    }
}
