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

            // Internal: validate the macro recorder hook + player.
            //   MouseClicker.exe --rectest <outputFile>
            if (args != null && args.Length >= 2 && args[0] == "--rectest")
            {
                RunRecTest(args[1]);
                return;
            }

            // Internal: unit-style audit of the app's logic (profiles, pattern
            // mapping, JSON tolerance, engine behavior, motion).
            //   MouseClicker.exe --audit <outputFile>
            if (args != null && args.Length >= 2 && args[0] == "--audit")
            {
                RunAudit(args[1]);
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

        // Exercises the macro recorder's low-level hook (install/uninstall) and
        // the player's replay of a recorded click sequence. Restores the cursor.
        private static void RunRecTest(string outFile)
        {
            var sb = new System.Text.StringBuilder();
            bool ok = true;
            try
            {
                // 1) The low-level mouse hook installs and uninstalls cleanly.
                var rec = new MacroRecorder();
                using (Form owner = new Form())
                {
                    owner.ShowInTaskbar = false;
                    rec.Start(owner);
                    bool hooked = rec.IsRecording;
                    sb.AppendLine("hook install: " + (hooked ? "PASS" : "FAIL"));
                    ok = ok && hooked;
                    rec.Stop();
                    sb.AppendLine("hook uninstall: " + (!rec.IsRecording ? "PASS" : "FAIL"));
                    ok = ok && !rec.IsRecording;
                }

                // 2) The player replays steps, landing the cursor on the last one.
                var vs = ScreenInfo.Virtual();
                int cx = vs.Left + vs.Width / 2;
                int cy = vs.Top + vs.Height / 2;
                var steps = new System.Collections.Generic.List<RecordedStep>();
                steps.Add(new RecordedStep { Kind = StepKind.Move, X = cx, Y = cy, DelayMs = 0 });
                steps.Add(new RecordedStep { Kind = StepKind.Click, X = cx - 120, Y = cy, Button = MouseButton.Left, DelayMs = 20 });
                steps.Add(new RecordedStep { Kind = StepKind.Click, X = cx + 120, Y = cy + 60, Button = MouseButton.Left, DelayMs = 40 });

                NativeMethods.POINT startPt = InputSimulator.GetCursor();
                var player = new MacroPlayer();
                bool[] done = { false };
                player.Finished += delegate(string reason) { done[0] = true; };
                player.Play(steps, 1);
                for (int i = 0; i < 150 && !done[0]; i++) Thread.Sleep(20);

                NativeMethods.POINT after = InputSimulator.GetCursor();
                bool landed = Math.Abs(after.X - (cx + 120)) <= 3 && Math.Abs(after.Y - (cy + 60)) <= 3;
                sb.AppendLine("player finished: " + (done[0] ? "PASS" : "FAIL"));
                sb.AppendLine("player cursor -> (" + after.X + ", " + after.Y + ") target ("
                    + (cx + 120) + ", " + (cy + 60) + ")  " + (landed ? "PASS" : "FAIL"));
                ok = ok && done[0] && landed;

                InputSimulator.MoveTo(startPt.X, startPt.Y);
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

        // Unit-style audit of the app's core logic. Each Check() line asserts
        // one behavior; any FAIL flips the overall result.
        private static void RunAudit(string outFile)
        {
            var sb = new System.Text.StringBuilder();
            bool[] allOk = { true };
            Action<string, bool> check = delegate(string what, bool pass)
            {
                sb.AppendLine((pass ? "PASS  " : "FAIL  ") + what);
                if (!pass) allOk[0] = false;
            };

            try
            {
                // ---- Profile.Normalize -------------------------------------
                var p = new Profile();
                p.Button = (MouseButton)99;
                p.Action = (ClickAction)99;
                p.RepeatMode = (RepeatMode)99;
                p.PositionMode = (PositionMode)99;
                p.MovementMode = (MovementMode)99;
                p.HoldMinMs = 500; p.HoldMaxMs = 100;       // inverted
                p.IntervalMinMs = 900; p.IntervalMaxMs = 100; // inverted
                p.ClicksPerEvent = 0;
                p.Normalize();
                check("Normalize clamps undefined Button", p.Button == MouseButton.Left);
                check("Normalize clamps undefined Action", p.Action == ClickAction.Single);
                check("Normalize clamps undefined RepeatMode", p.RepeatMode == RepeatMode.Infinite);
                check("Normalize clamps undefined PositionMode", p.PositionMode == PositionMode.CurrentCursor);
                check("Normalize clamps undefined MovementMode", p.MovementMode == MovementMode.Teleport);
                check("Normalize fixes inverted hold range", p.HoldMaxMs >= p.HoldMinMs);
                check("Normalize fixes inverted interval range", p.IntervalMaxMs >= p.IntervalMinMs);
                check("Normalize clamps ClicksPerEvent to >= 1", p.ClicksPerEvent >= 1);

                // ---- Profile JSON round-trip -------------------------------
                var json = new System.Web.Script.Serialization.JavaScriptSerializer();
                var src = new Profile();
                src.Button = MouseButton.Middle;
                src.Action = ClickAction.Double;
                src.IntervalMinMs = 123; src.IntervalMaxMs = 456;
                src.MinimizeToTray = false;
                src.ShowHud = false;
                src.Points.Add(new ClickPoint(11, 22));
                src.SetKey("Grok", "k-test");
                src.SetModel("Grok", "grok-4");
                Profile rt = json.Deserialize<Profile>(json.Serialize(src));
                rt.Normalize();
                check("Round-trip keeps Button", rt.Button == MouseButton.Middle);
                check("Round-trip keeps Action", rt.Action == ClickAction.Double);
                check("Round-trip keeps intervals", rt.IntervalMinMs == 123 && rt.IntervalMaxMs == 456);
                check("Round-trip keeps MinimizeToTray=false", rt.MinimizeToTray == false);
                check("Round-trip keeps ShowHud=false", rt.ShowHud == false);
                check("Round-trip keeps points", rt.Points.Count == 1 && rt.Points[0].X == 11 && rt.Points[0].Y == 22);
                check("Round-trip keeps provider key/model", rt.GetKey("Grok") == "k-test" && rt.GetModel("Grok") == "grok-4");

                // Legacy config (no new keys) keeps constructor defaults.
                Profile legacy = json.Deserialize<Profile>("{\"IntervalMinMs\":50}");
                check("Legacy config defaults MinimizeToTray=true", legacy.MinimizeToTray);
                check("Legacy config defaults ShowHud=true", legacy.ShowHud);

                // ---- ProfileStore round trip -------------------------------
                string odd = "au:di*t?te st";
                ProfileStore.SaveNamed(odd, src);
                Profile loaded = ProfileStore.LoadNamed(odd);
                check("Store round-trips odd names", loaded != null && loaded.IntervalMinMs == 123);
                ProfileStore.DeleteNamed(odd);
                bool gone = Array.IndexOf(ProfileStore.ListNames(), odd) < 0;
                check("Store deletes named profile", gone);

                // Reserved device names must not throw.
                bool conOk = true;
                try
                {
                    ProfileStore.SaveNamed("con", src);
                    Profile conP = ProfileStore.LoadNamed("con");
                    conOk = conP != null && conP.IntervalMinMs == 123;
                    ProfileStore.DeleteNamed("con");
                }
                catch { conOk = false; }
                check("Store survives reserved name 'con'", conOk);

                // ---- PatternMapper -----------------------------------------
                var pat = new System.Collections.Generic.Dictionary<string, object>();
                pat["button"] = "Right";
                pat["clickType"] = "Double";
                pat["intervalMinMs"] = 100; pat["intervalMaxMs"] = 200;
                pat["positionMode"] = "FixedPoint";
                pat["fixedX"] = 10; pat["fixedY"] = 20;
                pat["movementMode"] = "Humanized";
                pat["sequenceLoop"] = "no";
                pat["returnToOrigin"] = "yes";
                pat["points"] = new object[]
                {
                    MakeMap("x", 1, "y", 2),
                    MakeMap("x", 3, "y", 4)
                };
                var tp = new Profile();
                PatternMapper.ApplyToProfile(tp, pat);
                check("Mapper sets button/action", tp.Button == MouseButton.Right && tp.Action == ClickAction.Double);
                check("Mapper sets intervals", tp.IntervalMinMs == 100 && tp.IntervalMaxMs == 200);
                check("Mapper sets position + fixed point", tp.PositionMode == PositionMode.FixedPoint && tp.FixedX == 10 && tp.FixedY == 20);
                check("Mapper sets movement mode", tp.MovementMode == MovementMode.Humanized);
                check("Mapper parses bool words", tp.SequenceLoop == false && tp.ReturnToOrigin == true);
                check("Mapper parses points array", tp.Points.Count == 2 && tp.Points[1].X == 3 && tp.Points[1].Y == 4);

                var bad = new System.Collections.Generic.Dictionary<string, object>();
                bad["button"] = "7";        // numeric string -> undefined enum
                bad["clickType"] = "bogus"; // unknown name
                var bp = new Profile();
                MouseButton beforeB = bp.Button; ClickAction beforeA = bp.Action;
                PatternMapper.ApplyToProfile(bp, bad);
                check("Mapper rejects out-of-range numeric enum", bp.Button == beforeB);
                check("Mapper rejects unknown enum name", bp.Action == beforeA);
                check("Mapper GetInt parses double/string",
                    PatternMapper.GetInt(MakeMap("v", 12.6, "w", "34"), "v", 0) == 13
                    && PatternMapper.GetInt(MakeMap("v", 12.6, "w", "34"), "w", 0) == 34);

                // ---- AiClient JSON tolerance --------------------------------
                string wrapped = "Sure! Here you go:\n```json\n{\"a\":{\"b\":\"x}y\"},\"c\":2}\n``` hope that helps";
                string extracted = AiClient.ExtractJsonObject(wrapped);
                check("ExtractJsonObject handles nesting + braces in strings",
                    extracted == "{\"a\":{\"b\":\"x}y\"},\"c\":2}");

                string malformed = "{\"button\":\"Right\",\"intervalMaxMs\":3500\",\"durationSeconds\":60\",}";
                var ai = new AiClient();
                var repaired = ai.ParseLoose(malformed);
                check("ParseLoose repairs stray quotes + trailing comma",
                    repaired != null
                    && PatternMapper.GetInt(repaired, "intervalMaxMs", -1) == 3500
                    && PatternMapper.GetInt(repaired, "durationSeconds", -1) == 60);
                check("ParseLoose returns null for garbage", ai.ParseLoose("not json at all") == null);

                // ---- SuggestPresetName -------------------------------------
                string sugg = MainForm.SuggestPresetName("Click like a human every 1-3 seconds near the center");
                check("SuggestPresetName takes first words <= 28 chars",
                    sugg == "Click like a human" && sugg.Length <= 28);
                check("SuggestPresetName falls back on empty", MainForm.SuggestPresetName("  ") == "AI pattern");

                // ---- MacroRecorder.Capture (movement + clicks) -------------
                int WM_MOVE = NativeMethods.WM_MOUSEMOVE;
                int WM_L = NativeMethods.WM_LBUTTONDOWN;
                int WM_R = NativeMethods.WM_RBUTTONDOWN;

                var rec = new MacroRecorder();
                check("Capture ignores injected events",
                    rec.Capture(WM_L, 100, 100, true, false, 1000) == null && rec.Count == 0);
                check("Capture ignores events over the app window",
                    rec.Capture(WM_L, 100, 100, false, true, 1000) == null && rec.Count == 0);
                RecordedStep c1 = rec.Capture(WM_L, 300, 400, false, false, 1000);
                check("Capture records a left click (first delay = 0)",
                    c1 != null && c1.Kind == StepKind.Click && c1.Button == MouseButton.Left
                    && c1.X == 300 && c1.Y == 400 && c1.DelayMs == 0);
                RecordedStep c2 = rec.Capture(WM_R, 310, 410, false, false, 1250);
                check("Capture records a right click with the timing delta",
                    c2 != null && c2.Button == MouseButton.Right && c2.DelayMs == 250);
                check("Capture tallies clicks", rec.ClickCount == 2 && rec.MoveCount == 0);

                var mv = new MacroRecorder();
                RecordedStep m1 = mv.Capture(WM_MOVE, 0, 0, false, false, 5000);
                RecordedStep m2 = mv.Capture(WM_MOVE, 40, 40, false, false, 5005); // +5ms
                RecordedStep m3 = mv.Capture(WM_MOVE, 1, 1, false, false, 5030);   // <3px
                RecordedStep m4 = mv.Capture(WM_MOVE, 40, 40, false, false, 5045);
                check("Capture records movement", m1 != null && m1.Kind == StepKind.Move);
                check("Capture throttles movement by interval", m2 == null);
                check("Capture throttles movement by distance", m3 == null);
                check("Capture records a distinct movement sample", m4 != null && m4.Kind == StepKind.Move);
                check("Capture tallies moves", mv.MoveCount == 2 && mv.ClickCount == 0);

                var capr = new MacroRecorder();
                capr.Capture(WM_MOVE, 0, 0, false, false, 0);
                RecordedStep big = capr.Capture(WM_L, 500, 500, false, false, 999999);
                check("Capture caps huge idle gaps at 10000ms", big != null && big.DelayMs == 10000);

                // ---- HumanMotion (moves the real cursor; restored below) ----
                NativeMethods.POINT before = InputSimulator.GetCursor();
                var vs = ScreenInfo.Virtual();
                int hx = vs.Left + vs.Width / 2, hy = vs.Top + vs.Height / 2;
                HumanMotion.MoveTo(hx, hy, MovementMode.Teleport, 0, new Random(1), null);
                NativeMethods.POINT afterTp = InputSimulator.GetCursor();
                check("Motion teleport lands on target", Math.Abs(afterTp.X - hx) <= 2 && Math.Abs(afterTp.Y - hy) <= 2);
                HumanMotion.MoveTo(hx + 200, hy + 100, MovementMode.Humanized, 80, new Random(2), null);
                NativeMethods.POINT afterHm = InputSimulator.GetCursor();
                check("Motion humanized glide lands on target",
                    Math.Abs(afterHm.X - (hx + 200)) <= 2 && Math.Abs(afterHm.Y - (hy + 100)) <= 2);

                // ---- ClickEngine behavior (scroll events at a corner) -------
                // Park the cursor at the bottom-right corner so the wheel
                // events land on the desktop edge and affect nothing.
                InputSimulator.MoveTo(vs.Left + vs.Width - 2, vs.Top + vs.Height - 2);

                // 1) Count mode finishes at exactly N with one Stopped event.
                var engine = new ClickEngine();
                var clicks = new System.Collections.Generic.List<long>();
                var stops = new System.Collections.Generic.List<string>();
                object gate = new object();
                engine.ClickPerformed += delegate(long n) { lock (gate) clicks.Add(n); };
                engine.Stopped += delegate(string r) { lock (gate) stops.Add(r); };

                var cp = new Profile();
                cp.Action = ClickAction.ScrollUp;
                cp.ClicksPerEvent = 1;
                cp.PositionMode = PositionMode.CurrentCursor;
                cp.JitterRadius = 0;
                cp.RepeatMode = RepeatMode.Count;
                cp.RepeatCount = 3;
                cp.IntervalMinMs = 10; cp.IntervalMaxMs = 10;
                cp.HoldMinMs = 0; cp.HoldMaxMs = 0;
                cp.StartDelayMs = 0;
                engine.Start(cp);
                for (int i = 0; i < 100 && engine.IsRunning; i++) Thread.Sleep(20);
                Thread.Sleep(100);
                int nClicks, nStops; string lastStop;
                lock (gate) { nClicks = clicks.Count; nStops = stops.Count; lastStop = nStops > 0 ? stops[nStops - 1] : ""; }
                check("Engine count mode performs exactly N events", nClicks == 3);
                check("Engine count mode raises exactly one Stopped", nStops == 1);
                check("Engine count mode reason is 'finished'", lastStop.StartsWith("Finished"));
                check("Engine not running after finish", !engine.IsRunning);

                // 2) Cancelling during the countdown raises ONE Stopped with
                //    the cancellation reason (regression: double-Finish bug).
                lock (gate) { clicks.Clear(); stops.Clear(); }
                var dp = new Profile();
                dp.Action = ClickAction.ScrollUp;
                dp.StartDelayMs = 1500;
                dp.RepeatMode = RepeatMode.Count;
                dp.RepeatCount = 1;
                engine.Start(dp);
                Thread.Sleep(150);
                engine.Stop();
                Thread.Sleep(300);
                lock (gate) { nClicks = clicks.Count; nStops = stops.Count; lastStop = nStops > 0 ? stops[nStops - 1] : ""; }
                check("Engine countdown cancel performs no events", nClicks == 0);
                check("Engine countdown cancel raises exactly one Stopped", nStops == 1);
                check("Engine countdown cancel reason is 'Cancelled before start.'", lastStop == "Cancelled before start.");

                // 3) Rapid Stop->Start: the superseded session must stay silent
                //    and only the new session clicks (regression: restart race).
                lock (gate) { clicks.Clear(); stops.Clear(); }
                var ip = new Profile();
                ip.Action = ClickAction.ScrollUp;
                ip.PositionMode = PositionMode.CurrentCursor;
                ip.JitterRadius = 0;
                ip.RepeatMode = RepeatMode.Infinite;
                ip.IntervalMinMs = 30; ip.IntervalMaxMs = 30;
                ip.HoldMinMs = 0; ip.HoldMaxMs = 0;
                ip.StartDelayMs = 0;
                engine.Start(ip);
                Thread.Sleep(120);
                engine.Stop();
                engine.Start(ip);      // immediately supersede the old session
                Thread.Sleep(250);
                bool runningAfterRestart = engine.IsRunning;
                engine.Stop();
                Thread.Sleep(300);
                lock (gate) { nStops = stops.Count; lastStop = nStops > 0 ? stops[nStops - 1] : ""; }
                int settled; lock (gate) { settled = clicks.Count; }
                Thread.Sleep(250);
                int after; lock (gate) { after = clicks.Count; }
                check("Engine restart: new session runs", runningAfterRestart);
                check("Engine restart: superseded session stays silent (1 Stopped)", nStops == 1);
                check("Engine restart: final reason is 'Stopped.'", lastStop == "Stopped.");
                check("Engine restart: no events after final stop", after == settled);
                check("Engine not running at end", !engine.IsRunning);

                // Restore the cursor where we found it.
                InputSimulator.MoveTo(before.X, before.Y);
            }
            catch (Exception ex)
            {
                allOk[0] = false;
                sb.AppendLine("EXCEPTION: " + ex);
            }

            sb.AppendLine("RESULT: " + (allOk[0] ? "ALL PASS" : "FAILURE"));
            try { System.IO.File.WriteAllText(outFile, sb.ToString()); }
            catch { }
        }

        private static System.Collections.Generic.Dictionary<string, object> MakeMap(
            string k1, object v1, string k2, object v2)
        {
            var d = new System.Collections.Generic.Dictionary<string, object>();
            d[k1] = v1; d[k2] = v2;
            return d;
        }
    }
}
