using System;
using System.Drawing;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace ClickForge
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // Physical-pixel cursor coordinates on high-DPI displays.
            NativeMethods.EnableHighDpi();

            // Internal: validate the core input plumbing, write results to a file.
            //   MouseClicker.exe --selftest <outputFile>
            if (args != null && args.Length >= 2 && args[0] == "--selftest")
            {
                RunSelfTest(args[1]);
                return;
            }

            // Internal: show the live HUD (layered window) on screen and grab it.
            //   MouseClicker.exe --huddemo <outputFile>
            if (args != null && args.Length >= 2 && args[0] == "--huddemo")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                ClickHud h = new ClickHud();
                h.Begin();
                long count = 0;
                for (int i = 0; i < 90; i++)
                {
                    count += 137;
                    h.SetCount(count);
                    if (i % 6 == 0) h.Ping();
                    Application.DoEvents();
                    Thread.Sleep(16);
                }
                using (Bitmap bmp = new Bitmap(h.Width, h.Height))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                        g.CopyFromScreen(h.Location, Point.Empty, h.Size);
                    bmp.Save(args[1]);
                }
                h.End();
                return;
            }

            // Internal: preview the click HUD over a dark backdrop.
            //   MouseClicker.exe --hud <outputFile>
            if (args != null && args.Length >= 2 && args[0] == "--hud")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                ClickHud h = new ClickHud();
                h.SetCount(1234);
                h.Ping();
                using (Bitmap fg = h.RenderBitmap())
                using (Bitmap bg = new Bitmap(fg.Width, fg.Height))
                using (Graphics g = Graphics.FromImage(bg))
                {
                    g.Clear(Color.FromArgb(36, 40, 54));
                    g.DrawImage(fg, 0, 0);
                    bg.Save(args[1]);
                }
                return;
            }

            // Internal: capture real on-screen pixels of each page (includes
            // custom-painted controls DrawToBitmap misses).
            //   MouseClicker.exe --shot <outputDir>
            if (args != null && args.Length >= 2 && args[0] == "--shot")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                MainForm f = new MainForm();
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(80, 60);
                f.TopMost = true;
                f.Show();
                f.BringToFront();
                f.Activate();
                Application.DoEvents();
                f.CaptureAllPagesScreen(args[1]);
                f.Close();
                return;
            }

            // Internal: render each page to PNG for visual verification.
            //   MouseClicker.exe --render <outputDir>
            if (args != null && args.Length >= 2 && args[0] == "--render")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                MainForm f = new MainForm();
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-4000, -4000);
                f.Show();
                Application.DoEvents();
                f.CaptureAllPages(args[1]);
                f.Close();
                return;
            }

            // TLS 1.2 for the Anthropic HTTPS call on older default configs.
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Single instance: if the app is already up, just exit quietly.
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "ClickForge_SingleInstance_Mutex", out createdNew))
            {
                if (!createdNew)
                    return;

                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                {
                    MessageBox.Show("Unexpected error: " + e.Exception.Message,
                        "mouseclicker.app", MessageBoxButtons.OK, MessageBoxIcon.Error);
                };

                Application.Run(new MainForm());
            }
        }

        // Exercises SetCursorPos/GetCursorPos and SendInput (the exact structs
        // the engine relies on) and records the outcome. Restores the cursor.
        private static void RunSelfTest(string outFile)
        {
            var sb = new System.Text.StringBuilder();
            bool ok = true;
            try
            {
                NativeMethods.POINT start = InputSimulator.GetCursor();
                sb.AppendLine("start cursor: (" + start.X + ", " + start.Y + ")");

                var vs = ScreenInfo.Virtual();
                int tx = vs.Left + vs.Width / 2;
                int ty = vs.Top + vs.Height / 2;
                InputSimulator.MoveTo(tx, ty);
                NativeMethods.POINT after = InputSimulator.GetCursor();
                int dx = Math.Abs(after.X - tx);
                int dy = Math.Abs(after.Y - ty);
                bool moved = dx <= 2 && dy <= 2;
                sb.AppendLine("SetCursorPos target (" + tx + ", " + ty + ") -> (" + after.X + ", " + after.Y + ")  " + (moved ? "PASS" : "FAIL"));
                ok = ok && moved;

                // Validate SendInput struct marshaling: a no-op relative move
                // must be accepted (returns the number of events inserted).
                var mv = new NativeMethods.INPUT();
                mv.type = NativeMethods.INPUT_MOUSE;
                mv.u.mi.dwFlags = NativeMethods.MOUSEEVENTF_MOVE;
                var arr = new NativeMethods.INPUT[] { mv };
                uint sent = NativeMethods.SendInput(1, arr, System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.INPUT)));
                bool sendOk = sent == 1;
                sb.AppendLine("SendInput(MOVE) returned " + sent + "  " + (sendOk ? "PASS" : "FAIL"));
                ok = ok && sendOk;

                // Struct size sanity (mouse INPUT is 28 on x64, 24 on x86).
                int size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.INPUT));
                sb.AppendLine("sizeof(INPUT) = " + size + "  (" + (IntPtr.Size == 8 ? "x64" : "x86") + ")");

                InputSimulator.MoveTo(start.X, start.Y);
            }
            catch (Exception ex)
            {
                ok = false;
                sb.AppendLine("EXCEPTION: " + ex);
            }
            sb.AppendLine("RESULT: " + (ok ? "ALL PASS" : "FAILURE"));
            try { System.IO.File.WriteAllText(outFile, sb.ToString()); }
            catch { }
        }
    }
}
