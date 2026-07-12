using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ClickForge
{
    // The app glyph (a stylized mouse) with a rotating gradient sheen and a
    // breathing glow. Drawn directly into a caller's Graphics so it can live
    // in a glass panel's overlay without a flickering child control.
    internal static class LogoArt
    {
        public static void Paint(Graphics g, RectangleF area)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float size = Math.Min(area.Width, area.Height);
            float ox = area.X, oy = area.Y;
            float t = Environment.TickCount / 1000f;

            float pad = size * 0.16f;
            RectangleF body = new RectangleF(
                ox + pad + size * 0.06f, oy + pad,
                size - 2 * pad - size * 0.12f, size - 2 * pad);

            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddArc(body.X, body.Y, body.Width, body.Width, 180, 180);
                p.AddArc(body.X, body.Bottom - body.Width, body.Width, body.Width, 0, 180);
                p.CloseFigure();
                float angle = (t * 40f) % 360f;
                using (LinearGradientBrush br = new LinearGradientBrush(
                    Rectangle.Round(body),
                    Color.FromArgb(99, 130, 255), Color.FromArgb(150, 110, 255), angle))
                    g.FillPath(br, p);
            }

            float ww = body.Width * 0.16f;
            using (SolidBrush wb = new SolidBrush(Color.White))
                g.FillRectangle(wb, body.X + body.Width / 2 - ww / 2,
                    body.Y + body.Height * 0.16f, ww, body.Height * 0.22f);
            using (Pen pen = new Pen(Color.FromArgb(120, 255, 255, 255), Math.Max(1f, size * 0.015f)))
                g.DrawLine(pen, body.X + body.Width / 2, body.Y,
                    body.X + body.Width / 2, body.Y + body.Height * 0.16f);
        }
    }
}
