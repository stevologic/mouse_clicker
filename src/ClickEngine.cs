using System;
using System.Threading;

namespace ClickForge
{
    // Runs a clicking session on a dedicated background thread. The UI thread
    // only ever calls Start/Stop and subscribes to the events; all synthetic
    // input happens off the UI thread so the window stays responsive.
    //
    // Each run gets its own Session token (cancellation flag), Profile clone,
    // and Random. A rapid Stop -> Start can briefly overlap with the previous
    // thread winding down, but the old thread only ever consults its OWN
    // session flag (so it can't be revived by the new run starting), and a
    // superseded session never raises Stopped (so it can't clobber the UI
    // state of the run that replaced it).
    internal class ClickEngine
    {
        private class Session
        {
            public volatile bool Running = true;
        }

        private Session _session;

        // Fired (on the engine thread) after every completed click event.
        public event Action<long> ClickPerformed;
        // Fired when a run ends, for any reason. Argument = human reason text.
        public event Action<string> Stopped;
        // Fired for countdown / status updates.
        public event Action<string> Status;

        public bool IsRunning
        {
            get
            {
                Session s = _session;
                return s != null && s.Running;
            }
        }

        public void Start(Profile profile)
        {
            if (IsRunning)
                return;

            Profile p = Clone(profile);
            p.Normalize();

            Session s = new Session();
            _session = s;

            Thread t = new Thread(delegate() { RunLoop(s, p); });
            t.IsBackground = true;
            t.Name = "ClickForge.Engine";
            t.Start();
        }

        public void Stop()
        {
            Session s = _session;
            if (s != null)
                s.Running = false;
        }

        private void RunLoop(Session s, Profile p)
        {
            // Per-run RNG: Random is not thread-safe, and a stale thread from a
            // previous session may still be finishing while this one starts.
            Random rng = new Random(unchecked(Environment.TickCount * 31
                + Thread.CurrentThread.ManagedThreadId));

            long count = 0;
            string reason = "Stopped.";
            NativeMethods.POINT origin = InputSimulator.GetCursor();

            try
            {
                if (!Countdown(s, p.StartDelayMs))
                {
                    reason = "Cancelled before start.";
                    return; // the finally block reports the finish
                }

                Raise(Status, "Running...");

                DateTime endAt = DateTime.UtcNow.AddSeconds(p.DurationSeconds);
                int seqIndex = 0;

                while (s.Running)
                {
                    if (p.RepeatMode == RepeatMode.Count && count >= p.RepeatCount)
                    {
                        reason = "Finished: reached " + p.RepeatCount + " clicks.";
                        break;
                    }
                    if (p.RepeatMode == RepeatMode.Duration && DateTime.UtcNow >= endAt)
                    {
                        reason = "Finished: duration elapsed.";
                        break;
                    }

                    int tx, ty;
                    bool haveTarget = ComputeTarget(p, rng, seqIndex, out tx, out ty);

                    if (haveTarget)
                    {
                        ApplyJitter(p, rng, ref tx, ref ty);
                        HumanMotion.MoveTo(tx, ty, p.MovementMode,
                            p.MovementDurationMs, rng,
                            delegate { return !s.Running; });
                        if (!s.Running) break;
                    }

                    PerformAction(s, p, rng);
                    count++;
                    Raise(ClickPerformed, count);

                    if (p.PositionMode == PositionMode.PointSequence &&
                        p.Points.Count > 0)
                    {
                        seqIndex++;
                        if (seqIndex >= p.Points.Count)
                        {
                            if (p.SequenceLoop)
                            {
                                seqIndex = 0;
                            }
                            else
                            {
                                reason = "Finished: sequence complete.";
                                break;
                            }
                        }
                    }

                    int interval = RandomBetween(rng, p.IntervalMinMs, p.IntervalMaxMs);
                    if (!InterruptibleSleep(s, interval))
                        break;
                }
            }
            catch (Exception ex)
            {
                reason = "Error: " + ex.Message;
            }
            finally
            {
                if (p.ReturnToOrigin)
                {
                    try { InputSimulator.MoveTo(origin.X, origin.Y); }
                    catch { }
                }
                s.Running = false;
                // Only the current session gets to report; a superseded run
                // stays silent so it can't overwrite the new run's UI state.
                if (_session == s)
                    Raise(Stopped, reason);
            }
        }

        // ---- Target computation ------------------------------------------

        private static bool ComputeTarget(Profile p, Random rng, int seqIndex, out int x, out int y)
        {
            switch (p.PositionMode)
            {
                case PositionMode.CurrentCursor:
                    // Only move if jitter is requested; otherwise click in place.
                    if (p.JitterRadius <= 0)
                    {
                        x = 0; y = 0;
                        return false;
                    }
                    NativeMethods.POINT c = InputSimulator.GetCursor();
                    x = c.X; y = c.Y;
                    return true;

                case PositionMode.FixedPoint:
                    x = p.FixedX; y = p.FixedY;
                    return true;

                case PositionMode.RandomInRegion:
                    int left = Math.Min(p.RegionLeft, p.RegionRight);
                    int right = Math.Max(p.RegionLeft, p.RegionRight);
                    int top = Math.Min(p.RegionTop, p.RegionBottom);
                    int bottom = Math.Max(p.RegionTop, p.RegionBottom);
                    if (right <= left) right = left + 1;
                    if (bottom <= top) bottom = top + 1;
                    x = rng.Next(left, right + 1);
                    y = rng.Next(top, bottom + 1);
                    return true;

                case PositionMode.PointSequence:
                    if (p.Points.Count == 0)
                    {
                        x = 0; y = 0;
                        return false;
                    }
                    int idx = seqIndex % p.Points.Count;
                    ClickPoint pt = p.Points[idx];
                    x = pt.X; y = pt.Y;
                    return true;
            }
            x = 0; y = 0;
            return false;
        }

