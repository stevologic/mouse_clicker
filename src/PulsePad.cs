using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClickForge
{
    // A small live "activity" indicator: a center dot that emits an expanding
    // ring on every Ping(), and a steady soft pulse while active. Self-drawn
    // and childless, so it animates without flicker on a solid parent.
    internal class PulsePad : Control
    {
        private readonly List<float> _rings = new List<float>();
        public bool Active { get; set; }

        public PulsePad()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint
                   | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public void Ping()
        {
            if (_rings.Count < 6)
                _rings.Add(0f);
        }

        public void Tick()
        {
            for (int i = _rings.Count - 1; i >= 0; i--)
            {
                _rings[i] += 0.05f;
                if (_rings[i] >= 1f) _rings.RemoveAt(i);
            }
            // Only repaint when there's something to animate.
            if (Active || _rings.Count > 0)
                Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = Width / 2f, cy = Height / 2f;
            float maxR = Math.Min(Width, Height) / 2f - 2f;

            Color accent = Active ? Theme.Good : Theme.Accent;

            // Expanding rings from pings.
            for (int i = 0; i < _rings.Count; i++)
            {
                float t = _rings[i];
                float r = maxR * (0.3f + 0.7f * t);
                int a = (int)((1f - t) * 160);
                if (a < 2) continue;
                using (Pen pen = new Pen(Color.FromArgb(a, accent), 1.8f * (1f - t) + 0.4f))
                    g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
            }

            // Steady breathing halo while active.
            if (Active)
            {
                float breathe = 0.5f + 0.5f * (float)Math.Sin(Environment.TickCount / 300.0);
                float r = maxR * (0.4f + 0.25f * breathe);
                using (GraphicsPath gp = new GraphicsPath())
                {
                    gp.AddEllipse(cx - r, cy - r, r * 2, r * 2);
                    using (PathGradientBrush pgb = new PathGradientBrush(gp))
                    {
                        pgb.CenterColor = Color.FromArgb((int)(90 * breathe), accent);
                        pgb.SurroundColors = new Color[] { Color.FromArgb(0, accent) };
                        g.FillPath(pgb, gp);
                    }
                }
            }

            // Center dot.
            float dot = 3.5f;
            using (SolidBrush b = new SolidBrush(accent))
                g.FillEllipse(b, cx - dot, cy - dot, dot * 2, dot * 2);
        }
    }
}
