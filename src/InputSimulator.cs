using System;

namespace ClickForge
{
    public enum MouseButton
    {
        Left = 0,
        Right = 1,
        Middle = 2,
        X1 = 3,
        X2 = 4
    }

    // Low-level synthetic input built on SendInput / SetCursorPos.
    // All coordinates are physical (device) pixels; the process is DPI aware.
    internal static class InputSimulator
    {
        public static NativeMethods.POINT GetCursor()
        {
            NativeMethods.POINT p;
            NativeMethods.GetCursorPos(out p);
            return p;
        }

        // Instantly place the cursor at a physical-pixel coordinate.
        public static void MoveTo(int x, int y)
        {
            NativeMethods.SetCursorPos(x, y);
        }

        public static void MouseDown(MouseButton button)
        {
            SendButton(button, true);
        }

        public static void MouseUp(MouseButton button)
        {
            SendButton(button, false);
        }

        // Press then release with an optional hold time (ms) in between.
        public static void Click(MouseButton button, int holdMs)
        {
            SendButton(button, true);
            if (holdMs > 0)
                PrecisionSleep.Sleep(holdMs);
            SendButton(button, false);
        }

        // Positive notches scroll up / away from the user, negative scroll down.
        public static void ScrollWheel(int notches)
        {
            var input = NewMouseInput();
            input.u.mi.dwFlags = NativeMethods.MOUSEEVENTF_WHEEL;
            input.u.mi.mouseData = unchecked((uint)(notches * NativeMethods.WHEEL_DELTA));
            Send(input);
        }

        private static void SendButton(MouseButton button, bool down)
        {
            var input = NewMouseInput();
            switch (button)
            {
                case MouseButton.Left:
                    input.u.mi.dwFlags = down ? NativeMethods.MOUSEEVENTF_LEFTDOWN : NativeMethods.MOUSEEVENTF_LEFTUP;
                    break;
                case MouseButton.Right:
                    input.u.mi.dwFlags = down ? NativeMethods.MOUSEEVENTF_RIGHTDOWN : NativeMethods.MOUSEEVENTF_RIGHTUP;
                    break;
                case MouseButton.Middle:
                    input.u.mi.dwFlags = down ? NativeMethods.MOUSEEVENTF_MIDDLEDOWN : NativeMethods.MOUSEEVENTF_MIDDLEUP;
                    break;
                case MouseButton.X1:
                    input.u.mi.dwFlags = down ? NativeMethods.MOUSEEVENTF_XDOWN : NativeMethods.MOUSEEVENTF_XUP;
                    input.u.mi.mouseData = NativeMethods.XBUTTON1;
                    break;
                case MouseButton.X2:
                    input.u.mi.dwFlags = down ? NativeMethods.MOUSEEVENTF_XDOWN : NativeMethods.MOUSEEVENTF_XUP;
                    input.u.mi.mouseData = NativeMethods.XBUTTON2;
                    break;
            }
            Send(input);
        }

        private static NativeMethods.INPUT NewMouseInput()
        {
            var input = new NativeMethods.INPUT();
            input.type = NativeMethods.INPUT_MOUSE;
            input.u.mi.dx = 0;
            input.u.mi.dy = 0;
            input.u.mi.mouseData = 0;
            input.u.mi.dwFlags = 0;
            input.u.mi.time = 0;
            input.u.mi.dwExtraInfo = IntPtr.Zero;
            return input;
        }

        private static void Send(NativeMethods.INPUT input)
        {
            var arr = new NativeMethods.INPUT[1];
            arr[0] = input;
            NativeMethods.SendInput(1, arr, System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.INPUT)));
        }
    }
}