        private static void ApplyJitter(Profile p, Random rng, ref int x, ref int y)
        {
            int r = p.JitterRadius;
            if (r <= 0) return;
            // Uniform-ish disc: retry until inside radius (cheap for small r).
            for (int i = 0; i < 8; i++)
            {
                int ox = rng.Next(-r, r + 1);
                int oy = rng.Next(-r, r + 1);
                if (ox * ox + oy * oy <= r * r)
                {
                    x += ox; y += oy;
                    return;
                }
            }
        }

        // ---- Action ------------------------------------------------------

        private static void PerformAction(Session s, Profile p, Random rng)
        {
            int hold = RandomBetween(rng, p.HoldMinMs, p.HoldMaxMs);
            switch (p.Action)
            {
                case ClickAction.Single:
                    InputSimulator.Click(p.Button, hold);
                    break;
                case ClickAction.Double:
                    InputSimulator.Click(p.Button, hold);
                    PrecisionSleep.Sleep(GapForDouble(rng));
                    InputSimulator.Click(p.Button, hold);
                    break;
                case ClickAction.Triple:
                    InputSimulator.Click(p.Button, hold);
                    PrecisionSleep.Sleep(GapForDouble(rng));
                    InputSimulator.Click(p.Button, hold);
                    PrecisionSleep.Sleep(GapForDouble(rng));
                    InputSimulator.Click(p.Button, hold);
                    break;
                case ClickAction.MultiClick:
                    int n = p.ClicksPerEvent;
                    for (int i = 0; i < n; i++)
                    {
                        if (!s.Running) return;
                        InputSimulator.Click(p.Button, hold);
                        if (i < n - 1) PrecisionSleep.Sleep(GapForDouble(rng));
                    }
                    break;
                case ClickAction.ScrollUp:
                    InputSimulator.ScrollWheel(Math.Max(1, p.ClicksPerEvent));
                    break;
                case ClickAction.ScrollDown:
                    InputSimulator.ScrollWheel(-Math.Max(1, p.ClicksPerEvent));
                    break;
                case ClickAction.MouseDown:
                    InputSimulator.MouseDown(p.Button);
                    break;
                case ClickAction.MouseUp:
                    InputSimulator.MouseUp(p.Button);
                    break;
            }
        }

        // A small human-like gap between the presses of a multi-click.
        private static int GapForDouble(Random rng)
        {
            return RandomBetween(rng, 30, 60);
        }

        // ---- Helpers -----------------------------------------------------

        private static int RandomBetween(Random rng, int min, int max)
        {
            if (max <= min) return min;
            return rng.Next(min, max + 1);
        }

        // Sleep that wakes early if Stop is pressed. Returns false if stopped.
        private static bool InterruptibleSleep(Session s, int ms)
        {
            if (ms <= 0)
                return s.Running;

            int remaining = ms;
            while (remaining > 0)
            {
                if (!s.Running) return false;
                int chunk = remaining > 20 ? 20 : remaining;
                PrecisionSleep.Sleep(chunk);
                remaining -= chunk;
            }
            return s.Running;
        }

        // Pre-start countdown with 1-second status ticks.
        private bool Countdown(Session s, int ms)
        {
            int remaining = ms;
            while (remaining > 0)
            {
                if (!s.Running) return false;
                int secs = (int)Math.Ceiling(remaining / 1000.0);
                Raise(Status, "Starting in " + secs + "...");
                int chunk = remaining > 100 ? 100 : remaining;
                PrecisionSleep.Sleep(chunk);
                remaining -= chunk;
            }
            return s.Running;
        }

        private static void Raise(Action<string> ev, string arg)
        {
            if (ev != null) ev(arg);
        }

        private static void Raise(Action<long> ev, long arg)
        {
            if (ev != null) ev(arg);
        }

        private static Profile Clone(Profile p)
        {
            Profile c = new Profile();
            c.Button = p.Button;
            c.Action = p.Action;
            c.ClicksPerEvent = p.ClicksPerEvent;
            c.HoldMinMs = p.HoldMinMs;
            c.HoldMaxMs = p.HoldMaxMs;
            c.IntervalMinMs = p.IntervalMinMs;
            c.IntervalMaxMs = p.IntervalMaxMs;
            c.RepeatMode = p.RepeatMode;
            c.RepeatCount = p.RepeatCount;
            c.DurationSeconds = p.DurationSeconds;
            c.StartDelayMs = p.StartDelayMs;
            c.PositionMode = p.PositionMode;
            c.FixedX = p.FixedX;
            c.FixedY = p.FixedY;
            c.RegionLeft = p.RegionLeft;
            c.RegionTop = p.RegionTop;
            c.RegionRight = p.RegionRight;
            c.RegionBottom = p.RegionBottom;
            c.Points = new System.Collections.Generic.List<ClickPoint>();
            foreach (ClickPoint pt in p.Points)
                c.Points.Add(new ClickPoint(pt.X, pt.Y));
            c.SequenceLoop = p.SequenceLoop;
            c.MovementMode = p.MovementMode;
            c.MovementDurationMs = p.MovementDurationMs;
            c.JitterRadius = p.JitterRadius;
            c.ReturnToOrigin = p.ReturnToOrigin;
            c.ToggleHotkeyVk = p.ToggleHotkeyVk;
            c.StopHotkeyVk = p.StopHotkeyVk;
            c.ApiKey = p.ApiKey;
            c.Model = p.Model;
            return c;
        }
    }
}
