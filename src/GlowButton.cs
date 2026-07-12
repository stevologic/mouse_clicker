using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClickForge
{
    // A custom-drawn pill button with a gradient fill, an animated hover lift,
    // and an optional pulsing glow (used for the Start/Stop control while a
    // run is active).
    internal class GlowButton : Button
    {
        public Color ColorA { get; set; }
        public Color ColorB { get; set; }
        public bool Pulsing { get; set; }
        public int Radius { get; set; }

        private float _hover;   // 0..1 eased hover amount
        private bool _down;

        public GlowButton()
        {
            ColorA = Color.FromArgb(99, 130, 255);
            ColorB = Color.FromArgb(150, 110, 255);
            Radius = 12;
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint
                   | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            ForeColor = Color.White;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold);
        }

        // Advances the eased hover value; called by the animation tick.
        public void Tick()
        {
            float target = (Enabled && ClientRectangle.Contains(PointToClient(Cursor.Position))) ? 1f : 0f;
            _hover += (target - _hover) * 0.2f;
            if (Pulsing || _hover > 0.01f)
                Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle full = ClientRectangle;

            int inset = 8; // room for the glow
            Rectangle r = new Rectangle(inset, inset, full.Width - inset * 2, full.Height - inset * 2);
            if (r.Width <= 0 || r.Height <= 0) return;

            float pulse = Pulsing ? (0.55f + 0.45f * (float)Math.Sin(Environment.TickCount / 260.0)) : 0f;
            float glow = Math.Max(pulse, _hover * 0.6f);

            // Soft glow: a few expanding translucent rounded rects.
            if (glow > 0.01f)
            {
                Color gc = Blend(ColorA, ColorB, 0.5f);
                for (int i = 3; i >= 1; i--)
                {
                    int grow = i * 4;
                    Rectangle gr = Rectangle.Inflate(r, grow, grow);
                    int a = (int)(glow * 34 / i);
                    using (GraphicsPath gp = Theme.Rounded(gr, Radius + grow))
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(a, gc)))
                        g.FillPath(b, gp);
                }
            }

            // Lift on hover.
            int lift = (int)(_hover * 2f);
            r.Y -= lift;

            Color a1 = ColorA, b1 = ColorB;
            if (_down) { a1 = Darken(a1, 0.12f); b1 = Darken(b1, 0.12f); }
            else if (_hover > 0.01f) { a1 = Lighten(a1, _hover * 0.10f); b1 = Lighten(b1, _hover * 0.10f); }

            using (GraphicsPath path = Theme.Rounded(r, Radius))
            using (LinearGradientBrush lb = new LinearGradientBrush(r, a1, b1, 130f))
            {
                g.FillPath(lb, path);
                // Top sheen.
                using (LinearGradientBrush sheen = new LinearGradientBrush(
                    new Rectangle(r.X, r.Y, r.Width, r.Height / 2),
                    Color.FromArgb(46, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                {
                    Region old = g.Clip;
                    g.SetClip(path);
                    g.FillRectangle(sheen, r.X, r.Y, r.Width, r.Height / 2);
                    g.Clip = old;
                }
            }

            TextRenderer.DrawText(g, Text, Font, r, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }
        private static Color Lighten(Color c, float t) { return Blend(c, Color.White, t); }
        private static Color Darken(Color c, float t) { return Blend(c, Color.Black, t); }
    }
}
