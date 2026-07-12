using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClickForge
{
    // Central dark palette + helpers so every control gets a consistent look
    // without a designer file. WinForms can't theme every pixel of native
    // combo/updown chrome, but flat styles + these colors get very close.
    internal static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(18, 20, 27);
        public static readonly Color Panel = Color.FromArgb(26, 29, 38);
        public static readonly Color PanelAlt = Color.FromArgb(33, 37, 48);
        public static readonly Color Border = Color.FromArgb(46, 51, 64);
        public static readonly Color Text = Color.FromArgb(232, 235, 242);
        public static readonly Color Muted = Color.FromArgb(150, 157, 173);
        public static readonly Color Accent = Color.FromArgb(99, 130, 255);
        public static readonly Color AccentDim = Color.FromArgb(70, 92, 190);
        public static readonly Color Good = Color.FromArgb(64, 196, 128);
        public static readonly Color Danger = Color.FromArgb(232, 84, 96);
        public static readonly Color NavActive = Color.FromArgb(38, 43, 58);

        public static readonly Font UiFont = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        public static readonly Font UiBold = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        public static readonly Font TitleFont = new Font("Segoe UI Semibold", 14f, FontStyle.Bold);
        public static readonly Font SmallFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);

        public static void StylePrimaryButton(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = Accent;
            b.ForeColor = Color.White;
            b.Font = UiBold;
            b.Cursor = Cursors.Hand;
            b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(Accent, 0.1f);
            b.FlatAppearance.MouseDownBackColor = AccentDim;
        }

        public static void StyleSecondaryButton(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Border;
            b.FlatAppearance.BorderSize = 1;
            b.BackColor = PanelAlt;
            b.ForeColor = Text;
            b.Font = UiFont;
            b.Cursor = Cursors.Hand;
            b.FlatAppearance.MouseOverBackColor = NavActive;
            b.FlatAppearance.MouseDownBackColor = Border;
        }

        public static void StyleInput(Control c)
        {
            c.BackColor = PanelAlt;
            c.ForeColor = Text;
            c.Font = UiFont;
            TextBox tb = c as TextBox;
            if (tb != null) tb.BorderStyle = BorderStyle.FixedSingle;
        }

        public static void StyleCombo(ComboBox cb)
        {
            cb.FlatStyle = FlatStyle.Flat;
            cb.BackColor = PanelAlt;
            cb.ForeColor = Text;
            cb.Font = UiFont;
            cb.DropDownStyle = ComboBoxStyle.DropDownList;
        }

        public static void StyleNumeric(NumericUpDown n)
        {
            n.BackColor = PanelAlt;
            n.ForeColor = Text;
            n.Font = UiFont;
            n.BorderStyle = BorderStyle.FixedSingle;
        }

        public static Label Label(string text, bool muted)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.ForeColor = muted ? Muted : Text;
            l.Font = muted ? SmallFont : UiFont;
            l.BackColor = Color.Transparent;
            return l;
        }

        public static Label SectionHeader(string text)
        {
            Label l = new Label();
            l.Text = text.ToUpperInvariant();
            l.AutoSize = true;
            l.ForeColor = Accent;
            l.Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold);
            l.BackColor = Color.Transparent;
            return l;
        }

        // Rounded panel painter used for cards.
        public static void PaintCard(Graphics g, Rectangle r, Color fill, int radius)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Rounded(r, radius))
            using (SolidBrush brush = new SolidBrush(fill))
                g.FillPath(brush, path);
        }

        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            if (d <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // Draws the app glyph (a stylized mouse) into a square bitmap.
        public static Bitmap CreateLogo(int size)
        {
            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                float pad = size * 0.16f;
                RectangleF body = new RectangleF(pad + size * 0.06f, pad, size - 2 * pad - size * 0.12f, size - 2 * pad);

                using (GraphicsPath p = new GraphicsPath())
                {
                    float d = body.Width;
                    p.AddArc(body.X, body.Y, body.Width, body.Width, 180, 180);
                    p.AddArc(body.X, body.Bottom - body.Width, body.Width, body.Width, 0, 180);
                    p.CloseFigure();
                    using (LinearGradientBrush br = new LinearGradientBrush(
                        Rectangle.Round(body), Accent, Color.FromArgb(150, 110, 255), 60f))
                        g.FillPath(br, p);
                }

                // Scroll wheel.
                float ww = body.Width * 0.16f;
                float wx = body.X + body.Width / 2 - ww / 2;
                float wy = body.Y + body.Height * 0.16f;
                using (SolidBrush wb = new SolidBrush(Color.White))
                    g.FillRectangle(wb, wx, wy, ww, body.Height * 0.22f);

                // Split line between buttons.
                using (Pen pen = new Pen(Color.FromArgb(120, 255, 255, 255), Math.Max(1f, size * 0.015f)))
                    g.DrawLine(pen, body.X + body.Width / 2, body.Y, body.X + body.Width / 2, wy);
            }
            return bmp;
        }
    }
}
