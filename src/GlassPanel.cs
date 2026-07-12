using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClickForge
{
    // A frosted-glass panel: it blits the shared Scene frame from directly
    // behind itself, lays a translucent veil on top, and optionally draws a
    // rounded border and a caller-supplied overlay (used for the nav pill,
    // footer ripples, etc.). This is what makes the animation appear to flow
    // continuously behind the whole UI.
    internal class GlassPanel : Panel
    {
        private readonly Scene _scene;
        private readonly Form _root;

        public int VeilAlpha { get; set; }
        public Color VeilColor { get; set; }
        public int CornerRadius { get; set; }
        public bool TopAccent { get; set; }
        public bool BorderVisible { get; set; }

        // Extra painting in panel-client coordinates, drawn above the veil.
        public Action<Graphics, Rectangle> Overlay;

        public GlassPanel(Scene scene, Form root)
        {
            _scene = scene;
            _root = root;
            VeilAlpha = 150;
            VeilColor = Color.FromArgb(18, 20, 28);
            CornerRadius = 0;
            TopAccent = false;
            BorderVisible = false;
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint
                   | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(16, 18, 25);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle client = ClientRectangle;

            // 1. The animated scene, sampled from this panel's spot on the form.
            Bitmap frame = _scene != null ? _scene.Frame : null;
            if (frame != null)
            {
                Rectangle src = MapToForm();
                if (src.Width > 0 && src.Height > 0)
                {
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.DrawImage(frame, client, src, GraphicsUnit.Pixel);
                }
                else
                {
                    g.Clear(VeilColor);
                }
            }
            else
            {
                g.Clear(VeilColor);
            }

            // 2. Translucent veil for contrast/readability.
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush veil = new SolidBrush(Color.FromArgb(VeilAlpha, VeilColor)))
            {
                if (CornerRadius > 0)
                {
                    using (GraphicsPath p = Theme.Rounded(new Rectangle(0, 0, client.Width - 1, client.Height - 1), CornerRadius))
                        g.FillPath(veil, p);
                }
                else
                {
                    g.FillRectangle(veil, client);
                }
            }

            // 3. Optional border + top accent line.
            if (BorderVisible)
            {
                using (Pen pen = new Pen(Color.FromArgb(70, 130, 150, 220)))
                {
                    if (CornerRadius > 0)
                    {
                        using (GraphicsPath p = Theme.Rounded(new Rectangle(0, 0, client.Width - 1, client.Height - 1), CornerRadius))
                            g.DrawPath(pen, p);
                    }
                    else
                    {
                        g.DrawRectangle(pen, 0, 0, client.Width - 1, client.Height - 1);
                    }
                }
            }

            if (TopAccent)
            {
                using (LinearGradientBrush lb = new LinearGradientBrush(
                    new Rectangle(0, 0, client.Width, 2),
                    Color.FromArgb(0, Theme.Accent), Color.FromArgb(0, Theme.Accent), 0f))
                {
                    ColorBlend cb = new ColorBlend();
                    cb.Colors = new Color[]
                    {
                        Color.FromArgb(0, Theme.Accent),
                        Color.FromArgb(200, Theme.Accent),
                        Color.FromArgb(200, 150, 110, 255),
                        Color.FromArgb(0, 150, 110, 255)
                    };
                    cb.Positions = new float[] { 0f, 0.35f, 0.7f, 1f };
                    lb.InterpolationColors = cb;
                    g.FillRectangle(lb, 0, 0, client.Width, 2);
                }
            }

            if (Overlay != null)
                Overlay(g, client);
        }

        // Where this panel sits on the form's client area — the source rect
        // into the shared scene frame.
        private Rectangle MapToForm()
        {
            try
            {
                Point topLeft = _root.PointToClient(PointToScreen(Point.Empty));
                return new Rectangle(topLeft.X, topLeft.Y, ClientRectangle.Width, ClientRectangle.Height);
            }
            catch
            {
                return ClientRectangle;
            }
        }
    }
}
