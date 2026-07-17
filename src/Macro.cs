using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ClickForge
{
    public enum StepKind
    {
        Move = 0,
        Click = 1
    }

    // One captured event: a cursor move, or a click. DelayMs is how long to wait
    // before performing this step (reproducing the recorded timing). Button is
    // only meaningful when Kind == Click.
    public class RecordedStep
    {
        public StepKind Kind { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public MouseButton Button { get; set; }
        public int DelayMs { get; set; }

        public RecordedStep() { }
    }

    // Records the user's real mouse movement and clicks system-wide via a
    // low-level hook. Movement is sampled (throttled by time + distance) so a
    // recording stays compact; clicks are always captured. Events over the app's
    // own window and injected/synthetic events are ignored.
    internal class MacroRecorder
    {
        // Movement sampling limits — keep the stream compact and the hook cheap.
        private const int MoveMinIntervalMs = 15;   // <= ~66 samples/sec
        private const int MoveMinDistSq = 9;        // ignore sub-3px twitches
        private const int MaxSteps = 20000;         // safety cap (~5 min of motion)

        private readonly List<RecordedStep> _steps = new List<RecordedStep>();
        private NativeMethods.LowLevelMouseProc _proc; // kept alive while hooked
        private IntPtr _hook = IntPtr.Zero;
        private Form _owner;

        private int _lastEventTick;   // timing baseline for DelayMs
        private int _lastMoveTick;    // last recorded MOVE (for interval throttle)
        private int _lastX, _lastY;   // last recorded position (for distance throttle)
        private bool _haveLast;
        private int _clickCount;
        private int _moveCount;

        public List<RecordedStep> Steps { get { return _steps; } }
        public bool IsRecording { get { return _hook != IntPtr.Zero; } }
        public int Count { get { return _steps.Count; } }
        public int ClickCount { get { return _clickCount; } }
        public int MoveCount { get { return _moveCount; } }

        public void Start(Form owner)
        {
            if (IsRecording) return;
            _owner = owner;
            _lastEventTick = Environment.TickCount;
            _proc = HookProc;
            _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc,
                NativeMethods.GetModuleHandle(null), 0);
        }

        public void Stop()
        {
            if (_hook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
            _proc = null;
        }

        public void Clear()
        {
            _steps.Clear();
            _clickCount = 0;
            _moveCount = 0;
            _haveLast = false;
        }

        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode == NativeMethods.HC_ACTION)
            {
                int msg = wParam.ToInt32();
                // Only marshal the payload for the messages we actually record.
                if (msg == NativeMethods.WM_MOUSEMOVE
                    || msg == NativeMethods.WM_LBUTTONDOWN
                    || msg == NativeMethods.WM_RBUTTONDOWN
                    || msg == NativeMethods.WM_MBUTTONDOWN)
                {
                    var data = (NativeMethods.MSLLHOOKSTRUCT)Marshal.PtrToStructure(
                        lParam, typeof(NativeMethods.MSLLHOOKSTRUCT));
                    bool injected = (data.flags & NativeMethods.LLMHF_INJECTED) != 0;
                    bool overOwner = OwnerContains(data.pt.X, data.pt.Y);
                    Capture(msg, data.pt.X, data.pt.Y, injected, overOwner, Environment.TickCount);
                }
            }
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private bool OwnerContains(int x, int y)
        {
            Form o = _owner;
            // The hook fires on the UI thread, so touching the form is safe.
            return o != null && !o.IsDisposed && o.Visible
                && o.WindowState != FormWindowState.Minimized
                && o.Bounds.Contains(x, y);
        }

        // The pure capture decision — returns the step recorded, or null if the
        // event was filtered/throttled. Separated out so it can be unit-tested
        // without a real hook (see --audit).
        internal RecordedStep Capture(int msg, int px, int py, bool injected, bool overOwner, int nowTick)
        {
            if (injected || overOwner) return null;
            if (_steps.Count >= MaxSteps) return null;

            StepKind kind;
            MouseButton btn = MouseButton.Left;
            switch (msg)
            {
                case NativeMethods.WM_LBUTTONDOWN: kind = StepKind.Click; btn = MouseButton.Left; break;
                case NativeMethods.WM_RBUTTONDOWN: kind = StepKind.Click; btn = MouseButton.Right; break;
                case NativeMethods.WM_MBUTTONDOWN: kind = StepKind.Click; btn = MouseButton.Middle; break;
                case NativeMethods.WM_MOUSEMOVE: kind = StepKind.Move; break;
                default: return null;
            }

            if (kind == StepKind.Move && _haveLast)
            {
                if (nowTick - _lastMoveTick < MoveMinIntervalMs) return null;
                int ddx = px - _lastX, ddy = py - _lastY;
                if (ddx * ddx + ddy * ddy < MoveMinDistSq) return null;
            }

            int delay = _steps.Count == 0 ? 0 : (nowTick - _lastEventTick);
            if (delay < 0) delay = 0;
            if (delay > 10000) delay = 10000; // cap absurd idle gaps
            _lastEventTick = nowTick;

            RecordedStep step = new RecordedStep();
            step.Kind = kind;
            step.X = px;
            step.Y = py;
            step.Button = btn;
            step.DelayMs = delay;
            _steps.Add(step);

            _lastX = px;
            _lastY = py;
            _haveLast = true;
            if (kind == StepKind.Move) { _moveCount++; _lastMoveTick = nowTick; }
            else _clickCount++;
            return step;
        }
    }

    // Replays a recorded movement/click sequence on a background thread with the
    // captured timing, either a fixed number of times or looped until stopped.
    internal class MacroPlayer
    {
        private Thread _thread;
        private volatile bool _stop;

        public bool IsPlaying { get { return _thread != null && _thread.IsAlive; } }

        // Both fire on the worker thread — marshal to the UI thread in handlers.
        public event Action<int> Progress;    // total clicks performed so far
        public event Action<string> Finished; // human-readable reason

        public void Play(List<RecordedStep> steps, int repeatCount /* 0 = loop forever */)
        {
            if (IsPlaying || steps == null || steps.Count == 0) return;
            RecordedStep[] plan = steps.ToArray();
            _stop = false;
            _thread = new Thread(delegate()
            {
                int clicks = 0;
                try
                {
                    int loops = 0;
                    while (!_stop && (repeatCount == 0 || loops < repeatCount))
                    {
                        for (int i = 0; i < plan.Length && !_stop; i++)
                        {
                            RecordedStep s = plan[i];
                            SleepInterruptible(s.DelayMs);
                            if (_stop) break;
                            InputSimulator.MoveTo(s.X, s.Y);
                            if (s.Kind == StepKind.Click)
                            {
                                InputSimulator.Click(s.Button, 15);
                                clicks++;
                                Action<int> p = Progress;
                                if (p != null) p(clicks);
                            }
                        }
                        loops++;
                    }
                }
                catch { }
                Action<string> f = Finished;
                if (f != null) f(_stop ? "Playback stopped." : "Playback complete.");
            });
            _thread.IsBackground = true;
            _thread.Start();
        }

        public void Stop() { _stop = true; }

        private void SleepInterruptible(int ms)
        {
            int slept = 0;
            while (slept < ms && !_stop)
            {
                int chunk = Math.Min(30, ms - slept);
                Thread.Sleep(chunk);
                slept += chunk;
            }
        }
    }
}
