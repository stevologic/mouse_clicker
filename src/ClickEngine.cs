using System;
using System.Threading;

namespace ClickForge
{
    // Runs a clicking session on a dedicated background thread. The UI thread
    // only ever calls Start/Stop and subscribes to the events; all synthetic
    // input happens off the UI thread so the window stays responsive.
    internal class ClickEngine
    {
        private Thread _thread;
        private volatile bool _running;
        private Profile _profile;
        private readonly Random _rng = new Random();

        // Fired (on the engine thread) after every completed click event.
        public event Action<long> ClickPerformed;
        // Fired when a run ends, for any reason. Argument = human reason text.
        public event Action<string> Stopped;
        // Fired for countdown / status updates.
        public event Action<string> Status;

        public bool IsRunning { get { return _running; } }

        public void Start(Profile profile)
        {
            if (_running)
                return;

            _profile = Clone(profile);
            _profile.Normalize();
            _running = true;

            _thread = new Thread(RunLoop);
            _thread.IsBackground = true;
            _thread.Name = "ClickForge.Engine";
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
        }

        private void RunLoop()
        {
            long count = 0;
            string reason = "Stopped.";
            NativeMethods.POINT origin = InputSimulator.GetCursor();

            try
            {
                if (!Countdown(_profile.StartDelayMs))
                {
                    Finish(count, "Cancelled before start.");
                    return;
                }

                Raise(Status, "Running...");

                DateTime endAt = DateTime.UtcNow.AddSeconds(_profile.DurationSeconds);
                int seqIndex = 0;

                while (_running)
                {
                    if (_profile.RepeatMode == RepeatMode.Count && count >= _profile.RepeatCount)
                    {
                        reason = "Finished: reached " + _profile.RepeatCount + " clicks.";
                        break;
                    }
                    if (_profile.RepeatMode == RepeatMode.Duration && DateTime.UtcNow >= endAt)
                    {
                        reason = "Finished: duration elapsed.";
                        break;
                    }

                    int tx, ty;
                    bool haveTarget = ComputeTarget(seqIndex, out tx, out ty);

                    if (haveTarget)
                    {
                        ApplyJitter(ref tx, ref ty);
                        HumanMotion.MoveTo(tx, ty, _profile.MovementMode,
                            _profile.MovementDurationMs, _rng, IsStopped);
                        if (!_running) break;
                    }

                    PerformAction();
                    count++;
                    Raise(ClickPerformed, count);

                    if (_profile.PositionMode == PositionMode.PointSequence &&
                        _profile.Points.Count > 0)
                    {
                        seqIndex++;
                        if (seqIndex >= _profile.Points.Count)
                        {
                            if (_profile.SequenceLoop)
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

                    int interval = RandomBetween(_profile.IntervalMinMs, _profile.IntervalMaxMs);
                    if (!InterruptibleSleep(interval))
                        break;
                }

                if (_running == false && reason == "Stopped.")
                    reason = "Stopped.";
            }
            catch (Exception ex)
            {
                reason = "Error: " + ex.Message;
            }
            finally
            {
                if (_profile.ReturnToOrigin)
                {
                    try { InputSimulator.MoveTo(origin.X, origin.Y); }
                    catch { }
                }
                Finish(count, reason);
            }
        }

        private void Finish(long count, string reason)
        {
            _running = false;
            Raise(Stopped, reason);
        }

        // ---- Target computation ------------------------------------------

        private bool ComputeTarget(int seqIndex, out int x, out int y)
        {
            switch (_profile.PositionMode)
            {
                case PositionMode.CurrentCursor:
                    // Only move if jitter is requested; otherwise click in place.
                    if (_profile.JitterRadius <= 0)
                    {
                        x = 0; y = 0;
                        return false;
                    }
                    NativeMethods.POINT c = InputSimulator.GetCursor();
                    x = c.X; y = c.Y;
                    return true;

                case PositionMode.FixedPoint:
                    x = _profile.FixedX; y = _profile.FixedY;
                    return true;

                case PositionMode.RandomInRegion:
                    int left = Math.Min(_profile.RegionLeft, _profile.RegionRight);
                    int right = Math.Max(_profile.RegionLeft, _profile.RegionRight);
                    int top = Math.Min(_profile.RegionTop, _profile.RegionBottom);
                    int bottom = Math.Max(_profile.RegionTop, _profile.RegionBottom);
                    if (right <= left) right = left + 1;
                    if (bottom <= top) bottom = top + 1;
                    x = _rng.Next(left, right + 1);
                    y = _rng.Next(top, bottom + 1);
                    return true;

                case PositionMode.PointSequence:
                    if (_profile.Points.Count == 0)
                    {
                        x = 0; y = 0;
                        return false;
                    }
                    int idx = seqIndex % _profile.Points.Count;
                    ClickPoint p = _profile.Points[idx];
                    x = p.X; y = p.Y;
                    return true;
            }
            x = 0; y = 0;
            return false;
        }

        private void ApplyJitter(ref int x, ref int y)
        {
            int r = _profile.JitterRadius;
            if (r <= 0) return;
            // Uniform-ish disc: retry until inside radius (cheap for small r).
            for (int i = 0; i < 8; i++)
            {
                int ox = _rng.Next(-r, r + 1);
                int oy = _rng.Next(-r, r + 1);
                if (ox * ox + oy * oy <= r * r)
                {
                    x += ox; y += oy;
                    return;
                }
            }
        }

        // ---- Action ------------------------------------------------------

        private void PerformAction()
        {
            int hold = RandomBetween(_profile.HoldMinMs, _profile.HoldMaxMs);
            switch (_profile.Action)
            {
                case ClickAction.Single:
                    InputSimulator.Click(_profile.Button, hold);
                    break;
                case ClickAction.Double:
                    InputSimulator.Click(_profile.Button, hold);
                    PrecisionSleep.Sleep(GapForDouble());
                    InputSimulator.Click(_profile.Button, hold);
                    break;
                case ClickAction.Triple:
                    InputSimulator.Click(_profile.Button, hold);
                    PrecisionSleep.Sleep(GapForDouble());
                    InputSimulator.Click(_profile.Button, hold);
                    PrecisionSleep.Sleep(GapForDouble());
                    InputSimulator.Click(_profile.Button, hold);
                    break;
                case ClickAction.MultiClick:
                    int n = _profile.ClicksPerEvent;
                    for (int i = 0; i < n; i++)
                    {
                        if (!_running) return;
                        InputSimulator.Click(_profile.Button, hold);
                        if (i < n - 1) PrecisionSleep.Sleep(GapForDouble());
                    }
                    break;
                case ClickAction.ScrollUp:
                    InputSimulator.ScrollWheel(Math.Max(1, _profile.ClicksPerEvent));
                    break;
                case ClickAction.ScrollDown:
                    InputSimulator.ScrollWheel(-Math.Max(1, _profile.ClicksPerEvent));
                    break;
                case ClickAction.MouseDown:
                    InputSimulator.MouseDown(_profile.Button);
                    break;
                case ClickAction.MouseUp:
                    InputSimulator.MouseUp(_profile.Button);
                    break;
            }
        }

        // A small human-like gap between the presses of a multi-click.
        private int GapForDouble()
        {
            return RandomBetween(30, 60);
        }

        // ---- Helpers -----------------------------------------------------

        private bool IsStopped()
        {
            return !_running;
        }

        private int RandomBetween(int min, int max)
        {
            if (max <= min) return min;
            return _rng.Next(min, max + 1);
        }

        // Sleep that wakes early if Stop is pressed. Returns false if stopped.
        private bool InterruptibleSleep(int ms)
        {
            if (ms <= 0)
                return _running;

            int remaining = ms;
            while (remaining > 0)
            {
                if (!_running) return false;
                int chunk = remaining > 20 ? 20 : remaining;
                PrecisionSleep.Sleep(chunk);
                remaining -= chunk;
            }
            return _running;
        }

        // Pre-start countdown with 1-second status ticks.
        private bool Countdown(int ms)
        {
            int remaining = ms;
            while (remaining > 0)
            {
                if (!_running) return false;
                int secs = (int)Math.Ceiling(remaining / 1000.0);
                Raise(Status, "Starting in " + secs + "...");
                int chunk = remaining > 100 ? 100 : remaining;
                PrecisionSleep.Sleep(chunk);
                remaining -= chunk;
            }
            return _running;
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
