using System.Windows.Forms;

namespace ClickForge
{
    internal struct ScreenRect
    {
        public int Left;
        public int Top;
        public int Width;
        public int Height;
    }

    // Physical-pixel display geometry. The process is DPI aware, so these
    // match the coordinates SetCursorPos / GetCursorPos operate in.
    internal static class ScreenInfo
    {
        public static ScreenRect Virtual()
        {
            ScreenRect r;
            r.Left = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            r.Top = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            r.Width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            r.Height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
            if (r.Width <= 0) r.Width = 1920;
            if (r.Height <= 0) r.Height = 1080;
            return r;
        }

        public static ScreenRect Primary()
        {
            ScreenRect r;
            var b = Screen.PrimaryScreen.Bounds;
            r.Left = b.Left;
            r.Top = b.Top;
            r.Width = b.Width;
            r.Height = b.Height;
            return r;
        }
    }
}
