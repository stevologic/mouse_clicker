using System.Drawing;
using System.Windows.Forms;

namespace ClickForge
{
    // A DropDownList combo that honors the dark theme. WinForms won't repaint
    // the closed field or drop button in a custom color on its own, so we
    // owner-draw the items and overpaint the button + border after WM_PAINT.
    internal class DarkComboBox : ComboBox
    {
        private const int WM_PAINT = 0x000F;
        private const int ArrowWidth = 20;

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

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            Rectangle r = e.Bounds;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color bg = selected ? Theme.Accent : Theme.PanelAlt;
            using (SolidBrush b = new SolidBrush(bg))
                e.Graphics.FillRectangle(b, r);

            if (e.Index >= 0 && e.Index < Items.Count)
            {
                string text = GetItemText(Items[e.Index]);
                Color fg = selected ? Color.White : Theme.Text;
                TextRenderer.DrawText(e.Graphics, text, Font,
                    new Rectangle(r.X + 4, r.Y, r.Width - 8, r.Height), fg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_PAINT)
                OverpaintButton();
        }

        private void OverpaintButton()
        {
            using (Graphics g = CreateGraphics())
            {
                Rectangle btn = new Rectangle(Width - ArrowWidth, 0, ArrowWidth, Height);
                using (SolidBrush b = new SolidBrush(Theme.PanelAlt))
                    g.FillRectangle(b, btn);

                int cx = Width - ArrowWidth / 2 - 2;
                int cy = Height / 2;
                Point[] tri =
                {
                    new Point(cx - 4, cy - 2),
                    new Point(cx + 4, cy - 2),
                    new Point(cx, cy + 3)
                };
                using (SolidBrush b = new SolidBrush(Theme.Muted))
                    g.FillPolygon(b, tri);

                using (Pen p = new Pen(Theme.Border))
                    g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            }
        }
    }
}
