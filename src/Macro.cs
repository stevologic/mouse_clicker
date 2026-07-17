using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ClickForge
{
    public enum StepKind
    {
        Move = 0,
        Down = 1,   // mouse button pressed
        Up = 2,     // mouse button released
        KeyDown = 3, // keyboard key pressed
        KeyUp = 4    // keyboard key released
    }

    // One captured event. DelayMs is how long to wait before performing it
    // (reproducing the recorded timing). Down + moves + Up reproduce a
    // click-and-drag; KeyDown/KeyUp carry a virtual-key code in Vk.
    public class RecordedStep
    {
        public StepKind Kind { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public MouseButton Button { get; set; }
        public int Vk { get; set; }
        public int DelayMs { get; set; }

        public RecordedStep() { }
    }

    // A savable recording: the steps plus optional window-relative metadata so
    // playback can re-anchor to the target window if it has since moved.
    public class Macro
    {
        public List<RecordedStep> Steps { get; set; }
        public bool Relative { get; set; }
        public string WindowTitle { get; set; }
        public int OriginX { get; set; }
        public int OriginY { get; set; }

        public Macro()
        {
            Steps = new List<RecordedStep>();
            WindowTitle = "";
        }
    }

    // Records the user's real mouse (and optionally keyboard) input system-wide
    // via low-level hooks. Movement is sampled; clicks/keys are always captured.
    // Events over the app's own window and injected/synthetic events are ignored.
    internal class MacroRecorder
    {
        private const int MoveMinIntervalMs = 15;   // <= ~66 samples/sec
        private const int MoveMinDistSq = 9;        // ignore sub-3px twitches
        private const int MaxSteps = 20000;         // safety cap

        private readonly List<RecordedStep> _steps = new List<RecordedStep>();
        private NativeMethods.LowLevelMouseProc _mouseProc; // kept alive while hooked
        private NativeMethods.LowLevelMouseProc _keyProc;
        private IntPtr _mouseHook = IntPtr.Zero;
        private IntPtr _keyHook = IntPtr.Zero;
        private Form _owner;
        private int[] _reservedVks = new int[0]; // app hotkeys — never recorded

        private int _lastEventTick;
        private int _lastMoveTick;
        private int _lastX, _lastY;
        private bool _haveLast;
        private int _clickCount;
        private int _moveCount;
        private int _keyCount;

        // Set before Start() to also record keyboard input / anchor to a window.
        public bool RecordKeyboard { get; set; }
        public bool RecordRelative { get; set; }

        // Window this recording is anchored to (captured at Start), for
        // window-relative playback.
        public string WindowTitle { get; private set; }
        public int OriginX { get; private set; }
        public int OriginY { get; private set; }

        public List<RecordedStep> Steps { get { return _steps; } }
        public bool IsRecording { get { return _mouseHook != IntPtr.Zero; } }
        public int Count { get { return _steps.Count; } }
        public int ClickCount { get { return _clickCount; } }
        public int MoveCount { get { return _moveCount; } }
        public int KeyCount { get { return _keyCount; } }

        public void SetReservedKeys(int[] vks)
        {
            _reservedVks = vks ?? new int[0];
        }

        public void Start(Form owner)
        {
            if (IsRecording) return;
            _owner = owner;
            _lastEventTick = Environment.TickCount;
            WindowTitle = "";
            OriginX = 0;
            OriginY = 0;

            _mouseProc = MouseHookProc;
            _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc,
                NativeMethods.GetModuleHandle(null), 0);

            if (RecordKeyboard)
            {
                _keyProc = KeyHookProc;
                _keyHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyProc,
                    NativeMethods.GetModuleHandle(null), 0);
            }
        }

        public void Stop()
        {
            if (_mouseHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
            if (_keyHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_keyHook);
                _keyHook = IntPtr.Zero;
            }
            _mouseProc = null;
            _keyProc = null;
        }

        public void Clear()
        {
            _steps.Clear();
            _clickCount = 0;
            _moveCount = 0;
            _keyCount = 0;
            _haveLast = false;
        }

        // Load an existing recording's steps in so it can be played/saved/etc.
        public void Adopt(Macro m)
        {
            Clear();
            WindowTitle = m != null && m.WindowTitle != null ? m.WindowTitle : "";
            OriginX = m != null ? m.OriginX : 0;
            OriginY = m != null ? m.OriginY : 0;
            if (m != null && m.Steps != null)
            {
                foreach (RecordedStep s in m.Steps)
                {
                    _steps.Add(s);
                    if (s.Kind == StepKind.Move) _moveCount++;
                    else if (s.Kind == StepKind.Down) _clickCount++;
                    else if (s.Kind == StepKind.KeyDown) _keyCount++;
                }
            }
        }

        // Anchor to the window under the first recorded click, so window-relative
        // playback can offset by how far it later moved.
        private void CaptureBaseWindowAt(int x, int y)
        {
            try
            {
                IntPtr root = WindowTools.RootWindowAt(x, y);
                if (root == IntPtr.Zero) return;
                if (_owner != null && !_owner.IsDisposed && root == _owner.Handle) return;
                NativeMethods.RECT r;
                if (!NativeMethods.GetWindowRect(root, out r)) return;
                OriginX = r.Left;
                OriginY = r.Top;
                WindowTitle = WindowTools.TitleOf(root);
            }
            catch { }
        }

        // ---- Mouse ----------------------------------------------------------

        private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode == NativeMethods.HC_ACTION)
            {
                int msg = wParam.ToInt32();
                if (msg == NativeMethods.WM_MOUSEMOVE
                    || msg == NativeMethods.WM_LBUTTONDOWN
                    || msg == NativeMethods.WM_RBUTTONDOWN
                    || msg == NativeMethods.WM_MBUTTONDOWN
                    || msg == NativeMethods.WM_LBUTTONUP
                    || msg == NativeMethods.WM_RBUTTONUP
                    || msg == NativeMethods.WM_MBUTTONUP)
                {
                    var data = (NativeMethods.MSLLHOOKSTRUCT)Marshal.PtrToStructure(
                        lParam, typeof(NativeMethods.MSLLHOOKSTRUCT));
                    bool injected = (data.flags & NativeMethods.LLMHF_INJECTED) != 0;
                    bool overOwner = OwnerContains(data.pt.X, data.pt.Y);
                    RecordedStep step = Capture(msg, data.pt.X, data.pt.Y, injected, overOwner, Environment.TickCount);
                    // Anchor window-relative recordings to the first clicked window.
                    if (step != null && step.Kind == StepKind.Down
                        && RecordRelative && string.IsNullOrEmpty(WindowTitle))
                        CaptureBaseWindowAt(data.pt.X, data.pt.Y);
                }
            }
            return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private bool OwnerContains(int x, int y)
        {
            Form o = _owner;
            return o != null && !o.IsDisposed && o.Visible
                && o.WindowState != FormWindowState.Minimized
                && o.Bounds.Contains(x, y);
        }

        // Pure mouse-capture decision (unit-tested via --audit).
        internal RecordedStep Capture(int msg, int px, int py, bool injected, bool overOwner, int nowTick)
        {
            if (injected || overOwner) return null;
            if (_steps.Count >= MaxSteps) return null;

            StepKind kind;
            MouseButton btn = MouseButton.Left;
            switch (msg)
            {
                case NativeMethods.WM_LBUTTONDOWN: kind = StepKind.Down; btn = MouseButton.Left; break;
                case NativeMethods.WM_RBUTTONDOWN: kind = StepKind.Down; btn = MouseButton.Right; break;
                case NativeMethods.WM_MBUTTONDOWN: kind = StepKind.Down; btn = MouseButton.Middle; break;
                case NativeMethods.WM_LBUTTONUP: kind = StepKind.Up; btn = MouseButton.Left; break;
                case NativeMethods.WM_RBUTTONUP: kind = StepKind.Up; btn = MouseButton.Right; break;
                case NativeMethods.WM_MBUTTONUP: kind = StepKind.Up; btn = MouseButton.Middle; break;
                case NativeMethods.WM_MOUSEMOVE: kind = StepKind.Move; break;
                default: return null;
            }

            if (kind == StepKind.Move && _haveLast)
            {
                if (nowTick - _lastMoveTick < MoveMinIntervalMs) return null;
                int ddx = px - _lastX, ddy = py - _lastY;
                if (ddx * ddx + ddy * ddy < MoveMinDistSq) return null;
            }

            RecordedStep step = new RecordedStep();
            step.Kind = kind;
            step.X = px;
            step.Y = py;
            step.Button = btn;
            step.DelayMs = NextDelay(nowTick);
            _steps.Add(step);

            _lastX = px;
            _lastY = py;
            _haveLast = true;
            if (kind == StepKind.Move) { _moveCount++; _lastMoveTick = nowTick; }
            else if (kind == StepKind.Down) _clickCount++;
            return step;
        }

        // ---- Keyboard -------------------------------------------------------

        private IntPtr KeyHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode == NativeMethods.HC_ACTION)
            {
                int msg = wParam.ToInt32();
                if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_KEYUP
                    || msg == NativeMethods.WM_SYSKEYDOWN || msg == NativeMethods.WM_SYSKEYUP)
                {
                    var data = (NativeMethods.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(
                        lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));
                    bool injected = (data.flags & NativeMethods.LLKHF_INJECTED) != 0;
                    CaptureKey(msg, (int)data.vkCode, injected, Environment.TickCount);
                }
            }
            return NativeMethods.CallNextHookEx(_keyHook, nCode, wParam, lParam);
        }

        // Pure keyboard-capture decision (unit-tested via --audit).
        internal RecordedStep CaptureKey(int msg, int vk, bool injected, int nowTick)
        {
            if (injected) return null;
            if (_steps.Count >= MaxSteps) return null;
            for (int i = 0; i < _reservedVks.Length; i++)
                if (_reservedVks[i] == vk) return null; // don't record our own hotkeys

            StepKind kind;
            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN) kind = StepKind.KeyDown;
            else if (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP) kind = StepKind.KeyUp;
            else return null;

            // Auto-repeat sends a stream of KEYDOWNs while a key is held; collapse
            // them (record only the first press until the matching release).
            if (kind == StepKind.KeyDown && IsKeyHeld(vk)) return null;

            RecordedStep step = new RecordedStep();
            step.Kind = kind;
            step.Vk = vk;
            step.DelayMs = NextDelay(nowTick);
            _steps.Add(step);
            if (kind == StepKind.KeyDown) _keyCount++;
            return step;
        }

        // True if the last recorded down/up for this vk was a still-held down.
        private bool IsKeyHeld(int vk)
        {
            for (int i = _steps.Count - 1; i >= 0; i--)
            {
                RecordedStep s = _steps[i];
                if ((s.Kind == StepKind.KeyDown || s.Kind == StepKind.KeyUp) && s.Vk == vk)
                    return s.Kind == StepKind.KeyDown;
            }
            return false;
        }

        private int NextDelay(int nowTick)
        {
            int delay = _steps.Count == 0 ? 0 : (nowTick - _lastEventTick);
            if (delay < 0) delay = 0;
            if (delay > 10000) delay = 10000;
            _lastEventTick = nowTick;
            return delay;
        }
    }

    // Replays a recorded sequence on a background thread with the captured
    // timing (optionally sped up/slowed down and offset for a moved window),
    // looped or a fixed number of times.
    internal class MacroPlayer
    {
        private Thread _thread;
        private volatile bool _stop;
        private volatile bool _active;

        public bool IsPlaying { get { return _active; } }

        public event Action<int> Progress;    // clicks performed so far
        public event Action<string> Finished; // human-readable reason

        public void Play(List<RecordedStep> steps, int repeatCount, double speed, int offsetX, int offsetY)
        {
            if (_active || steps == null || steps.Count == 0) return;
            RecordedStep[] plan = steps.ToArray();
            _stop = false;
            _active = true;
            _thread = new Thread(delegate()
            {
                int clicks = 0;
                bool[] pressed = new bool[3];
                var keysDown = new List<int>();
                try
                {
                    int loops = 0;
                    while (!_stop && (repeatCount == 0 || loops < repeatCount))
                    {
                        for (int i = 0; i < plan.Length && !_stop; i++)
                        {
                            RecordedStep s = plan[i];
                            SleepInterruptible(ScaleDelay(s.DelayMs, speed));
                            if (_stop) break;
                            switch (s.Kind)
                            {
                                case StepKind.Move:
                                    InputSimulator.MoveTo(s.X + offsetX, s.Y + offsetY);
                                    break;
                                case StepKind.Down:
                                    InputSimulator.MoveTo(s.X + offsetX, s.Y + offsetY);
                                    InputSimulator.MouseDown(s.Button);
                                    MarkPressed(pressed, s.Button, true);
                                    clicks++;
                                    Action<int> p = Progress;
                                    if (p != null) p(clicks);
                                    break;
                                case StepKind.Up:
                                    InputSimulator.MoveTo(s.X + offsetX, s.Y + offsetY);
                                    InputSimulator.MouseUp(s.Button);
                                    MarkPressed(pressed, s.Button, false);
                                    break;
                                case StepKind.KeyDown:
                                    InputSimulator.KeyDown(s.Vk);
                                    if (!keysDown.Contains(s.Vk)) keysDown.Add(s.Vk);
                                    break;
                                case StepKind.KeyUp:
                                    InputSimulator.KeyUp(s.Vk);
                                    keysDown.Remove(s.Vk);
                                    break;
                            }
                        }
                        loops++;
                    }
                }
                catch { }
                ReleaseAll(pressed, keysDown);
                _active = false;
                Action<string> f = Finished;
                if (f != null) f(_stop ? "Playback stopped." : "Playback complete.");
            });
            _thread.IsBackground = true;
            _thread.Start();
        }

        public void Stop() { _stop = true; }

        // Scale a recorded delay by the playback speed (2x -> half the wait).
        internal static int ScaleDelay(int ms, double speed)
        {
            if (speed <= 0) speed = 1.0;
            double v = ms / speed;
            if (v < 0) v = 0;
            if (v > int.MaxValue) return int.MaxValue;
            return (int)Math.Round(v);
        }

        private static void MarkPressed(bool[] pressed, MouseButton b, bool down)
        {
            int i = (int)b;
            if (i >= 0 && i < pressed.Length) pressed[i] = down;
        }

        // Release anything still held so a partial recording / early Stop can't
        // leave a button or key stuck down.
        private static void ReleaseAll(bool[] pressed, List<int> keysDown)
        {
            for (int i = 0; i < pressed.Length; i++)
                if (pressed[i]) { try { InputSimulator.MouseUp((MouseButton)i); } catch { } }
            for (int i = 0; i < keysDown.Count; i++)
                { try { InputSimulator.KeyUp(keysDown[i]); } catch { } }
        }

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

    // Window lookup helpers for window-relative recording/playback.
    internal static class WindowTools
    {
        public static string TitleOf(IntPtr hWnd)
        {
            try
            {
                int len = NativeMethods.GetWindowTextLength(hWnd);
                if (len <= 0) return "";
                var sb = new StringBuilder(len + 1);
                NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
                return sb.ToString();
            }
            catch { return ""; }
        }

        // The top-level (root) window under a screen point.
        public static IntPtr RootWindowAt(int x, int y)
        {
            NativeMethods.POINT p = new NativeMethods.POINT();
            p.X = x; p.Y = y;
            IntPtr h = NativeMethods.WindowFromPoint(p);
            if (h == IntPtr.Zero) return IntPtr.Zero;
            IntPtr root = NativeMethods.GetAncestor(h, NativeMethods.GA_ROOT);
            return root != IntPtr.Zero ? root : h;
        }

        // Current top-left of the first visible window whose title matches, or
        // null if none is found. Used to offset a relative recording.
        public static NativeMethods.RECT? FindWindowRectByTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return null;
            IntPtr found = IntPtr.Zero;
            NativeMethods.EnumWindows(delegate(IntPtr hWnd, IntPtr l)
            {
                if (!NativeMethods.IsWindowVisible(hWnd)) return true;
                if (TitleOf(hWnd) == title) { found = hWnd; return false; }
                return true;
            }, IntPtr.Zero);
            if (found == IntPtr.Zero) return null;
            NativeMethods.RECT r;
            if (!NativeMethods.GetWindowRect(found, out r)) return null;
            return r;
        }
    }
}
