using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ClickForge
{
    // One captured click: where, which button, and how long to wait before it.
    public class RecordedStep
    {
        public int X { get; set; }
        public int Y { get; set; }
        public MouseButton Button { get; set; }
        public int DelayMs { get; set; }   // wait before performing this click

        public RecordedStep() { }
    }

    // Records the user's real mouse clicks system-wide via a low-level hook.
    // Clicks on the app's own window are ignored (so the UI stays usable while
    // recording), as are injected/synthetic events.
    internal class MacroRecorder
    {
        private readonly List<RecordedStep> _steps = new List<RecordedStep>();
        private NativeMethods.LowLevelMouseProc _proc; // kept alive while hooked
        private IntPtr _hook = IntPtr.Zero;
        private int _lastTick;
        private Form _owner;

        // Raised (on the UI thread) whenever the recorded set changes.
        public event Action Changed;

        public List<RecordedStep> Steps { get { return _steps; } }
        public bool IsRecording { get { return _hook != IntPtr.Zero; } }
        public int Count { get { return _steps.Count; } }

        public void Start(Form owner)
        {
            if (IsRecording) return;
            _owner = owner;
            _lastTick = Environment.TickCount;
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
            Raise();
        }

        private void Raise()
        {
            Action h = Changed;
            if (h != null) h();
        }

        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode == NativeMethods.HC_ACTION)
            {
                int msg = wParam.ToInt32();
                MouseButton btn = MouseButton.Left;
                bool isDown = true;
                switch (msg)
                {
                    case NativeMethods.WM_LBUTTONDOWN: btn = MouseButton.Left; break;
                    case NativeMethods.WM_RBUTTONDOWN: btn = MouseButton.Right; break;
                    case NativeMethods.WM_MBUTTONDOWN: btn = MouseButton.Middle; break;
                    default: isDown = false; break;
                }
                if (isDown)
                {
                    var data = (NativeMethods.MSLLHOOKSTRUCT)Marshal.PtrToStructure(
                        lParam, typeof(NativeMethods.MSLLHOOKSTRUCT));
                    bool injected = (data.flags & NativeMethods.LLMHF_INJECTED) != 0;
                    bool overOwner = _owner != null && !_owner.IsDisposed && _owner.Visible
                        && _owner.WindowState != FormWindowState.Minimized
                        && _owner.Bounds.Contains(data.pt.X, data.pt.Y);
                    if (!injected && !overOwner)
                    {
                        int now = Environment.TickCount;
                        int delay = now - _lastTick;
                        if (delay < 0) delay = 0;
                        if (delay > 10000) delay = 10000; // cap absurd idle gaps
                        _lastTick = now;

                        RecordedStep step = new RecordedStep();
                        step.X = data.pt.X;
                        step.Y = data.pt.Y;
                        step.Button = btn;
                        step.DelayMs = _steps.Count == 0 ? 0 : delay;
                        _steps.Add(step);
                        Raise();
                    }
                }
            }
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }
    }

    // Replays a recorded click sequence on a background thread with the captured
    // timing, either a fixed number of times or looped until stopped.
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
                int done = 0;
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
                            InputSimulator.Click(s.Button, 15);
                            done++;
                            Action<int> p = Progress;
                            if (p != null) p(done);
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
