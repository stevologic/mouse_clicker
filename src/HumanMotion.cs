using System;

namespace ClickForge
{
    // Generates cursor movement paths. Teleport is a single SetCursorPos;
    // Linear and Humanized step the cursor along a path over time so the
    // motion looks like a hand moved the mouse rather than a script.
    internal static class HumanMotion
    {
        // Move from the current cursor position to (targetX, targetY).
        //   mode         Teleport / Linear / Humanized
        //   durationMs   total glide time for Linear/Humanized
        //   shouldStop   polled between steps so Stop is responsive mid-glide
        public static void MoveTo(int targetX, int targetY, MovementMode mode,
                                  int durationMs, Random rng, Func<bool> shouldStop)
        {
            if (mode == MovementMode.Teleport || durationMs <= 0)
            {
                InputSimulator.MoveTo(targetX, targetY);
                return;
            }

            NativeMethods.POINT start = InputSimulator.GetCursor();
            double sx = start.X;
            double sy = start.Y;
            double dx = targetX - sx;
            double dy = targetY - sy;
            double distance = Math.Sqrt(dx * dx + dy * dy);

            if (distance < 1.5)
            {
                InputSimulator.MoveTo(targetX, targetY);
                return;
            }

            // ~120 steps/sec, bounded so short hops still animate and long
            // ones don't produce thousands of SetCursorPos calls.
            int steps = (int)Math.Round(durationMs / 8.0);
            if (steps < 6) steps = 6;
            if (steps > 400) steps = 400;

            double c1x, c1y, c2x, c2y;
            BuildControlPoints(sx, sy, targetX, targetY, distance, mode, rng,
                out c1x, out c1y, out c2x, out c2y);

            double perStepDelay = (double)durationMs / steps;
            double jitterAmp = mode == MovementMode.Humanized ? Math.Min(4.0, distance * 0.01) : 0.0;

            for (int i = 1; i <= steps; i++)
            {
                if (shouldStop != null && shouldStop())
                    return;

                double t = (double)i / steps;
                double e = EaseInOut(t);

                double x = Bezier(sx, c1x, c2x, targetX, e);
                double y = Bezier(sy, c1y, c2y, targetY, e);

                if (jitterAmp > 0 && i < steps)
                {
                    x += (rng.NextDouble() - 0.5) * jitterAmp;
                    y += (rng.NextDouble() - 0.5) * jitterAmp;
                }

                InputSimulator.MoveTo((int)Math.Round(x), (int)Math.Round(y));
                PrecisionSleep.Sleep((int)Math.Round(perStepDelay));
            }

            // Land exactly on target regardless of rounding drift.
            InputSimulator.MoveTo(targetX, targetY);
        }

        private static void BuildControlPoints(double sx, double sy, double ex, double ey,
            double distance, MovementMode mode, Random rng,
            out double c1x, out double c1y, out double c2x, out double c2y)
        {
            if (mode == MovementMode.Linear)
            {
                // Control points on the straight line -> straight glide.
                c1x = sx + (ex - sx) / 3.0;
                c1y = sy + (ey - sy) / 3.0;
                c2x = sx + 2.0 * (ex - sx) / 3.0;
                c2y = sy + 2.0 * (ey - sy) / 3.0;
                return;
            }

            // Humanized: bow the path sideways by a random, distance-scaled arc.
            double nx = -(ey - sy) / distance; // unit normal
            double ny = (ex - sx) / distance;
            double arc = distance * (0.08 + rng.NextDouble() * 0.14);
            if (rng.Next(2) == 0) arc = -arc;

            double m1x = sx + (ex - sx) * 0.33;
            double m1y = sy + (ey - sy) * 0.33;
            double m2x = sx + (ex - sx) * 0.66;
            double m2y = sy + (ey - sy) * 0.66;

            double bow1 = arc * (0.7 + rng.NextDouble() * 0.6);
            double bow2 = arc * (0.7 + rng.NextDouble() * 0.6);

            c1x = m1x + nx * bow1;
            c1y = m1y + ny * bow1;
            c2x = m2x + nx * bow2;
            c2y = m2y + ny * bow2;
        }

        // Cubic Bezier for one axis.
        private static double Bezier(double p0, double p1, double p2, double p3, double t)
        {
            double u = 1 - t;
            return u * u * u * p0
                 + 3 * u * u * t * p1
                 + 3 * u * t * t * p2
                 + t * t * t * p3;
        }

        // Smootherstep easing -> accelerate then decelerate.
        private static double EaseInOut(double t)
        {
            return t * t * t * (t * (t * 6 - 15) + 10);
        }
    }
}
