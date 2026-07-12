using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClickForge
{
    // A DropDownList combo themed for the dark UI. WinForms paints the closed
    // field white and won't honor a custom color, so we fully repaint it on
    // both WM_PAINT (live) and WM_PRINTCLIENT (DrawToBitmap) — the latter keeps
    // it correct in captured screenshots too.
    internal class DarkComboBox : ComboBox
    {
        private const int WM_PAINT = 0x000F;
        private const int WM_PRINT = 0x0317;
        private const int WM_PRINTCLIENT = 0x0318;
        private const int ArrowWidth = 22;

        public DarkComboBox()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            DrawMode = DrawMode.OwnerDrawFixed;
            BackColor = Theme.PanelAlt;
            ForeColor = Theme.Text;
            Font = Theme.UiFont;
            ItemHeight = 20;
        }

        // Dropdown list items.
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            Rectangle r = e.Bounds;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush b = new SolidBrush(selected ? Theme.Accent : Theme.PanelAlt))
                e.Graphics.FillRectangle(b, r);
            if (e.Index >= 0 && e.Index < Items.Count)
            {
                string text = GetItemText(Items[e.Index]);
                TextRenderer.DrawText(e.Graphics, text, Font,
                    new Rectangle(r.X + 4, r.Y, r.Width - 8, r.Height),
                    selected ? Color.White : Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_PAINT)
            {
                base.WndProc(ref m);
                using (Graphics g = Graphics.FromHwnd(Handle))
                    DrawClosed(g);
                return;
            }
            if (m.Msg == WM_PRINT || m.Msg == WM_PRINTCLIENT)
            {
                base.WndProc(ref m);
                if (m.WParam != IntPtr.Zero)
                {
                    using (Graphics g = Graphics.FromHdc(m.WParam))
                        DrawClosed(g);
                }
                return;
            }
            base.WndProc(ref m);
        }

        // Repaints the entire closed field: dark fill, current value, arrow, border.
        private void DrawClosed(Graphics g)
        {
            Rectangle r = ClientRectangle;
            using (SolidBrush b = new SolidBrush(Theme.PanelAlt))
                g.FillRectangle(b, r);

            string text = SelectedIndex >= 0 ? GetItemText(SelectedItem) : "";
            TextRenderer.DrawText(g, text, Font,
                new Rectangle(r.X + 5, r.Y, r.Width - ArrowWidth - 8, r.Height),
                Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            int cx = r.Right - ArrowWidth / 2 - 4;
            int cy = r.Height / 2;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Point[] tri =
            {
                new Point(cx - 4, cy - 2),
                new Point(cx + 4, cy - 2),
                new Point(cx, cy + 3)
            };
            using (SolidBrush b = new SolidBrush(Theme.Muted))
                g.FillPolygon(b, tri);

            using (Pen p = new Pen(Theme.Border))
                g.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
        }
    }
}
