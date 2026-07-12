using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClickForge
{
    // A thin gradient rule with a highlight that sweeps across it — a subtle
    // sign of life along the top edge of the content card.
    internal class AccentBar : Control
    {
        public AccentBar()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint, true);
            Height = 2;
            BackColor = Color.FromArgb(16, 18, 25);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int w = Width, h = Height;
            if (w <= 0) return;

            using (LinearGradientBrush baseBrush = new LinearGradientBrush(
                new Rectangle(0, 0, w, h),
                Color.FromArgb(60, Theme.Accent), Color.FromArgb(60, 150, 110, 255), 0f))
                g.FillRectangle(baseBrush, 0, 0, w, h);

            // Sweeping highlight.
            float t = (Environment.TickCount % 3600) / 3600f;
            int hw = Math.Max(80, w / 4);
            int hx = (int)(t * (w + hw)) - hw;
            using (LinearGradientBrush hl = new LinearGradientBrush(
                new Rectangle(hx, 0, hw, h),
                Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 0f))
            {
                ColorBlend cb = new ColorBlend();
                cb.Colors = new Color[]
                {
                    Color.FromArgb(0, 180, 200, 255),
                    Color.FromArgb(210, 200, 215, 255),
                    Color.FromArgb(0, 180, 200, 255)
                };
                cb.Positions = new float[] { 0f, 0.5f, 1f };
                hl.InterpolationColors = cb;
                g.FillRectangle(hl, hx, 0, hw, h);
            }
        }
    }
}
