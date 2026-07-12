using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ClickForge
{
    // A floating, always-on-top "activity" popup shown while a click run is
    // active: a pulsing indicator plus a live counter. It's a per-pixel-alpha
    // layered window (soft shadow, rounded, translucent) and is click-through,
    // so it looks good and never intercepts the clicks it's reporting.
    internal class ClickHud : Form
    {
        private const int W = 248;
        private const int H = 104;

        private static readonly Font FLabel = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold);
        private static readonly Font FCount = new Font("Segoe UI", 20f, FontStyle.Bold);
        private static readonly Font FSuffix = new Font("Segoe UI", 10f, FontStyle.Regular);

        private readonly Timer _timer;
        private long _count;
        private float _fade;
        private bool _showing;
        private readonly List<float> _rings = new List<float>();
        private float _phase;
        private int _sinceRing = 999;

        public ClickHud()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(W, H);
            TopMost = true;

            _timer = new Timer();
            _timer.Interval = 33;
            _timer.Tick += delegate { Tick(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED
                            | NativeMethods.WS_EX_TRANSPARENT
                            | NativeMethods.WS_EX_TOOLWINDOW
                            | NativeMethods.WS_EX_NOACTIVATE
                            | NativeMethods.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        // Called (on the UI thread) when a run starts.
        public void Begin()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - W - 24, wa.Bottom - H - 28);
            _count = 0;
            _rings.Clear();
            _showing = true;
            if (!IsHandleCreated)
            {
                var force = Handle; // create the window handle
                GC.KeepAlive(force);
            }
            NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWNOACTIVATE);
            _timer.Start();
        }

        public void End()
        {
            _showing = false; // fade out, then Tick hides it
        }

        public void SetCount(long n)
        {
            _count = n;
        }

        public void Ping()
        {
            _sinceRing = 0;
            if (_rings.Count < 5) _rings.Add(0f);
        }

        private void Tick()
        {
            _phase += 0.05f;
            _sinceRing++;
            for (int i = _rings.Count - 1; i >= 0; i--)
            {
                _rings[i] += 0.05f;
                if (_rings[i] >= 1f) _rings.RemoveAt(i);
            }

            float target = _showing ? 1f : 0f;
            _fade += (target - _fade) * 0.22f;

            if (!_showing && _fade < 0.02f)
            {
                _timer.Stop();
                Hide();
                return;
            }

            Push(_fade);
        }

        // ---- Rendering ----------------------------------------------------

        // Full-opacity render of the HUD (used both for the live overlay and
        // for a standalone preview).
        public Bitmap RenderBitmap()
        {
            Bitmap bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(0, 0, 0, 0));

                int pad = 16;
                Rectangle card = new Rectangle(pad, pad, W - pad * 2, H - pad * 2);

                // Soft drop shadow.
                for (int i = 6; i >= 1; i--)
                {
                    Rectangle sr = Rectangle.Inflate(card, i * 2, i * 2);
                    sr.Offset(0, 3);
                    using (GraphicsPath sp = Theme.Rounded(sr, 20 + i * 2))
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(14 - i, 0, 0, 0)))
                        g.FillPath(sb, sp);
                }

                // Glass card.
                using (GraphicsPath cp = Theme.Rounded(card, 18))
                {
                    using (LinearGradientBrush cb = new LinearGradientBrush(card,
                        Color.FromArgb(244, 30, 34, 46), Color.FromArgb(244, 20, 22, 32), 90f))
                        g.FillPath(cb, cp);
                    using (Pen bp = new Pen(Color.FromArgb(150, 99, 130, 255), 1.2f))
                        g.DrawPath(bp, cp);
                }

                // Pulsing activity indicator.
                float icx = card.X + 34, icy = card.Y + card.Height / 2f;
                Color accent = Color.FromArgb(64, 200, 132);
                for (int i = 0; i < _rings.Count; i++)
                {
                    float t = _rings[i];
                    float rr = 8 + 20 * t;
                    int a = (int)((1f - t) * 150);
                    if (a < 2) continue;
                    using (Pen pen = new Pen(Color.FromArgb(a, accent), 2f * (1f - t) + 0.5f))
                        g.DrawEllipse(pen, icx - rr, icy - rr, rr * 2, rr * 2);
                }
                float breathe = 0.5f + 0.5f * (float)Math.Sin(_phase * 2.2);
                float hr = 12 + 4 * breathe;
                using (GraphicsPath gp = new GraphicsPath())
                {
                    gp.AddEllipse(icx - hr, icy - hr, hr * 2, hr * 2);
                    using (PathGradientBrush pgb = new PathGradientBrush(gp))
                    {
                        pgb.CenterColor = Color.FromArgb((int)(150 * breathe), accent);
                        pgb.SurroundColors = new Color[] { Color.FromArgb(0, accent) };
                        g.FillPath(pgb, gp);
                    }
                }
                using (SolidBrush db = new SolidBrush(accent))
                    g.FillEllipse(db, icx - 5, icy - 5, 10, 10);

                // Text.
                float tx = card.X + 62;
                string num = _count.ToString("#,0");
                TextRenderer.DrawText(g, "CLICKING", FLabel,
                    new Point((int)tx, card.Y + 12), Color.FromArgb(120, 220, 170),
                    TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, num, FCount,
                    new Point((int)tx - 2, card.Y + 26), Color.White, TextFormatFlags.NoPadding);
                Size numSize = TextRenderer.MeasureText(num, FCount);
                TextRenderer.DrawText(g, _count == 1 ? "click" : "clicks", FSuffix,
                    new Point((int)tx + numSize.Width - 2, card.Y + 40), Color.FromArgb(150, 157, 173),
                    TextFormatFlags.NoPadding);
            }
            return bmp;
        }

        private void Push(float fade)
        {
            if (!IsHandleCreated) return;
            byte alpha = (byte)Math.Max(0, Math.Min(255, (int)(255 * fade)));

            using (Bitmap bmp = RenderBitmap())
            {
                IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
                IntPtr memDc = NativeMethods.CreateCompatibleDC(screenDc);
                IntPtr hBmp = IntPtr.Zero;
                IntPtr old = IntPtr.Zero;
                try
                {
                    hBmp = bmp.GetHbitmap(Color.FromArgb(0));
                    old = NativeMethods.SelectObject(memDc, hBmp);

                    NativeMethods.SIZE size = new NativeMethods.SIZE(bmp.Width, bmp.Height);
                    NativeMethods.POINT src = new NativeMethods.POINT();
                    src.X = 0; src.Y = 0;
                    NativeMethods.POINT dst = new NativeMethods.POINT();
                    dst.X = Left; dst.Y = Top;

                    NativeMethods.BLENDFUNCTION blend = new NativeMethods.BLENDFUNCTION();
                    blend.BlendOp = NativeMethods.AC_SRC_OVER;
                    blend.BlendFlags = 0;
                    blend.SourceConstantAlpha = alpha;
                    blend.AlphaFormat = NativeMethods.AC_SRC_ALPHA;

                    NativeMethods.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size,
                        memDc, ref src, 0, ref blend, NativeMethods.ULW_ALPHA);
                }
                finally
                {
                    NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
                    if (old != IntPtr.Zero) NativeMethods.SelectObject(memDc, old);
                    if (hBmp != IntPtr.Zero) NativeMethods.DeleteObject(hBmp);
                    NativeMethods.DeleteDC(memDc);
                }
            }
        }
    }
}
