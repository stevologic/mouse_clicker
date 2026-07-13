using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClickForge
{
    // Small factory helpers for building the settings forms without a designer.
    internal static class Ui
    {
        public const int RowHeight = 34;
        public const int LabelWidth = 175;
        public const int ContentWidth = 560;

        public static NumericUpDown Num(int min, int max, int width)
        {
            NumericUpDown n = new NumericUpDown();
            n.Minimum = min;
            n.Maximum = max;
            n.Width = width;
            n.Height = 26;
            Theme.StyleNumeric(n);
            return n;
        }

        public static ComboBox Combo(int width, params string[] items)
        {
            DarkComboBox cb = new DarkComboBox();
            cb.Width = width;
            cb.Height = 26;
            cb.Items.AddRange(items);
            if (items.Length > 0) cb.SelectedIndex = 0;
            return cb;
        }

        public static CheckBox Check(string text)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.AutoSize = true;
            c.ForeColor = Theme.Text;
            c.Font = Theme.UiFont;
            c.BackColor = Color.Transparent;
            c.Cursor = Cursors.Hand;
            return c;
        }

        public static TextBox Text(int width, bool password)
        {
            TextBox t = new TextBox();
            t.Width = width;
            Theme.StyleInput(t);
            if (password) t.UseSystemPasswordChar = true;
            return t;
        }

        // A labeled row: caption on the left, one control on the right.
        // Sizes itself to controls taller than a normal row (multi-line text,
        // auto-size panels) so nothing overflows or gets clipped.
        public static Panel Row(string caption, Control control)
        {
            // Auto-size controls report their real size via PreferredSize; the
            // plain Height is still zero before the first layout pass.
            int ch = control.Height;
            if (control.AutoSize)
            {
                int ph = control.PreferredSize.Height;
                if (ph > 0) ch = ph;
            }
            bool tall = ch > RowHeight - 6;
            int rowH = tall ? ch + 8 : RowHeight;

            Panel row = new Panel();
            row.Width = ContentWidth;
            row.Height = rowH;
            row.BackColor = Color.Transparent;

            Label lbl = new Label();
            lbl.Text = caption;
            lbl.ForeColor = Theme.Text;
            lbl.Font = Theme.UiFont;
            lbl.BackColor = Color.Transparent;
            lbl.AutoSize = false;
            lbl.Width = LabelWidth;
            lbl.Height = tall ? Math.Min(rowH, 26) : rowH;
            lbl.TextAlign = tall ? ContentAlignment.TopLeft : ContentAlignment.MiddleLeft;
            lbl.Location = new Point(0, tall ? 4 : 0);
            row.Controls.Add(lbl);

            control.Location = new Point(LabelWidth, tall ? 2 : (rowH - control.Height) / 2);
            row.Controls.Add(control);
            return row;
        }

        // A row whose right side holds several controls laid out left-to-right.
        public static Panel RowMulti(string caption, params Control[] controls)
        {
            Panel host = new Panel();
            host.Height = 26;
            int x = 0;
            foreach (Control c in controls)
            {
                c.Location = new Point(x, (26 - c.Height) / 2);
                host.Controls.Add(c);
                x += c.Width + 8;
            }
            host.Width = x;
            return Row(caption, host);
        }

        public static Label Dash()
        {
            Label l = new Label();
            l.Text = "–";
            l.AutoSize = false;
            l.Width = 14;
            l.Height = 26;
            l.ForeColor = Theme.Muted;
            l.TextAlign = ContentAlignment.MiddleCenter;
            l.BackColor = Color.Transparent;
            return l;
        }

        public static Label Suffix(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.Width = 34;
            l.Height = 26;
            l.ForeColor = Theme.Muted;
            l.Font = Theme.SmallFont;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.BackColor = Color.Transparent;
            return l;
        }

        public static Button SmallButton(string text, int width)
        {
            Button b = new Button();
            b.Text = text;
            b.Width = width;
            b.Height = 26;
            Theme.StyleSecondaryButton(b);
            return b;
        }

        // Vertical scrolling container that stacks its children top-down.
        public static FlowLayoutPanel Stack()
        {
            FlowLayoutPanel f = new FlowLayoutPanel();
            f.FlowDirection = FlowDirection.TopDown;
            f.WrapContents = false;
            f.AutoScroll = true;
            f.Dock = DockStyle.Fill;
            f.BackColor = Color.Transparent;
            f.Padding = new Padding(28, 14, 20, 14);
            return f;
        }

        public static Control Spacer(int height)
        {
            Panel p = new Panel();
            p.Width = ContentWidth;
            p.Height = height;
            p.BackColor = Color.Transparent;
            return p;
        }
    }
}
