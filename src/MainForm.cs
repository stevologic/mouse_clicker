using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace ClickForge
{
    public class MainForm : Form
    {
        private const string AppName = "ClickForge";
        private const string AppVersion = "2.2";

        private Profile _profile;
        private readonly ClickEngine _engine = new ClickEngine();
        private readonly AiClient _ai = new AiClient();
        private HotkeyManager _hotkeys;
        private ClickHud _hud;
        private long _clickCount;

        // Layout
        private Panel _content;
        private readonly Dictionary<string, Control> _pages = new Dictionary<string, Control>();
        private GlowButton _startButton;
        private Label _statusLabel;
        private Label _countLabel;
        private PulsePad _pulse;

        // Animation
        private readonly Scene _scene = new Scene();
        private Timer _anim;
        private GlassPanel _headerGlass;
        private GlassPanel _navGlass;
        private AccentBar _accent;

        // Nav items are drawn in the nav glass overlay (no child controls, so
        // the animated glass shows through with no flicker).
        private static readonly string[] NavNames = { "Click", "Timing", "Movement", "AI", "Profiles", "About" };
        private static readonly string[] NavIcons = { "⟳", "⏱", "↗", "✦", "☰", "ⓘ" };
        private Rectangle[] _navRects;
        private int _navActive;
        private int _navHover = -1;
        private float _navPillY;
        private float _navPillTarget;
        private int _lastRippleTick;

        // Click page
        private ComboBox _buttonCombo;
        private ComboBox _actionCombo;
        private NumericUpDown _clicksPerEvent;
        private NumericUpDown _holdMin, _holdMax;

        // Timing page
        private NumericUpDown _intervalMin, _intervalMax;
        private Label _cpsLabel;
        private ComboBox _repeatCombo;
        private FlowLayoutPanel _countGroup;
        private FlowLayoutPanel _durationGroup;
        private NumericUpDown _repeatCount;
        private NumericUpDown _durationSecs;
        private NumericUpDown _startDelay;

        // Move page
        private ComboBox _positionCombo;
        private FlowLayoutPanel _fixedGroup;
        private FlowLayoutPanel _regionGroup;
        private FlowLayoutPanel _pointsGroup;
        private NumericUpDown _fixedX, _fixedY;
        private NumericUpDown _regL, _regT, _regR, _regB;
        private ListBox _pointsList;
        private CheckBox _sequenceLoop;
        private ComboBox _movementCombo;
        private NumericUpDown _movementMs;
        private NumericUpDown _jitter;
        private CheckBox _returnToOrigin;

        // AI page
        private ComboBox _providerCombo;
        private TextBox _apiKey;
        private ComboBox _modelCombo;
        private Label _keyNote;
        private string _uiProvider;
        private TextBox _aiPrompt;
        private Button _generateButton;
        private Label _aiResult;

        // Profiles page
        private ListBox _profilesList;
        private TextBox _profileName;
        private ComboBox _toggleKeyCombo;
        private ComboBox _stopKeyCombo;

        // Point capture
        private Timer _captureTimer;
        private int _captureCountdown;
        private Action<int, int> _captureCallback;
        private bool _loadingUi;

        // (label, virtual-key) pairs for the hotkey pickers.
        private static readonly KeyValuePair<string, int>[] HotkeyChoices =
        {
            Kv("F1", 0x70), Kv("F2", 0x71), Kv("F3", 0x72), Kv("F4", 0x73),
            Kv("F5", 0x74), Kv("F6", 0x75), Kv("F7", 0x76), Kv("F8", 0x77),
            Kv("F9", 0x78), Kv("F10", 0x79), Kv("F11", 0x7A), Kv("F12", 0x7B),
            Kv("Pause", 0x13), Kv("ScrollLock", 0x91), Kv("Insert", 0x2D), Kv("Home", 0x24)
        };

        private static KeyValuePair<string, int> Kv(string k, int v)
        {
            return new KeyValuePair<string, int>(k, v);
        }

        public MainForm()
        {
            _profile = ProfileStore.LoadConfig();

            Text = AppName + " — Robust Auto Clicker";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(900, 640);
            MinimumSize = new Size(840, 580);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.UiFont;
            AutoScaleMode = AutoScaleMode.Font;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            try { Icon = System.Drawing.Icon.FromHandle(Theme.CreateLogo(32).GetHicon()); }
            catch { }

            _scene.Resize(ClientSize.Width, ClientSize.Height);
            _hud = new ClickHud();

            BuildHeader();
            BuildFooter();
            BuildNavAndContent();
            UpdateSceneRegions();

            WireEngine();

            LoadToControls();
            SwitchPage("Click");

            _anim = new Timer();
            _anim.Interval = 42; // ~24fps — smooth for an ambient backdrop, lighter on the CPU
            _anim.Tick += AnimTick;
            _anim.Start();

            Activated += delegate { _active = true; };
            Deactivate += delegate { _active = false; };

            Load += delegate { SetupHotkeys(); };
            Resize += delegate { OnFormResized(); };
            FormClosing += OnClosing;
        }

        private bool _active = true;
        private int _animFrame;

        // ---- Animation loop ----------------------------------------------

        private void OnFormResized()
        {
            if (ClientSize.Width < 2 || ClientSize.Height < 2) return;
            _scene.Resize(ClientSize.Width, ClientSize.Height);
            UpdateSceneRegions();
        }

        private void UpdateSceneRegions()
        {
            if (_navGlass == null || _headerGlass == null) return;
            int navW = _navGlass.Width;
            int headH = _headerGlass.Height;
            _scene.SetVisibleRects(new Rectangle[]
            {
                new Rectangle(0, 0, navW, ClientSize.Height),
                new Rectangle(navW, 0, ClientSize.Width - navW, headH)
            });
        }

        private void AnimTick(object sender, EventArgs e)
        {
            // Idle to near-zero CPU when the window isn't in the foreground —
            // important for an auto-clicker that runs while you work elsewhere.
            if (WindowState == FormWindowState.Minimized || !_active)
                return;

            // Feed the live cursor position (anywhere over the window) to the
            // constellation so particles react to it.
            Point c = PointToClient(Cursor.Position);
            bool inside = ClientRectangle.Contains(c);
            _scene.SetCursor(c.X, c.Y, inside);

            _scene.Update();
            _scene.Render();

            // Ease the nav highlight pill toward the active item (near-instant).
            _navPillY += (_navPillTarget - _navPillY) * 0.55f;

            // The nav is the main animated surface — repaint it every frame.
            // The header/accent are secondary; repaint them less often to save
            // the cost of recompositing their text/gradients each tick.
            _animFrame++;
            if (_navGlass != null) _navGlass.Invalidate();
            if ((_animFrame & 3) == 0)
            {
                if (_headerGlass != null) _headerGlass.Invalidate();
                if (_accent != null) _accent.Invalidate();
            }
            if (_startButton != null) _startButton.Tick();
            if (_pulse != null) _pulse.Tick();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Paint the scene into any exposed form background (gutters).
            Bitmap f = _scene.Frame;
            if (f != null)
                e.Graphics.DrawImage(f, 0, 0);
            else
                base.OnPaintBackground(e);
        }

        // ---- Header ------------------------------------------------------

        private void BuildHeader()
        {
            _headerGlass = new GlassPanel(_scene, this);
            _headerGlass.Dock = DockStyle.Top;
            _headerGlass.Height = 96;
            _headerGlass.VeilAlpha = 96;
            _headerGlass.VeilColor = Color.FromArgb(14, 16, 24);
            _headerGlass.Overlay = PaintHeaderOverlay;
            Controls.Add(_headerGlass);
        }

        private void PaintHeaderOverlay(Graphics g, Rectangle r)
        {
            LogoArt.Paint(g, new RectangleF(22, 24, 48, 48));

            TextRenderer.DrawText(g, AppName, Theme.TitleFont,
                new Point(84, 26), Theme.Text, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "Robust auto clicker  ·  AI-generated patterns", Theme.SmallFont,
                new Point(86, 56), Theme.Muted, TextFormatFlags.NoPadding);

            // Bottom hairline.
            using (Pen pen = new Pen(Color.FromArgb(60, 130, 150, 220)))
                g.DrawLine(pen, 0, r.Height - 1, r.Width, r.Height - 1);
        }

        // ---- Footer (persistent controls) --------------------------------

        private void BuildFooter()
        {
            Panel footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = 92;
            footer.BackColor = Theme.Panel;

            Panel rule = new Panel();
            rule.Dock = DockStyle.Top;
            rule.Height = 1;
            rule.BackColor = Theme.Border;
            footer.Controls.Add(rule);

            _startButton = new GlowButton();
            _startButton.Size = new Size(210, 64);
            _startButton.Location = new Point(18, 14);
            _startButton.Click += delegate { ToggleRun(); };
            footer.Controls.Add(_startButton);

            _pulse = new PulsePad();
            _pulse.Size = new Size(44, 44);
            _pulse.Location = new Point(244, 24);
            _pulse.BackColor = Theme.Panel;
            footer.Controls.Add(_pulse);

            _statusLabel = new Label();
            _statusLabel.Text = "Idle";
            _statusLabel.Font = Theme.UiBold;
            _statusLabel.ForeColor = Theme.Text;
            _statusLabel.AutoSize = true;
            _statusLabel.Location = new Point(296, 26);
            _statusLabel.BackColor = Theme.Panel;
            footer.Controls.Add(_statusLabel);

            _countLabel = new Label();
            _countLabel.Text = "0 clicks";
            _countLabel.Font = Theme.SmallFont;
            _countLabel.ForeColor = Theme.Muted;
            _countLabel.AutoSize = true;
            _countLabel.Location = new Point(296, 50);
            _countLabel.BackColor = Theme.Panel;
            footer.Controls.Add(_countLabel);

            Label hint = new Label();
            hint.Text = "F6 start / stop   ·   F8 stop";
            hint.Font = Theme.SmallFont;
            hint.ForeColor = Theme.Muted;
            hint.AutoSize = true;
            hint.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            hint.BackColor = Theme.Panel;
            footer.Controls.Add(hint);
            footer.Resize += delegate
            {
                hint.Location = new Point(footer.Width - hint.Width - 22, 38);
            };

            UpdateStartButton();
            Controls.Add(footer);
        }

        // ---- Nav + content -----------------------------------------------

        private void BuildNavAndContent()
        {
            _content = new Panel();
            _content.Dock = DockStyle.Fill;
            _content.BackColor = Theme.Bg;
            Controls.Add(_content);

            _navGlass = new GlassPanel(_scene, this);
            _navGlass.Dock = DockStyle.Left;
            _navGlass.Width = 172;
            _navGlass.VeilAlpha = 120;
            _navGlass.VeilColor = Color.FromArgb(15, 17, 25);
            _navGlass.Cursor = Cursors.Hand;
            _navGlass.Overlay = PaintNavOverlay;
            _navGlass.MouseMove += NavMouseMove;
            _navGlass.MouseLeave += delegate { _navHover = -1; };
            _navGlass.MouseClick += NavMouseClick;
            Controls.Add(_navGlass);

            // Precompute the item hit-rectangles.
            _navRects = new Rectangle[NavNames.Length];
            int y = 20, h = 44, gap = 6;
            for (int i = 0; i < NavNames.Length; i++)
            {
                _navRects[i] = new Rectangle(10, y, _navGlass.Width - 20, h);
                y += h + gap;
            }
            _navPillY = _navRects[0].Y;
            _navPillTarget = _navPillY;

            _accent = new AccentBar();
            _accent.Dock = DockStyle.Top;
            _accent.Height = 2;
            _content.Controls.Add(_accent);

            _pages["Click"] = BuildClickPage();
            _pages["Timing"] = BuildTimingPage();
            _pages["Movement"] = BuildMovePage();
            _pages["AI"] = BuildAiPage();
            _pages["Profiles"] = BuildProfilesPage();
            _pages["About"] = BuildAboutPage();

            foreach (KeyValuePair<string, Control> kv in _pages)
            {
                kv.Value.Visible = false;
                _content.Controls.Add(kv.Value);
            }

            // Dock layout processes children in reverse z-order, so the Fill
            // panel must sit at the front (index 0) to be sized LAST — after the
            // header/footer/nav reserve their edges. Otherwise it claims the
            // full height and hides its top rows behind the header.
            _content.BringToFront();
        }

        private void PaintNavOverlay(Graphics g, Rectangle r)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Sliding highlight pill behind the active item.
            Rectangle pill = new Rectangle(10, (int)_navPillY, _navGlass.Width - 20, 44);
            using (System.Drawing.Drawing2D.GraphicsPath gp = Theme.Rounded(pill, 11))
            {
                using (System.Drawing.Drawing2D.LinearGradientBrush lb =
                    new System.Drawing.Drawing2D.LinearGradientBrush(pill,
                        Color.FromArgb(150, 99, 130, 255), Color.FromArgb(150, 150, 110, 255), 0f))
                    g.FillPath(lb, gp);
                using (Pen pen = new Pen(Color.FromArgb(120, 170, 190, 255)))
                    g.DrawPath(pen, gp);
            }

            for (int i = 0; i < NavNames.Length; i++)
            {
                Rectangle ir = _navRects[i];
                bool active = i == _navActive;
                if (!active && i == _navHover)
                {
                    using (System.Drawing.Drawing2D.GraphicsPath gp = Theme.Rounded(ir, 11))
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(45, 150, 165, 230)))
                        g.FillPath(b, gp);
                }

                Color fg = active ? Color.White : (i == _navHover ? Theme.Text : Theme.Muted);
                Font f = active ? Theme.UiBold : Theme.UiFont;
                TextRenderer.DrawText(g, NavIcons[i], Theme.UiBold,
                    new Rectangle(ir.X + 14, ir.Y, 22, ir.Height), fg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, NavNames[i], f,
                    new Rectangle(ir.X + 42, ir.Y, ir.Width - 46, ir.Height), fg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
        }

        private int NavHitTest(Point p)
        {
            if (_navRects == null) return -1;
            for (int i = 0; i < _navRects.Length; i++)
                if (_navRects[i].Contains(p)) return i;
            return -1;
        }

        private void NavMouseMove(object sender, MouseEventArgs e)
        {
            int hit = NavHitTest(e.Location);
            if (hit != _navHover)
            {
                _navHover = hit;
                _navGlass.Invalidate();
            }
        }

        private void NavMouseClick(object sender, MouseEventArgs e)
        {
            int hit = NavHitTest(e.Location);
            if (hit >= 0)
                SwitchPage(NavNames[hit]);
        }

        private void SwitchPage(string name)
        {
            foreach (KeyValuePair<string, Control> kv in _pages)
                kv.Value.Visible = (kv.Key == name);
            Control active;
            if (_pages.TryGetValue(name, out active))
                active.BringToFront();

            int idx = Array.IndexOf(NavNames, name);
            if (idx >= 0)
            {
                _navActive = idx;
                _navPillTarget = _navRects[idx].Y;
            }
        }

        // ---- Pages -------------------------------------------------------

        private Control BuildClickPage()
        {
            FlowLayoutPanel s = Ui.Stack();
            s.Controls.Add(Theme.SectionHeader("Mouse action"));
            s.Controls.Add(Ui.Spacer(6));

            _buttonCombo = Ui.Combo(160, "Left", "Right", "Middle", "X1 (back)", "X2 (forward)");
            s.Controls.Add(Ui.Row("Button", _buttonCombo));

            _actionCombo = Ui.Combo(190, "Single click", "Double click", "Triple click",
                "Multi-click (N)", "Scroll up", "Scroll down", "Press & hold", "Release");
            _actionCombo.SelectedIndexChanged += delegate { UpdateActionLabels(); };
            s.Controls.Add(Ui.Row("Action", _actionCombo));

            _clicksPerEvent = Ui.Num(1, 10000, 90);
            s.Controls.Add(Ui.Row("Clicks / scroll notches", _clicksPerEvent));

            s.Controls.Add(Ui.Spacer(14));
            s.Controls.Add(Theme.SectionHeader("Hold time (press → release)"));
            s.Controls.Add(Ui.Spacer(6));

            _holdMin = Ui.Num(0, 60000, 80);
            _holdMax = Ui.Num(0, 60000, 80);
            s.Controls.Add(Ui.RowMulti("Random range", _holdMin, Ui.Dash(), _holdMax, Ui.Suffix("ms")));

            s.Controls.Add(Ui.Spacer(8));
            Label tip = Theme.Label("A small random hold (e.g. 20–60 ms) makes clicks look human.", true);
            tip.MaximumSize = new Size(Ui.ContentWidth, 0);
            s.Controls.Add(tip);
            return s;
        }

        private Control BuildTimingPage()
        {
            FlowLayoutPanel s = Ui.Stack();
            s.Controls.Add(Theme.SectionHeader("Interval between events"));
            s.Controls.Add(Ui.Spacer(6));

            _intervalMin = Ui.Num(0, 3600000, 90);
            _intervalMax = Ui.Num(0, 3600000, 90);
            EventHandler cps = delegate { UpdateCps(); };
            _intervalMin.ValueChanged += cps;
            _intervalMax.ValueChanged += cps;
            s.Controls.Add(Ui.RowMulti("Random range", _intervalMin, Ui.Dash(), _intervalMax, Ui.Suffix("ms")));

            _cpsLabel = Theme.Label("", true);
            s.Controls.Add(Ui.Row("", _cpsLabel));

            int[] rates = { 5, 10, 25, 50, 100 };
            List<Control> rateButtons = new List<Control>();
            foreach (int r in rates)
            {
                Button b = Ui.SmallButton(r + " CPS", 66);
                int ms = (int)Math.Round(1000.0 / r);
                b.Click += delegate { _intervalMin.Value = ms; _intervalMax.Value = ms; UpdateCps(); };
                rateButtons.Add(b);
            }
            s.Controls.Add(Ui.RowMulti("Quick rate", rateButtons.ToArray()));

            s.Controls.Add(Ui.Spacer(14));
            s.Controls.Add(Theme.SectionHeader("How long to run"));
            s.Controls.Add(Ui.Spacer(6));

            _repeatCombo = Ui.Combo(180, "Until stopped", "Fixed number of clicks", "For a duration");
            _repeatCombo.SelectedIndexChanged += delegate { UpdateRepeatVisibility(); };
            s.Controls.Add(Ui.Row("Repeat", _repeatCombo));

            _repeatCount = Ui.Num(1, 100000000, 110);
            _countGroup = SubGroup(Ui.Row("Number of clicks", _repeatCount));
            s.Controls.Add(_countGroup);

            _durationSecs = Ui.Num(1, 864000, 110);
            _durationGroup = SubGroup(Ui.RowMulti("Duration", _durationSecs, Ui.Suffix("sec")));
            s.Controls.Add(_durationGroup);

            s.Controls.Add(Ui.Spacer(14));
            s.Controls.Add(Theme.SectionHeader("Start"));
            s.Controls.Add(Ui.Spacer(6));
            _startDelay = Ui.Num(0, 600000, 100);
            s.Controls.Add(Ui.RowMulti("Countdown before start", _startDelay, Ui.Suffix("ms")));
            return s;
        }

        private Control BuildMovePage()
        {
            FlowLayoutPanel s = Ui.Stack();
            s.Controls.Add(Theme.SectionHeader("Where to click"));
            s.Controls.Add(Ui.Spacer(6));

            _positionCombo = Ui.Combo(210, "Current cursor position", "Fixed point",
                "Random inside a region", "Sequence of points");
            _positionCombo.SelectedIndexChanged += delegate { UpdatePositionVisibility(); };
            s.Controls.Add(Ui.Row("Target", _positionCombo));

            // Fixed point
            _fixedX = Ui.Num(-100000, 100000, 90);
            _fixedY = Ui.Num(-100000, 100000, 90);
            Button pickFixed = Ui.SmallButton("Pick…", 70);
            pickFixed.Click += delegate { PickPoint(delegate(int x, int y) { _fixedX.Value = Clamp(x, _fixedX); _fixedY.Value = Clamp(y, _fixedY); }); };
            _fixedGroup = SubGroup(Ui.RowMulti("Point (x, y)", _fixedX, _fixedY, pickFixed));

            // Region
            _regL = Ui.Num(-100000, 100000, 80);
            _regT = Ui.Num(-100000, 100000, 80);
            _regR = Ui.Num(-100000, 100000, 80);
            _regB = Ui.Num(-100000, 100000, 80);
            Button pickTL = Ui.SmallButton("Pick top-left", 108);
            pickTL.Click += delegate { PickPoint(delegate(int x, int y) { _regL.Value = Clamp(x, _regL); _regT.Value = Clamp(y, _regT); }); };
            Button pickBR = Ui.SmallButton("Pick bottom-right", 128);
            pickBR.Click += delegate { PickPoint(delegate(int x, int y) { _regR.Value = Clamp(x, _regR); _regB.Value = Clamp(y, _regB); }); };
            FlowLayoutPanel reg = new FlowLayoutPanel();
            reg.FlowDirection = FlowDirection.TopDown;
            reg.WrapContents = false;
            reg.AutoSize = true;
            reg.Controls.Add(Ui.RowMulti("Left / Top", _regL, _regT));
            reg.Controls.Add(Ui.RowMulti("Right / Bottom", _regR, _regB));
            reg.Controls.Add(Ui.RowMulti("Capture", pickTL, pickBR));
            _regionGroup = SubGroup(reg);

            // Points
            _pointsList = new ListBox();
            _pointsList.Width = 300;
            _pointsList.Height = 120;
            _pointsList.BackColor = Theme.PanelAlt;
            _pointsList.ForeColor = Theme.Text;
            _pointsList.BorderStyle = BorderStyle.FixedSingle;
            Button addPt = Ui.SmallButton("Add point…", 100);
            addPt.Click += delegate { PickPoint(delegate(int x, int y) { _profile.Points.Add(new ClickPoint(x, y)); RefreshPoints(); }); };
            Button rmPt = Ui.SmallButton("Remove", 80);
            rmPt.Click += delegate
            {
                int i = _pointsList.SelectedIndex;
                if (i >= 0 && i < _profile.Points.Count) { _profile.Points.RemoveAt(i); RefreshPoints(); }
            };
            Button clrPt = Ui.SmallButton("Clear", 70);
            clrPt.Click += delegate { _profile.Points.Clear(); RefreshPoints(); };
            _sequenceLoop = Ui.Check("Loop the sequence");
            FlowLayoutPanel pts = new FlowLayoutPanel();
            pts.FlowDirection = FlowDirection.TopDown; pts.WrapContents = false; pts.AutoSize = true;
            pts.Controls.Add(Ui.Row("Points", _pointsList));
            pts.Controls.Add(Ui.RowMulti("", addPt, rmPt, clrPt));
            pts.Controls.Add(Ui.Row("", _sequenceLoop));
            _pointsGroup = SubGroup(pts);

            s.Controls.Add(_fixedGroup);
            s.Controls.Add(_regionGroup);
            s.Controls.Add(_pointsGroup);

            s.Controls.Add(Ui.Spacer(14));
            s.Controls.Add(Theme.SectionHeader("How the cursor moves"));
            s.Controls.Add(Ui.Spacer(6));

            _movementCombo = Ui.Combo(210, "Teleport (instant)", "Linear glide", "Humanized (curved)");
            s.Controls.Add(Ui.Row("Movement", _movementCombo));
            _movementMs = Ui.Num(0, 60000, 90);
            s.Controls.Add(Ui.RowMulti("Glide time", _movementMs, Ui.Suffix("ms")));
            _jitter = Ui.Num(0, 5000, 80);
            s.Controls.Add(Ui.RowMulti("Target jitter radius", _jitter, Ui.Suffix("px")));
            _returnToOrigin = Ui.Check("Return cursor to its start position when the run ends");
            s.Controls.Add(Ui.Row("", _returnToOrigin));
            return s;
        }

        private Control BuildAiPage()
        {
            FlowLayoutPanel s = Ui.Stack();
            s.Controls.Add(Theme.SectionHeader("Generate a pattern with AI"));
            s.Controls.Add(Ui.Spacer(4));
            Label blurb = Theme.Label("Describe what you want in plain English and let your chosen AI build the pattern.", true);
            blurb.MaximumSize = new Size(Ui.ContentWidth, 0);
            s.Controls.Add(blurb);
            s.Controls.Add(Ui.Spacer(10));

            _providerCombo = Ui.Combo(220,
                AiProviders.Display(AiProviders.Anthropic),
                AiProviders.Display(AiProviders.OpenAI),
                AiProviders.Display(AiProviders.Google));
            _providerCombo.SelectedIndexChanged += delegate { OnProviderChanged(); };
            s.Controls.Add(Ui.Row("Provider", _providerCombo));

            _apiKey = Ui.Text(360, true);
            s.Controls.Add(Ui.Row("API key", _apiKey));
            _keyNote = Theme.Label("", true);
            _keyNote.MaximumSize = new Size(Ui.ContentWidth, 0);
            _keyNote.Margin = new Padding(0, 2, 0, 8);
            s.Controls.Add(_keyNote);

            // Editable so any current model id works, with per-provider suggestions.
            _modelCombo = new ComboBox();
            _modelCombo.Width = 220;
            _modelCombo.DropDownStyle = ComboBoxStyle.DropDown;
            _modelCombo.FlatStyle = FlatStyle.Flat;
            _modelCombo.BackColor = Theme.PanelAlt;
            _modelCombo.ForeColor = Theme.Text;
            _modelCombo.Font = Theme.UiFont;
            s.Controls.Add(Ui.Row("Model", _modelCombo));

            Label describeLbl = Theme.Label("Describe what you want", false);
            describeLbl.Margin = new Padding(0, 8, 0, 4);
            s.Controls.Add(describeLbl);

            _aiPrompt = new TextBox();
            _aiPrompt.Multiline = true;
            _aiPrompt.Width = 500;
            _aiPrompt.Height = 84;
            _aiPrompt.ScrollBars = ScrollBars.Vertical;
            Theme.StyleInput(_aiPrompt);
            _aiPrompt.Text = "Click like a human roughly every 1-3 seconds near the center of the screen, with slight random movement, for 5 minutes.";
            s.Controls.Add(_aiPrompt);

            _generateButton = new Button();
            _generateButton.Text = "✦  Generate pattern";
            _generateButton.Size = new Size(190, 34);
            Theme.StylePrimaryButton(_generateButton);
            _generateButton.Click += OnGenerateClicked;
            s.Controls.Add(Ui.Row("", _generateButton));

            _aiResult = Theme.Label("", false);
            _aiResult.MaximumSize = new Size(Ui.ContentWidth, 0);
            _aiResult.ForeColor = Theme.Good;
            s.Controls.Add(Ui.Row("", _aiResult));

            s.Controls.Add(Ui.Spacer(14));
            s.Controls.Add(Theme.SectionHeader("Or start from a preset"));
            s.Controls.Add(Ui.Spacer(6));
            FlowLayoutPanel presets = new FlowLayoutPanel();
            presets.AutoSize = true; presets.WrapContents = true; presets.Width = Ui.ContentWidth;
            AddPreset(presets, "Rapid fire", RapidFirePreset);
            AddPreset(presets, "Human idle jiggle", HumanIdlePreset);
            AddPreset(presets, "Gentle 2s clicks", GentlePreset);
            AddPreset(presets, "Double-click spam", DoublePreset);
            AddPreset(presets, "Random in region", RegionPreset);
            s.Controls.Add(presets);
            return s;
        }

        private Control BuildProfilesPage()
        {
            FlowLayoutPanel s = Ui.Stack();
            s.Controls.Add(Theme.SectionHeader("Saved profiles"));
            s.Controls.Add(Ui.Spacer(6));

            _profilesList = new ListBox();
            _profilesList.Width = 300;
            _profilesList.Height = 130;
            _profilesList.BackColor = Theme.PanelAlt;
            _profilesList.ForeColor = Theme.Text;
            _profilesList.BorderStyle = BorderStyle.FixedSingle;
            s.Controls.Add(Ui.Row("Profiles", _profilesList));

            Button load = Ui.SmallButton("Load", 80);
            load.Click += delegate { LoadSelectedProfile(); };
            Button del = Ui.SmallButton("Delete", 80);
            del.Click += delegate { DeleteSelectedProfile(); };
            Button refresh = Ui.SmallButton("Refresh", 84);
            refresh.Click += delegate { RefreshProfiles(); };
            s.Controls.Add(Ui.RowMulti("", load, del, refresh));

            _profileName = Ui.Text(200, false);
            Button save = Ui.SmallButton("Save as", 90);
            save.Click += delegate { SaveNamedProfile(); };
            s.Controls.Add(Ui.RowMulti("Save current as", _profileName, save));

            s.Controls.Add(Ui.Spacer(14));
            s.Controls.Add(Theme.SectionHeader("Global hotkeys"));
            s.Controls.Add(Ui.Spacer(6));
            _toggleKeyCombo = HotkeyCombo();
            _toggleKeyCombo.SelectedIndexChanged += delegate { ApplyHotkeysFromUi(); };
            s.Controls.Add(Ui.Row("Start / Stop", _toggleKeyCombo));
            _stopKeyCombo = HotkeyCombo();
            _stopKeyCombo.SelectedIndexChanged += delegate { ApplyHotkeysFromUi(); };
            s.Controls.Add(Ui.Row("Emergency stop", _stopKeyCombo));
            Label hkNote = Theme.Label("Hotkeys work even when ClickForge is in the background.", true);
            s.Controls.Add(Ui.Row("", hkNote));
            return s;
        }

        private Control BuildAboutPage()
        {
            FlowLayoutPanel s = Ui.Stack();
            s.Controls.Add(Theme.SectionHeader("About"));
            s.Controls.Add(Ui.Spacer(6));

            Label name = Theme.Label(AppName + " " + AppVersion, false);
            name.Font = Theme.TitleFont;
            s.Controls.Add(name);
            s.Controls.Add(Ui.Spacer(4));

            string[] lines =
            {
                "A robust, portable Windows auto clicker. No install, no runtime download —",
                "a single self-contained executable built on the .NET Framework that ships",
                "with Windows.",
                "",
                "Free and open source under the MIT License.",
                "",
                "Use responsibly and only where automated input is allowed. Many games and",
                "online services prohibit automation; ClickForge is a general desktop tool,",
                "not a cheat.",
            };
            foreach (string line in lines)
            {
                Label l = Theme.Label(line, string.IsNullOrEmpty(line) ? true : false);
                l.MaximumSize = new Size(Ui.ContentWidth, 0);
                s.Controls.Add(l);
            }

            s.Controls.Add(Ui.Spacer(10));
            LinkLabel repo = new LinkLabel();
            repo.Text = "github.com/stevologic/mouse_clicker";
            repo.LinkColor = Theme.Accent;
            repo.ActiveLinkColor = Theme.Text;
            repo.AutoSize = true;
            repo.BackColor = Color.Transparent;
            repo.LinkClicked += delegate
            {
                try { System.Diagnostics.Process.Start("https://github.com/stevologic/mouse_clicker"); }
                catch { }
            };
            s.Controls.Add(repo);
            return s;
        }

        // ---- Helpers for pages -------------------------------------------

        private FlowLayoutPanel SubGroup(Control child)
        {
            FlowLayoutPanel g = new FlowLayoutPanel();
            g.FlowDirection = FlowDirection.TopDown;
            g.WrapContents = false;
            g.AutoSize = true;
            g.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            g.Margin = new Padding(0);
            g.BackColor = Color.Transparent;
            g.Controls.Add(child);
            return g;
        }

        private void AddPreset(FlowLayoutPanel host, string text, Action apply)
        {
            Button b = Ui.SmallButton(text, 150);
            b.Height = 32;
            b.Margin = new Padding(0, 0, 8, 8);
            b.Click += delegate { apply(); };
            host.Controls.Add(b);
        }

        private ComboBox HotkeyCombo()
        {
            DarkComboBox cb = new DarkComboBox();
            cb.Width = 140;
            cb.Height = 26;
            foreach (KeyValuePair<string, int> kv in HotkeyChoices)
                cb.Items.Add(kv.Key);
            return cb;
        }

        // ---- Load / sync control <-> profile -----------------------------

        private void LoadToControls()
        {
            _buttonCombo.SelectedIndex = (int)_profile.Button;
            _actionCombo.SelectedIndex = (int)_profile.Action;
            _clicksPerEvent.Value = Clamp(_profile.ClicksPerEvent, _clicksPerEvent);
            _holdMin.Value = Clamp(_profile.HoldMinMs, _holdMin);
            _holdMax.Value = Clamp(_profile.HoldMaxMs, _holdMax);

            _intervalMin.Value = Clamp(_profile.IntervalMinMs, _intervalMin);
            _intervalMax.Value = Clamp(_profile.IntervalMaxMs, _intervalMax);
            _repeatCombo.SelectedIndex = (int)_profile.RepeatMode;
            _repeatCount.Value = Clamp(_profile.RepeatCount, _repeatCount);
            _durationSecs.Value = Clamp(_profile.DurationSeconds, _durationSecs);
            _startDelay.Value = Clamp(_profile.StartDelayMs, _startDelay);

            _positionCombo.SelectedIndex = (int)_profile.PositionMode;
            _fixedX.Value = Clamp(_profile.FixedX, _fixedX);
            _fixedY.Value = Clamp(_profile.FixedY, _fixedY);
            _regL.Value = Clamp(_profile.RegionLeft, _regL);
            _regT.Value = Clamp(_profile.RegionTop, _regT);
            _regR.Value = Clamp(_profile.RegionRight, _regR);
            _regB.Value = Clamp(_profile.RegionBottom, _regB);
            _sequenceLoop.Checked = _profile.SequenceLoop;
            _movementCombo.SelectedIndex = (int)_profile.MovementMode;
            _movementMs.Value = Clamp(_profile.MovementDurationMs, _movementMs);
            _jitter.Value = Clamp(_profile.JitterRadius, _jitter);
            _returnToOrigin.Checked = _profile.ReturnToOrigin;

            _loadingUi = true;
            _uiProvider = _profile.Provider;
            _providerCombo.SelectedIndex = AiProviders.DisplayIndex(_profile.Provider);
            PopulateModelCombo(_profile.Provider, _profile.GetModel(_profile.Provider));
            _apiKey.Text = _profile.GetKey(_profile.Provider);
            UpdateKeyNote(_profile.Provider);
            _loadingUi = false;

            SelectHotkey(_toggleKeyCombo, _profile.ToggleHotkeyVk);
            SelectHotkey(_stopKeyCombo, _profile.StopHotkeyVk);

            RefreshPoints();
            UpdateActionLabels();
            UpdateCps();
            UpdateRepeatVisibility();
            UpdatePositionVisibility();
            RefreshProfiles();
        }

        private void SyncToProfile()
        {
            _profile.Button = (MouseButton)_buttonCombo.SelectedIndex;
            _profile.Action = (ClickAction)_actionCombo.SelectedIndex;
            _profile.ClicksPerEvent = (int)_clicksPerEvent.Value;
            _profile.HoldMinMs = (int)_holdMin.Value;
            _profile.HoldMaxMs = (int)_holdMax.Value;

            _profile.IntervalMinMs = (int)_intervalMin.Value;
            _profile.IntervalMaxMs = (int)_intervalMax.Value;
            _profile.RepeatMode = (RepeatMode)_repeatCombo.SelectedIndex;
            _profile.RepeatCount = (int)_repeatCount.Value;
            _profile.DurationSeconds = (int)_durationSecs.Value;
            _profile.StartDelayMs = (int)_startDelay.Value;

            _profile.PositionMode = (PositionMode)_positionCombo.SelectedIndex;
            _profile.FixedX = (int)_fixedX.Value;
            _profile.FixedY = (int)_fixedY.Value;
            _profile.RegionLeft = (int)_regL.Value;
            _profile.RegionTop = (int)_regT.Value;
            _profile.RegionRight = (int)_regR.Value;
            _profile.RegionBottom = (int)_regB.Value;
            _profile.SequenceLoop = _sequenceLoop.Checked;
            _profile.MovementMode = (MovementMode)_movementCombo.SelectedIndex;
            _profile.MovementDurationMs = (int)_movementMs.Value;
            _profile.JitterRadius = (int)_jitter.Value;
            _profile.ReturnToOrigin = _returnToOrigin.Checked;

            string prov = AiProviders.FromDisplayIndex(_providerCombo.SelectedIndex);
            _profile.Provider = prov;
            _profile.SetKey(prov, _apiKey.Text.Trim());
            _profile.SetModel(prov, _modelCombo.Text.Trim());
            _profile.ApiKey = _apiKey.Text.Trim();
            _profile.Model = _modelCombo.Text.Trim();

            _profile.ToggleHotkeyVk = SelectedVk(_toggleKeyCombo, 0x75);
            _profile.StopHotkeyVk = SelectedVk(_stopKeyCombo, 0x77);
            _profile.Normalize();
        }

        private void OnProviderChanged()
        {
            if (_loadingUi) return;
            // Remember what the user typed under the provider we were showing.
            if (!string.IsNullOrEmpty(_uiProvider))
            {
                _profile.SetKey(_uiProvider, _apiKey.Text.Trim());
                _profile.SetModel(_uiProvider, _modelCombo.Text.Trim());
            }
            string prov = AiProviders.FromDisplayIndex(_providerCombo.SelectedIndex);
            _uiProvider = prov;
            _profile.Provider = prov;
            PopulateModelCombo(prov, _profile.GetModel(prov));
            _apiKey.Text = _profile.GetKey(prov);
            UpdateKeyNote(prov);
        }

        private void PopulateModelCombo(string provider, string model)
        {
            _modelCombo.Items.Clear();
            _modelCombo.Items.AddRange(AiProviders.Models(provider));
            _modelCombo.Text = model;
        }

        private void UpdateKeyNote(string provider)
        {
            _keyNote.Text = "Get a key at " + AiProviders.KeyHint(provider)
                + ". Stored locally in %APPDATA%\\ClickForge. Leave blank to use the offline generator.";
        }

        private void SelectHotkey(ComboBox cb, int vk)
        {
            for (int i = 0; i < HotkeyChoices.Length; i++)
                if (HotkeyChoices[i].Value == vk) { cb.SelectedIndex = i; return; }
            cb.SelectedIndex = 0;
        }

        private int SelectedVk(ComboBox cb, int fallback)
        {
            int i = cb.SelectedIndex;
            if (i >= 0 && i < HotkeyChoices.Length)
                return HotkeyChoices[i].Value;
            return fallback;
        }

        private static decimal Clamp(int value, NumericUpDown n)
        {
            decimal v = value;
            if (v < n.Minimum) v = n.Minimum;
            if (v > n.Maximum) v = n.Maximum;
            return v;
        }

        // ---- Dynamic UI state --------------------------------------------

        private void UpdateActionLabels()
        {
            ClickAction a = (ClickAction)Math.Max(0, _actionCombo.SelectedIndex);
            bool scroll = a == ClickAction.ScrollUp || a == ClickAction.ScrollDown;
            _clicksPerEvent.Enabled = a == ClickAction.MultiClick || scroll;
        }

        private void UpdateCps()
        {
            int min = (int)_intervalMin.Value;
            int max = (int)_intervalMax.Value;
            if (min <= 0 && max <= 0)
            {
                _cpsLabel.Text = "≈ as fast as possible";
                return;
            }
            double avg = (min + max) / 2.0;
            if (avg <= 0) avg = 1;
            double rate = 1000.0 / avg;
            _cpsLabel.Text = "≈ " + rate.ToString("0.#") + " clicks/sec";
        }

        private void UpdateRepeatVisibility()
        {
            RepeatMode m = (RepeatMode)Math.Max(0, _repeatCombo.SelectedIndex);
            _countGroup.Visible = m == RepeatMode.Count;
            _durationGroup.Visible = m == RepeatMode.Duration;
        }

        private void UpdatePositionVisibility()
        {
            PositionMode m = (PositionMode)Math.Max(0, _positionCombo.SelectedIndex);
            _fixedGroup.Visible = m == PositionMode.FixedPoint;
            _regionGroup.Visible = m == PositionMode.RandomInRegion;
            _pointsGroup.Visible = m == PositionMode.PointSequence;
        }

        private void RefreshPoints()
        {
            _pointsList.Items.Clear();
            for (int i = 0; i < _profile.Points.Count; i++)
            {
                ClickPoint p = _profile.Points[i];
                _pointsList.Items.Add((i + 1) + ".  (" + p.X + ", " + p.Y + ")");
            }
        }

        // ---- Point capture -----------------------------------------------

        private void PickPoint(Action<int, int> callback)
        {
            if (_captureTimer != null) return;
            _captureCallback = callback;
            _captureCountdown = 3;
            _captureTimer = new Timer();
            _captureTimer.Interval = 1000;
            _captureTimer.Tick += CaptureTick;
            _statusLabel.ForeColor = Theme.Accent;
            _statusLabel.Text = "Move the mouse — capturing in 3...";
            _captureTimer.Start();
        }

        private void CaptureTick(object sender, EventArgs e)
        {
            _captureCountdown--;
            if (_captureCountdown > 0)
            {
                _statusLabel.Text = "Move the mouse — capturing in " + _captureCountdown + "...";
                return;
            }
            _captureTimer.Stop();
            _captureTimer.Dispose();
            _captureTimer = null;

            NativeMethods.POINT p = InputSimulator.GetCursor();
            _statusLabel.ForeColor = Theme.Good;
            _statusLabel.Text = "Captured (" + p.X + ", " + p.Y + ")";
            if (_captureCallback != null)
            {
                Action<int, int> cb = _captureCallback;
                _captureCallback = null;
                cb(p.X, p.Y);
            }
            if (!_engine.IsRunning)
                ResetIdleStatus();
        }

        // ---- Engine control ----------------------------------------------

        private void ToggleRun()
        {
            if (_engine.IsRunning)
                _engine.Stop();
            else
                StartRun();
        }

        private void StartRun()
        {
            SyncToProfile();
            ProfileStore.SaveConfig(_profile);
            _clickCount = 0;
            _countLabel.Text = "0 clicks";
            _statusLabel.ForeColor = Theme.Accent;
            _engine.Start(_profile);
            UpdateStartButton();
            if (_hud != null) _hud.Begin();
        }

        private void WireEngine()
        {
            _engine.ClickPerformed += delegate(long n)
            {
                _clickCount = n;
                UiInvoke(delegate
                {
                    _countLabel.Text = n + (n == 1 ? " click" : " clicks");
                    if (_hud != null) _hud.SetCount(n);
                    // Throttle the visual pings so very high CPS stays smooth.
                    int now = Environment.TickCount;
                    if (now - _lastRippleTick > 70)
                    {
                        _lastRippleTick = now;
                        if (_pulse != null) _pulse.Ping();
                        if (_hud != null) _hud.Ping();
                    }
                });
            };
            _engine.Status += delegate(string s)
            {
                UiInvoke(delegate { _statusLabel.Text = s; });
            };
            _engine.Stopped += delegate(string reason)
            {
                UiInvoke(delegate
                {
                    _statusLabel.ForeColor = reason.StartsWith("Error") ? Theme.Danger : Theme.Muted;
                    _statusLabel.Text = reason;
                    UpdateStartButton();
                    if (_hud != null) _hud.End();
                });
            };
        }

        private void UpdateStartButton()
        {
            bool running = _engine.IsRunning;
            _startButton.Text = running ? "■   Stop" : "▶   Start";
            if (running)
            {
                _startButton.ColorA = Color.FromArgb(232, 96, 104);
                _startButton.ColorB = Color.FromArgb(205, 62, 96);
                _startButton.Pulsing = true;
            }
            else
            {
                _startButton.ColorA = Color.FromArgb(64, 200, 132);
                _startButton.ColorB = Color.FromArgb(38, 168, 152);
                _startButton.Pulsing = false;
            }
            _startButton.Invalidate();
            if (_pulse != null) _pulse.Active = running;
        }

        private void ResetIdleStatus()
        {
            _statusLabel.ForeColor = Theme.Muted;
            _statusLabel.Text = "Idle";
        }

        // ---- AI ----------------------------------------------------------

        private async void OnGenerateClicked(object sender, EventArgs e)
        {
            SyncToProfile();
            ProfileStore.SaveConfig(_profile);

            _generateButton.Enabled = false;
            _aiResult.ForeColor = Theme.Muted;
            _aiResult.Text = "Generating…";
            try
            {
                AiResult res = await _ai.GeneratePatternAsync(_aiPrompt.Text.Trim(), _profile);
                if (res.Success)
                {
                    PatternMapper.ApplyToProfile(_profile, res.Pattern);
                    LoadToControls();
                    _aiResult.ForeColor = res.UsedOffline ? Theme.Muted : Theme.Good;
                    _aiResult.Text = (res.UsedOffline ? "Offline: " : "✓ ") + res.Explanation;
                }
                else
                {
                    _aiResult.ForeColor = Theme.Danger;
                    _aiResult.Text = res.Error;
                }
            }
            catch (Exception ex)
            {
                _aiResult.ForeColor = Theme.Danger;
                _aiResult.Text = "Unexpected error: " + ex.Message;
            }
            finally
            {
                _generateButton.Enabled = true;
            }
        }

        // ---- Presets -----------------------------------------------------

        private void ApplyPreset(Dictionary<string, object> pat, string note)
        {
            SyncToProfile();
            PatternMapper.ApplyToProfile(_profile, pat);
            LoadToControls();
            SwitchPage("Click");
            _statusLabel.ForeColor = Theme.Good;
            _statusLabel.Text = note;
        }

        private void RapidFirePreset()
        {
            var d = new Dictionary<string, object>();
            d["button"] = "Left"; d["clickType"] = "Single";
            d["intervalMinMs"] = 8; d["intervalMaxMs"] = 8;
            d["holdMinMs"] = 0; d["holdMaxMs"] = 0;
            d["positionMode"] = "CurrentCursor"; d["movementMode"] = "Teleport";
            d["repeatMode"] = "Infinite"; d["jitterRadius"] = 0;
            ApplyPreset(d, "Preset: rapid fire (~120 CPS)");
        }

        private void HumanIdlePreset()
        {
            var pr = ScreenInfo.Primary();
            var d = new Dictionary<string, object>();
            d["button"] = "Left"; d["clickType"] = "Single";
            d["intervalMinMs"] = 4000; d["intervalMaxMs"] = 9000;
            d["holdMinMs"] = 30; d["holdMaxMs"] = 90;
            d["positionMode"] = "RandomInRegion";
            d["regionLeft"] = pr.Left + pr.Width / 3; d["regionTop"] = pr.Top + pr.Height / 3;
            d["regionRight"] = pr.Left + 2 * pr.Width / 3; d["regionBottom"] = pr.Top + 2 * pr.Height / 3;
            d["movementMode"] = "Humanized"; d["movementDurationMs"] = 500; d["jitterRadius"] = 6;
            d["repeatMode"] = "Infinite";
            ApplyPreset(d, "Preset: human idle jiggle");
        }

        private void GentlePreset()
        {
            var d = new Dictionary<string, object>();
            d["button"] = "Left"; d["clickType"] = "Single";
            d["intervalMinMs"] = 1800; d["intervalMaxMs"] = 2200;
            d["holdMinMs"] = 20; d["holdMaxMs"] = 60;
            d["positionMode"] = "CurrentCursor"; d["movementMode"] = "Teleport";
            d["repeatMode"] = "Infinite";
            ApplyPreset(d, "Preset: gentle 2s clicks");
        }

        private void DoublePreset()
        {
            var d = new Dictionary<string, object>();
            d["button"] = "Left"; d["clickType"] = "Double";
            d["intervalMinMs"] = 200; d["intervalMaxMs"] = 350;
            d["holdMinMs"] = 15; d["holdMaxMs"] = 40;
            d["positionMode"] = "CurrentCursor"; d["repeatMode"] = "Infinite";
            ApplyPreset(d, "Preset: double-click spam");
        }

        private void RegionPreset()
        {
            var pr = ScreenInfo.Primary();
            var d = new Dictionary<string, object>();
            d["button"] = "Left"; d["clickType"] = "Single";
            d["intervalMinMs"] = 120; d["intervalMaxMs"] = 260;
            d["positionMode"] = "RandomInRegion";
            d["regionLeft"] = pr.Left + 100; d["regionTop"] = pr.Top + 100;
            d["regionRight"] = pr.Left + pr.Width - 100; d["regionBottom"] = pr.Top + pr.Height - 100;
            d["movementMode"] = "Humanized"; d["movementDurationMs"] = 250; d["jitterRadius"] = 3;
            d["repeatMode"] = "Infinite";
            ApplyPreset(d, "Preset: random clicks in region");
        }

        // ---- Profiles ----------------------------------------------------

        private void RefreshProfiles()
        {
            _profilesList.Items.Clear();
            foreach (string name in ProfileStore.ListNames())
                _profilesList.Items.Add(name);
        }

        private void SaveNamedProfile()
        {
            string name = _profileName.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show(this, "Enter a name first.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            SyncToProfile();
            ProfileStore.SaveNamed(name, _profile);
            RefreshProfiles();
            _profileName.Text = "";
        }

        private void LoadSelectedProfile()
        {
            object sel = _profilesList.SelectedItem;
            if (sel == null) return;
            try
            {
                Profile p = ProfileStore.LoadNamed(sel.ToString());
                if (p != null)
                {
                    _profile = p;
                    LoadToControls();
                    ApplyHotkeysFromUi();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not load: " + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void DeleteSelectedProfile()
        {
            object sel = _profilesList.SelectedItem;
            if (sel == null) return;
            ProfileStore.DeleteNamed(sel.ToString());
            RefreshProfiles();
        }

        // ---- Hotkeys -----------------------------------------------------

        private void SetupHotkeys()
        {
            _hotkeys = new HotkeyManager(Handle);
            _hotkeys.Triggered += OnHotkey;
            ApplyHotkeysFromUi();
        }

        private void ApplyHotkeysFromUi()
        {
            if (_hotkeys == null) return;
            int toggle = SelectedVk(_toggleKeyCombo, 0x75);
            int stop = SelectedVk(_stopKeyCombo, 0x77);
            _hotkeys.Register(toggle, stop);
        }

        private void OnHotkey(int id)
        {
            UiInvoke(delegate
            {
                if (id == HotkeyManager.ID_TOGGLE) ToggleRun();
                else if (id == HotkeyManager.ID_STOP) _engine.Stop();
            });
        }

        protected override void WndProc(ref Message m)
        {
            if (_hotkeys != null && _hotkeys.HandleMessage(ref m))
                return;
            base.WndProc(ref m);
        }

        // ---- Lifecycle ---------------------------------------------------

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (_anim != null) _anim.Stop();
            if (_hud != null) { try { _hud.End(); _hud.Dispose(); } catch { } }
            _engine.Stop();
            try { SyncToProfile(); ProfileStore.SaveConfig(_profile); }
            catch { }
            if (_hotkeys != null) _hotkeys.Unregister();
        }

        // Captures the real on-screen pixels of every page (works for the
        // custom-painted footer/Start button that DrawToBitmap misses).
        public void CaptureAllPagesScreen(string dir)
        {
            Directory.CreateDirectory(dir);
            _active = true;
            string[] names = { "Click", "Timing", "Movement", "AI", "Profiles", "About" };
            foreach (string name in names)
            {
                SwitchPage(name);
                _navPillY = _navPillTarget;
                for (int k = 0; k < 30; k++)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(16);
                }
                Rectangle b = Bounds;
                using (Bitmap bmp = new Bitmap(b.Width, b.Height))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                        g.CopyFromScreen(b.Location, Point.Empty, b.Size);
                    bmp.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
                }
            }
        }

        // Renders every page to a PNG for headless visual verification.
        public void CaptureAllPages(string dir)
        {
            Directory.CreateDirectory(dir);
            string[] names = { "Click", "Timing", "Movement", "AI", "Profiles", "About" };
            foreach (string name in names)
            {
                SwitchPage(name);
                _navPillY = _navPillTarget; // settle the pill for a clean still
                for (int k = 0; k < 30; k++) { _scene.Update(); _scene.Render(); }
                Application.DoEvents();
                using (Bitmap bmp = new Bitmap(Width, Height))
                {
                    DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                    bmp.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
                }
            }
        }

        private void UiInvoke(Action a)
        {
            if (!IsHandleCreated) return;
            try
            {
                if (InvokeRequired) BeginInvoke(a);
                else a();
            }
            catch { }
        }
    }
}
