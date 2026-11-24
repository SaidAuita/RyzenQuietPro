using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RyzenQuietPro
{
    public class SettingsForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;
        private const int WM_NCHITTEST = 0x84;
        private const int HTCLIENT = 1;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        private readonly HardwareMonitor _hardware;
        private readonly AppSettings _settings;
        private readonly Action _onSettingsUpdated;
        private readonly Action<PowerPlanManager.Mode> _onModeChangeRequested;

        private readonly ToolTip _toolTip = new ToolTip
        {
            InitialDelay = 250,
            ReshowDelay = 100,
            AutoPopDelay = 30000
        };

        // UI Controls
        private Label _lblHeaderTitle = null!;
        private Label _lblSecLang = null!;
        private Label _lblSecGraphs = null!;
        private Label _lblSecTray = null!;
        private Label _lblSecUiScale = null!;
        private Label _lblSecSystem = null!;
        private Label _lblSecGpuTuning = null!;

        private readonly List<Button> _langButtons = new();
        private readonly List<Button> _trayButtons = new();
        private readonly List<Button> _uiScalePresetButtons = new();
        private readonly List<Button> _gpuPowerPresetButtons = new();
        private readonly List<Button> _gpuFanPresetButtons = new();

        private CheckBox _chkGpuTuning = null!;
        private CheckBox _chkSeparateCpuGpu = null!;
        private Label _lblGpuPowerLimit = null!;
        private TrackBar _tbGpuPowerLimit = null!;
        private CheckBox _chkGpuFanCap = null!;
        private Label _lblGpuFanCap = null!;
        private TrackBar _tbGpuFanCap = null!;
        private Label _lblGpuFailSafe = null!;

        private CheckBox _chkStopwatch = null!;
        private Label _lblSecStopwatch = null!;
        private CheckBox _chkStopwatchAuto = null!;
        private readonly List<Button> _swSourceButtons = new();
        private Label _lblSwSourceTitle = null!;
        private Label _lblSwSourceDesc = null!;
        private Label _lblSwStartWatts = null!;
        private TrackBar _tbSwStartWatts = null!;
        private Label _lblSwStopWatts = null!;
        private TrackBar _tbSwStopWatts = null!;
        private Label _lblSwHysteresis = null!;
        private TrackBar _tbSwHysteresis = null!;
        private Label _lblSwListTitle = null!;
        private Button _btnSwAdd = null!;
        private Panel _pnlSwListContainer = null!;

        // HDD Awake Schedule Controls
        private Label _lblSecHdd = null!;
        private CheckBox _chkHddSchedule = null!;
        private Panel _pnlHddControls = null!;
        private Label _lblHddWorkHours = null!;
        private ComboBox _cbHddStart = null!;
        private Label _lblHddDash = null!;
        private ComboBox _cbHddEnd = null!;
        private CheckBox _chkHddWorkDaysOnly = null!;
        private Label _lblHddOffHours = null!;
        private readonly List<Button> _hddOffHoursPresetButtons = new();
        private CheckBox _chkHddKeepAlive = null!;
        private Panel _sepHdd = null!;
        private Button _btnHddWakeNow = null!;
        private Button _btnHddSleepNow = null!;
        private Label _lblHddWakeStatus = null!;

        private class CardSection
        {
            public string Key = "";
            public Panel Card = null!;
            public Label Header = null!;
            public Func<string> TitleGetter = null!;
            public int FullHeight;
            public bool IsCollapsed = true;
        }

        private readonly List<CardSection> _cardSections = new();
        private readonly List<Panel> _allCards = new();

        // Translate & LLM Controls
        private CheckBox _chkTranslateModule = null!;
        private Label _lblSecTranslate = null!;
        private CheckBox _chkTranslateEnable = null!;
        private Panel _pnlTranslateControls = null!;
        private Label _lblTranslateApiUrl = null!;
        private TextBox _tbTranslateUrl = null!;
        private Button _btnPresetLms = null!;
        private Button _btnPresetOllama = null!;
        private Button _btnTranslateCheck = null!;
        private Label _lblTranslateCheckStatus = null!;
        private Label _lblTranslateModel = null!;
        private Label _lblTranslateLang = null!;
        private ComboBox _cbTranslateModel = null!;
        private ComboBox _cbTranslateTargetLang = null!;
        private Button _btnFreeVramNow = null!;
        private Label _lblVramStatus = null!;
        private Label _lblTranslateVramTimeout = null!;
        private ComboBox _cbTranslateVramTimeout = null!;
        private CheckBox _chkTranslateAutoCopy = null!;
        private Panel _sepTransHotkeys = null!;
        private Panel _rowHk1 = null!;
        private Panel _rowHk2 = null!;
        private Panel _rowHk3 = null!;

        private readonly List<Panel> _cardDividers = new();
        private readonly List<Button> _opacityPresetButtons = new();
        private Panel _pnlFailSafe = null!;
        private Panel _pnlBody = null!;
        private Panel? _pnlBottomSpacer;
        private Label _lblResizeGrip = null!;

        private CheckBox _chkCpuGraph = null!;
        private Label _lblCpuScaleTitle = null!;
        private readonly List<Button> _cpuScaleButtons = new();
        private CheckBox _chkCpuCores = null!;
        private CheckBox _chkTopProcesses = null!;
        private CheckBox _chkRamGraph = null!;
        private CheckBox _chkGpuGraph = null!;
        private CheckBox _chkGpuTemp = null!;
        private CheckBox _chkGpuFan = null!;
        private CheckBox _chkVramGraph = null!;
        private CheckBox _chkTopVramProcesses = null!;
        private CheckBox _chkDiskGraph = null!;
        private CheckBox _chkEnhancedDiskMode = null!;
        private CheckBox _chkFanGraph = null!;
        private Label _lblFanStatus = null!;
        private Button _btnFanToggle = null!;
        private Button _btnFanFolder = null!;
        private Button _btnFanModeGraph = null!;
        private Button _btnFanModeIcons = null!;
        private Button _btnFanModeGrid = null!;
        private CheckBox _chkFanDemo = null!;
        private Label _lblFanScale = null!;
        private TrackBar _tbFanScale = null!;
        private Button _btnFanSelect = null!;
        private CheckBox? _chkShowAllGpus;

        private Button _btnResetGraphs = null!;

        private TrackBar _sliderOpacity = null!;
        private Label _lblOpacityVal = null!;

        private TrackBar _sliderUiScale = null!;
        private Label _lblUiScaleVal = null!;
        private CheckBox _chkAutoUiScale = null!;

        private CheckBox _chkAlwaysOnTop = null!;
        private CheckBox _chkAutoQuiet = null!;
        private CheckBox _chkStartup = null!;
        private CheckBox _chkStartMinimized = null!;

        private Button _btnSpecs = null!;
        private Button _btnTools = null!;
        private Button _btnExpandAll = null!;
        private Button _btnCollapseAll = null!;
        private Button _btnClose = null!;

        // ServerSync Card
        private Label _lblSecServerSync = null!;
        private CheckBox _chkServerSync = null!;
        private Panel _pnlServerSyncControls = null!;
        private Label _lblServerSyncHost = null!;
        private TextBox _txtServerSyncHost = null!;
        private Label _lblServerSyncPort = null!;
        private TextBox _txtServerSyncPort = null!;
        private Button _btnServerSyncTest = null!;
        private Label _lblServerSyncTestStatus = null!;
        private Label _lblServerSyncHint = null!;

        // Keyboard Layout Switcher Card (Punto Switcher)
        private Label _lblSecLayout = null!;
        private CheckBox _chkLayoutSwitch = null!;
        private Panel _pnlLayoutControls = null!;
        private FlowLayoutPanel _flowModifiers = null!;
        private Label _lblLayoutHotkey = null!;
        private CheckBox _chkModCtrl = null!;
        private CheckBox _chkModAlt = null!;
        private CheckBox _chkModShift = null!;
        private CheckBox _chkModWin = null!;
        private FlowLayoutPanel _flowKeyRow = null!;
        private Label _lblLayoutKey = null!;
        private ComboBox _cbLayoutKey = null!;
        private Button _btnResetLayoutHotkey = null!;
        private Label _lblLayoutSwitchHint = null!;

        // Desktop Icons Card (Multi-Monitor)
        private Label _lblSecDesktopIcons = null!;
        private Panel _pnlDesktopIconsControls = null!;
        private Button _btnDesktopIconsSave = null!;
        private Button _btnDesktopIconsRestore = null!;
        private CheckBox _chkDesktopIconsAutoRestore = null!;
        private Label _lblDesktopIconsStatus = null!;
        private Label _lblDesktopIconsHint = null!;

        private HardwareInfoForm? _infoForm;
        private FanSelectionForm? _fanSelectionForm;

        public SettingsForm(
            HardwareMonitor hardware,
            AppSettings settings,
            Action onSettingsUpdated,
            Action<PowerPlanManager.Mode> onModeChangeRequested)
        {
            _hardware = hardware;
            _settings = settings;
            _onSettingsUpdated = onSettingsUpdated;
            _onModeChangeRequested = onModeChangeRequested;

            bool multiGpu = _hardware.Gpu.GpuCount > 1;
            int totalFormH = (multiGpu ? 1740 : 1710) + 185;

            var screen = Screen.FromPoint(Cursor.Position);
            int availH = screen.WorkingArea.Height - 30;
            int defaultW = 560;
            int minFormW = 320;
            int formW = Math.Clamp(_settings.SettingsWidth >= minFormW ? _settings.SettingsWidth : defaultW, minFormW, 1100);
            int formH = (_settings.SettingsHeight >= 320 && _settings.SettingsHeight <= availH)
                ? _settings.SettingsHeight
                : Math.Min(650, availH);

            this.AutoScaleMode = AutoScaleMode.None;
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.ShowInTaskbar = false;
            this.ClientSize = new Size(formW, formH);
            this.MinimumSize = new Size(minFormW, 300);
            this.BackColor = Color.FromArgb(24, 24, 32);
            this.ForeColor = Color.White;
            this.DoubleBuffered = true;
            this.Icon = AppIcons.QuietIcon;

            InitializeComponents();
            Loc.LanguageChanged += RefreshLocalizedTexts;

            Action onHddStatusChanged = () => {
                if (this.IsHandleCreated && !this.IsDisposed)
                {
                    try { this.BeginInvoke((Action)UpdateHddCardUI); } catch { }
                }
            };
            _hardware.HddSchedule.StatusChanged += onHddStatusChanged;
            this.FormClosed += (s, e) => {
                _hardware.HddSchedule.StatusChanged -= onHddStatusChanged;
                Loc.LanguageChanged -= RefreshLocalizedTexts;
            };

            _hardware.Fans.StatusChanged += () => {
                if (this.IsHandleCreated && !this.IsDisposed)
                {
                    try { this.BeginInvoke((Action)UpdateFanStatusUI); } catch { }
                }
            };
            UpdateFanStatusUI();

            this.Paint += (s, e) => {
                // 2px vibrant Sky Blue accent border to clearly delineate SettingsForm from background
                using var pen = new Pen(Color.FromArgb(14, 165, 233), 2f);
                e.Graphics.DrawRectangle(pen, 1, 1, this.ClientSize.Width - 2, this.ClientSize.Height - 2);
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if (m.Result == (IntPtr)HTCLIENT)
                {
                    Point pt = PointToClient(Cursor.Position);
                    int bw = 8;
                    bool left = pt.X <= bw;
                    bool right = pt.X >= ClientSize.Width - bw;
                    bool top = pt.Y <= bw;
                    bool bottom = pt.Y >= ClientSize.Height - bw;

                    if (top && left) m.Result = (IntPtr)HTTOPLEFT;
                    else if (top && right) m.Result = (IntPtr)HTTOPRIGHT;
                    else if (bottom && left) m.Result = (IntPtr)HTBOTTOMLEFT;
                    else if (bottom && right) m.Result = (IntPtr)HTBOTTOMRIGHT;
                    else if (left) m.Result = (IntPtr)HTLEFT;
                    else if (right) m.Result = (IntPtr)HTRIGHT;
                    else if (top) m.Result = (IntPtr)HTTOP;
                    else if (bottom) m.Result = (IntPtr)HTBOTTOM;
                }
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (this.WindowState == FormWindowState.Normal && this.Width > 0 && this.Height > 0)
            {
                _settings.SettingsWidth = this.Width;
                _settings.SettingsHeight = this.Height;
                _settings.Save();
                RelayoutCards();
                this.Invalidate();
            }
        }

        private void InitializeComponents()
        {
            int w = this.ClientSize.Width;
            int cardMargin = 14;
            int scrollbarAllowance = 22;
            int cardW = Math.Max(260, w - (cardMargin * 2) - scrollbarAllowance);
            int cardX = cardMargin;
            bool multiGpu = _hardware.Gpu.GpuCount > 1;

            // ================= HEADER =================
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 38,
                BackColor = Color.FromArgb(18, 18, 24),
                Cursor = Cursors.SizeAll
            };
            header.MouseDown += OnHeaderDrag;

            var picIcon = new PictureBox
            {
                Location = new Point(12, 9),
                Size = new Size(20, 20),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = AppIcons.QuietBitmap,
                Cursor = Cursors.SizeAll
            };
            picIcon.MouseDown += OnHeaderDrag;
            header.Controls.Add(picIcon);

            _lblHeaderTitle = new Label
            {
                Text = Loc.Get("MenuSettings").TrimEnd(':'),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 245),
                Location = new Point(38, 8),
                AutoSize = true,
                Cursor = Cursors.SizeAll
            };
            _lblHeaderTitle.MouseDown += OnHeaderDrag;
            header.Controls.Add(_lblHeaderTitle);

            var btnX = new Label
            {
                Text = "✕",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(160, 160, 175),
                Size = new Size(28, 28),
                Location = new Point(w - 34, 5),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            btnX.MouseEnter += (s, e) => btnX.ForeColor = Color.White;
            btnX.MouseLeave += (s, e) => btnX.ForeColor = Color.FromArgb(160, 160, 175);
            btnX.Click += (s, e) => this.Close();
            header.Controls.Add(btnX);

            // ================= FOOTER BUTTONS =================
            var pnlFooter = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                BackColor = Color.FromArgb(18, 18, 24)
            };

            int fBtnW = 36;
            int fBtnGap = 6;

            _btnSpecs = new Button
            {
                Text = "ℹ",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 215),
                BackColor = Color.FromArgb(32, 32, 40),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(fBtnW, 32),
                Location = new Point(cardX, 6),
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = Padding.Empty,
                Cursor = Cursors.Hand
            };
            _btnSpecs.FlatAppearance.BorderColor = Color.FromArgb(50, 50, 65);
            _toolTip.SetToolTip(_btnSpecs, Loc.Get("TipInfo"));
            _btnSpecs.Click += (s, e) => OpenSpecsDialog();
            pnlFooter.Controls.Add(_btnSpecs);

            _btnTools = new Button
            {
                Text = "🔗",
                Font = new Font("Segoe UI", 11f),
                ForeColor = Color.FromArgb(160, 160, 180),
                BackColor = Color.FromArgb(32, 32, 40),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(fBtnW, 32),
                Location = new Point(cardX + (fBtnW + fBtnGap), 6),
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = Padding.Empty,
                Cursor = Cursors.Hand
            };
            _btnTools.FlatAppearance.BorderColor = Color.FromArgb(50, 50, 65);
            _toolTip.SetToolTip(_btnTools, "https://ph-cu-s.com/tools");
            _btnTools.Click += (s, e) => OpenToolsUrl();
            pnlFooter.Controls.Add(_btnTools);

            _btnExpandAll = new Button
            {
                Text = "▼",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 215),
                BackColor = Color.FromArgb(32, 32, 40),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(fBtnW, 32),
                Location = new Point(cardX + (fBtnW + fBtnGap) * 2, 6),
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = Padding.Empty,
                Cursor = Cursors.Hand
            };
            _btnExpandAll.FlatAppearance.BorderColor = Color.FromArgb(50, 50, 65);
            _toolTip.SetToolTip(_btnExpandAll, Loc.Get("TipSettingsExpandAll"));
            _btnExpandAll.Click += (s, e) => SetAllSectionsCollapsed(false);
            pnlFooter.Controls.Add(_btnExpandAll);

            _btnCollapseAll = new Button
            {
                Text = "▲",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 215),
                BackColor = Color.FromArgb(32, 32, 40),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(fBtnW, 32),
                Location = new Point(cardX + (fBtnW + fBtnGap) * 3, 6),
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = Padding.Empty,
                Cursor = Cursors.Hand
            };
            _btnCollapseAll.FlatAppearance.BorderColor = Color.FromArgb(50, 50, 65);
            _toolTip.SetToolTip(_btnCollapseAll, Loc.Get("TipSettingsCollapseAll"));
            _btnCollapseAll.Click += (s, e) => SetAllSectionsCollapsed(true);
            pnlFooter.Controls.Add(_btnCollapseAll);

            int closeW = 90;
            _btnClose = new Button
            {
                Text = Loc.Get("InfoClose"),
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(14, 165, 233), // Sky Blue Accent
                FlatStyle = FlatStyle.Flat,
                Size = new Size(closeW, 32),
                Location = new Point(cardX + cardW - closeW, 6),
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = Padding.Empty,
                Cursor = Cursors.Hand
            };
            _btnClose.FlatAppearance.BorderSize = 0;
            _btnClose.Click += (s, e) => this.Close();
            pnlFooter.Controls.Add(_btnClose);

            // Footer bottom edge resizing & corner grip
            pnlFooter.MouseMove += (s, e) => {
                if (e.Y >= pnlFooter.Height - 8)
                {
                    pnlFooter.Cursor = (e.X >= pnlFooter.Width - 24) ? Cursors.SizeNWSE :
                                       (e.X <= 8) ? Cursors.SizeNESW : Cursors.SizeNS;
                }
                else
                {
                    pnlFooter.Cursor = Cursors.Default;
                }
            };
            pnlFooter.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Left && e.Y >= pnlFooter.Height - 8)
                {
                    int hit = (e.X >= pnlFooter.Width - 24) ? HTBOTTOMRIGHT :
                              (e.X <= 8) ? HTBOTTOMLEFT : HTBOTTOM;
                    ReleaseCapture();
                    SendMessage(this.Handle, WM_NCLBUTTONDOWN, hit, 0);
                }
            };

            _lblResizeGrip = new Label
            {
                Text = "⤡",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(140, 150, 175),
                Size = new Size(20, 20),
                TextAlign = ContentAlignment.BottomRight,
                Cursor = Cursors.SizeNWSE,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Location = new Point(w - 22, 44 - 22)
            };
            _lblResizeGrip.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Left)
                {
                    ReleaseCapture();
                    SendMessage(this.Handle, WM_NCLBUTTONDOWN, HTBOTTOMRIGHT, 0);
                }
            };
            pnlFooter.Controls.Add(_lblResizeGrip);

            // ================= SCROLLABLE BODY =================
            _pnlBody = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.Transparent
            };
            _pnlBody.HorizontalScroll.Maximum = 0;
            _pnlBody.HorizontalScroll.Visible = false;

            this.Controls.Add(_pnlBody);
            this.Controls.Add(pnlFooter);
            this.Controls.Add(header);
            _pnlBody.BringToFront();

            int curY = 10;

            // ================= 1. LANGUAGE CARD =================
            // 8 clean 2-letter buttons in 1 row: RU, EN, DE, ES, FR, JA, PT, ZH
            var cardLang = CreateCard(cardX, curY, cardW, 70);
            _pnlBody.Controls.Add(cardLang);

            _lblSecLang = CreateSectionHeader(Loc.Get("Language"), 12, 8);
            cardLang.Controls.Add(_lblSecLang);
            RegisterCardSection("lang", cardLang, _lblSecLang, () => Loc.Get("Language"), 70);

            int btnLangX = 13;
            int btnLangW = 42;
            int btnLangH = 28;
            int btnLangY = 32;

            foreach (var lang in Loc.SupportedLanguages)
            {
                var btn = new Button
                {
                    Text = lang.Code.ToUpperInvariant(),
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                    Size = new Size(btnLangW, btnLangH),
                    Location = new Point(btnLangX, btnLangY),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand,
                    Tag = lang.Code
                };
                btn.FlatAppearance.BorderSize = 0;
                _toolTip.SetToolTip(btn, lang.Name);
                btn.Click += (s, e) => SelectLanguage(lang.Code);
                cardLang.Controls.Add(btn);
                _langButtons.Add(btn);

                btnLangX += btnLangW + 4;
            }
            UpdateLangButtonsHighlight();

            curY += 78;

            // ================= 2. GRAPHS & MODULES CARD =================
            // Single vertical column (1 row per item) - spacious and slender
            int baseOff = 56;
            int cardGraphsH = multiGpu ? 552 + baseOff + 26 : 522 + baseOff + 26;
            var cardGraphs = CreateCard(cardX, curY, cardW, cardGraphsH);
            _pnlBody.Controls.Add(cardGraphs);

            _lblSecGraphs = CreateSectionHeader(Loc.Get("MenuGraphs"), 12, 8);
            cardGraphs.Controls.Add(_lblSecGraphs);
            RegisterCardSection("graphs", cardGraphs, _lblSecGraphs, () => Loc.Get("MenuGraphs"), cardGraphsH);

            int chkX = 14;
            int chkY = 32;
            int chkGap = 26;

            _chkStopwatch = CreateCheckbox(Loc.Get("StopwatchModule"), chkX, chkY, _settings.ShowStopwatch, v => {
                _settings.ShowStopwatch = v;
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            });
            cardGraphs.Controls.Add(_chkStopwatch);

            _chkCpuGraph = CreateCheckbox(Loc.Get("CpuGraph"), chkX, chkY + (chkGap * 1), _settings.ShowCpuGraph, v => {
                _settings.ShowCpuGraph = v;
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            });
            cardGraphs.Controls.Add(_chkCpuGraph);

            // CPU Scale: 2 lines for compact layout
            // Line 1: Title
            int scaleRowY = chkY + (chkGap * 1) + 24;
            _lblCpuScaleTitle = new Label
            {
                Text = Loc.Get("CpuGraphScaleTitle"),
                Font = new Font("Segoe UI", 8.0f, FontStyle.Bold),
                ForeColor = Color.FromArgb(170, 170, 185),
                Location = new Point(chkX + 16, scaleRowY + 2),
                AutoSize = true
            };
            cardGraphs.Controls.Add(_lblCpuScaleTitle);

            (string label, int val)[] scaleOptions = new[]
            {
                ("1x", 1),
                ("2x", 2),
                ("4x", 4),
                ("6x", 6),
                ("8x", 8),
                (Loc.Get("ScaleAuto"), 0)
            };

            // Line 2: Scale buttons spread evenly across available width
            int scaleBtnsY = scaleRowY + 22;
            int scStartX = chkX + 16;
            int scAvailW = cardW - 28 - 16;
            int scBtnGap = 4;
            int scBtnW = Math.Max(24, (scAvailW - (scaleOptions.Length - 1) * scBtnGap) / scaleOptions.Length);
            for (int i = 0; i < scaleOptions.Length; i++)
            {
                var opt = scaleOptions[i];
                int btnW = (i == scaleOptions.Length - 1) ? (scAvailW - i * (scBtnW + scBtnGap)) : scBtnW;
                var btn = new Button
                {
                    Text = opt.label,
                    Font = new Font("Segoe UI", 8.0f, FontStyle.Bold),
                    Size = new Size(btnW, 24),
                    Location = new Point(scStartX + i * (scBtnW + scBtnGap), scaleBtnsY),
                    FlatStyle = FlatStyle.Flat,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Padding = Padding.Empty,
                    Cursor = Cursors.Hand,
                    Tag = opt.val
                };
                btn.FlatAppearance.BorderSize = 1;
                btn.Click += (s, e) => {
                    _settings.CpuGraphScale = (int)((Button)s!).Tag!;
                    _settings.Save();
                    UpdateCpuScaleButtonsHighlight();
                    _onSettingsUpdated?.Invoke();
                };
                cardGraphs.Controls.Add(btn);
                _cpuScaleButtons.Add(btn);
            }
            UpdateCpuScaleButtonsHighlight();

            _chkCpuCores = CreateCheckbox(Loc.Get("CpuCores"), chkX, chkY + (chkGap * 2) + baseOff, _settings.ShowCpuCores, v => _settings.ShowCpuCores = v);
            _chkTopProcesses = CreateCheckbox(Loc.Get("TopProcesses"), chkX, chkY + (chkGap * 3) + baseOff, _settings.ShowTopProcesses, v => {
                _settings.ShowTopProcesses = v;
                _hardware.Processes.IsEnabled = v;
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            });
            _chkRamGraph = CreateCheckbox(Loc.Get("RamGraph"), chkX, chkY + (chkGap * 4) + baseOff, _settings.ShowRamGraph, v => _settings.ShowRamGraph = v);
            _chkGpuGraph = CreateCheckbox(Loc.Get("GpuGraph"), chkX, chkY + (chkGap * 5) + baseOff, _settings.ShowGpuGraph, v => _settings.ShowGpuGraph = v);
            _chkGpuTemp = CreateCheckbox(Loc.Get("GpuTemp"), chkX, chkY + (chkGap * 6) + baseOff, _settings.ShowGpuTempLine, v => _settings.ShowGpuTempLine = v);
            _chkGpuFan = CreateCheckbox(Loc.Get("GpuFan"), chkX, chkY + (chkGap * 7) + baseOff, _settings.ShowGpuFanSpeed, v => _settings.ShowGpuFanSpeed = v);
            _chkVramGraph = CreateCheckbox(Loc.Get("VramGraph"), chkX, chkY + (chkGap * 8) + baseOff, _settings.ShowVramGraph, v => {
                _settings.ShowVramGraph = v;
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            });
            _chkTopVramProcesses = CreateCheckbox(Loc.Get("VramProcesses"), chkX + 16, chkY + (chkGap * 9) + baseOff, _settings.ShowTopVramProcesses, v => {
                _settings.ShowTopVramProcesses = v;
                _hardware.Processes.IsVramMonitoringEnabled = v;
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            });
            _chkDiskGraph = CreateCheckbox(Loc.Get("DiskGraph"), chkX, chkY + (chkGap * 10) + baseOff, _settings.ShowDiskGraph, v => {
                _settings.ShowDiskGraph = v;
                _chkEnhancedDiskMode.Enabled = v;
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            });
            _chkEnhancedDiskMode = CreateCheckbox(Loc.Get("EnhancedDiskMode"), chkX + 16, chkY + (chkGap * 11) + baseOff, _settings.EnhancedDiskMode, v => {
                _settings.EnhancedDiskMode = v;
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            });
            _chkEnhancedDiskMode.Enabled = _settings.ShowDiskGraph;

            // Tier 1: Checkbox on left + Active/Start status button on right + Folder button
            int fanY = chkY + (chkGap * 12) + baseOff;
            _chkFanGraph = CreateCheckbox(Loc.Get("FanGraph"), chkX, fanY + 3, _settings.ShowFanGraph, v => {
                _settings.ShowFanGraph = v;
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            });
            _chkFanGraph.AutoSize = false;
            _chkFanGraph.Size = new Size(cardW - 146, 24);

            _lblFanStatus = new Label
            {
                Visible = false,
                Size = Size.Empty
            };

            _btnFanToggle = new Button
            {
                Text = Loc.Get("FanPlugin_Start"),
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = Color.FromArgb(160, 168, 185),
                BackColor = Color.FromArgb(28, 30, 38),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(106, 30),
                Location = new Point(cardW - 138, fanY - 1),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };
            _btnFanToggle.FlatAppearance.BorderSize = 1;
            _btnFanToggle.FlatAppearance.BorderColor = Color.FromArgb(60, 68, 85);
            _btnFanToggle.Click += (s, e) => ToggleFanService();
            _btnFanToggle.MouseEnter += (s, e) => {
                if (_hardware.Fans.IsRunning && _hardware.Fans.Status == FanPluginStatus.Connected)
                {
                    string stopText = Loc.Get("FanPlugin_Stop");
                    if (!stopText.StartsWith("⏹") && !stopText.StartsWith("⏸")) stopText = "⏹ " + stopText;
                    _btnFanToggle.Text = stopText;
                    _btnFanToggle.ForeColor = Color.FromArgb(248, 113, 113);
                    _btnFanToggle.BackColor = Color.FromArgb(42, 22, 26);
                    _btnFanToggle.FlatAppearance.BorderColor = Color.FromArgb(120, 45, 55);
                }
                else if (_hardware.Fans.Status != FanPluginStatus.Connecting)
                {
                    _btnFanToggle.ForeColor = Color.FromArgb(220, 225, 240);
                    _btnFanToggle.FlatAppearance.BorderColor = Color.FromArgb(56, 189, 248);
                }
            };
            _btnFanToggle.MouseLeave += (s, e) => {
                UpdateFanStatusUI();
            };

            _btnFanFolder = new Button
            {
                Text = "📁",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(170, 170, 185),
                BackColor = Color.FromArgb(32, 32, 42),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(28, 30),
                Location = new Point(cardW - 30, fanY - 1),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };
            _btnFanFolder.FlatAppearance.BorderSize = 0;
            _toolTip.SetToolTip(_btnFanFolder, Loc.Get("FanPluginFolder"));
            _btnFanFolder.Click += (s, e) => FanMonitorClient.OpenPluginFolder();

            // Tier 2: 3 segmented mode buttons [ 📈 График ] [ 🌀 В 1 ряд ] [ ▦ Сетка ]
            int fanModeY = fanY + 36;
            int totalBtnSpace = cardW - 28;
            int btnGap = 5;
            int modeBtnW = (totalBtnSpace - (btnGap * 2)) / 3;
            int modeBtnH = 32;

            _btnFanModeGraph = new Button
            {
                Text = Loc.Get("FanVisualMode_Graph"),
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Size = new Size(modeBtnW, modeBtnH),
                Location = new Point(14, fanModeY),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };
            _btnFanModeGraph.FlatAppearance.BorderSize = 1;
            _btnFanModeGraph.Click += (s, e) => {
                _settings.FanVisualMode = 0;
                UpdateFanModeUI();
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            };

            _btnFanModeIcons = new Button
            {
                Text = Loc.Get("FanVisualMode_Icons"),
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Size = new Size(modeBtnW, modeBtnH),
                Location = new Point(14 + modeBtnW + btnGap, fanModeY),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };
            _btnFanModeIcons.FlatAppearance.BorderSize = 1;
            _btnFanModeIcons.Click += (s, e) => {
                _settings.FanVisualMode = 1;
                UpdateFanModeUI();
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            };

            _btnFanModeGrid = new Button
            {
                Text = Loc.Get("FanVisualMode_Grid"),
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Size = new Size(totalBtnSpace - ((modeBtnW + btnGap) * 2), modeBtnH),
                Location = new Point(14 + (modeBtnW + btnGap) * 2, fanModeY),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };
            _btnFanModeGrid.FlatAppearance.BorderSize = 1;
            _btnFanModeGrid.Click += (s, e) => {
                _settings.FanVisualMode = 2;
                UpdateFanModeUI();
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            };

            // Tier 3: [ ⚙ Выбор ] + [✓] Демо
            int fanOptY = fanModeY + 38;
            _btnFanSelect = new Button
            {
                Text = Loc.Get("FanSelectFans"),
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                BackColor = Color.FromArgb(28, 36, 48),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(130, 30),
                Location = new Point(14, fanOptY),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };
            _btnFanSelect.FlatAppearance.BorderSize = 1;
            _btnFanSelect.FlatAppearance.BorderColor = Color.FromArgb(45, 75, 100);
            _toolTip.SetToolTip(_btnFanSelect, Loc.Get("FanSelectionTitle"));
            _btnFanSelect.Click += (s, e) => OpenFanSelectionDialog();

            _chkFanDemo = CreateCheckbox(Loc.Get("FanDemoMode"), 154, fanOptY + 3, _settings.EnableFanDemo, v => {
                _settings.EnableFanDemo = v;
                _hardware.Fans.DemoMode = v;
                _settings.Save();
                UpdateFanStatusUI();
                _onSettingsUpdated?.Invoke();
            });
            _chkFanDemo.Font = new Font("Segoe UI", 8.25f);
            _chkFanDemo.Size = new Size(110, 24);
            _toolTip.SetToolTip(_chkFanDemo, Loc.Get("FanDemoModeTooltip"));

            // Tier 4: Fan Spinner Scale Slider [ Размер: 100% ] [--------O--------]
            int fanScaleY = fanOptY + 36;
            _lblFanScale = new Label
            {
                Text = $"{Loc.Get("FanScale")}: {_settings.FanScalePercent}%",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 205, 220),
                Location = new Point(14, fanScaleY + 2),
                Size = new Size(130, 22),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _tbFanScale = new TrackBar
            {
                AutoSize = false,
                Location = new Point(148, fanScaleY),
                Size = new Size(cardW - 148 - 14, 24),
                Minimum = 100,
                Maximum = 200,
                TickStyle = TickStyle.None,
                SmallChange = 5,
                LargeChange = 25,
                Value = Math.Clamp(_settings.FanScalePercent, 100, 200),
                Cursor = Cursors.Hand
            };
            _tbFanScale.ValueChanged += (s, e) => {
                _settings.FanScalePercent = _tbFanScale.Value;
                _lblFanScale.Text = $"{Loc.Get("FanScale")}: {_tbFanScale.Value}%";
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            };
            _toolTip.SetToolTip(_tbFanScale, Loc.Get("FanScaleTip"));
            _toolTip.SetToolTip(_lblFanScale, Loc.Get("FanScaleTip"));

            cardGraphs.Controls.AddRange(new Control[] {
                _chkCpuGraph, _chkCpuCores, _chkTopProcesses,
                _chkRamGraph, _chkGpuGraph, _chkGpuTemp, _chkGpuFan,
                _chkVramGraph, _chkTopVramProcesses, _chkDiskGraph, _chkEnhancedDiskMode, _chkFanGraph,
                _lblFanStatus, _btnFanToggle, _btnFanFolder,
                _btnFanModeGraph, _btnFanModeIcons, _btnFanModeGrid,
                _btnFanSelect, _chkFanDemo,
                _lblFanScale, _tbFanScale
            });
            UpdateFanModeUI();

            int nextY = fanScaleY + 44;

            if (multiGpu)
            {
                _chkShowAllGpus = CreateCheckbox(Loc.Get("ShowAllGpus"), chkX, nextY, _settings.ShowAllGpus, v => _settings.ShowAllGpus = v);
                cardGraphs.Controls.Add(_chkShowAllGpus);
                nextY += chkGap;
            }

            _chkTranslateModule = CreateCheckbox(Loc.Get("Translate_Module"), chkX, nextY, _settings.ShowTranslateModule, v => {
                _settings.ShowTranslateModule = v;
                if (_chkTranslateEnable != null) _chkTranslateEnable.Checked = v;
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            });
            cardGraphs.Controls.Add(_chkTranslateModule);
            nextY += chkGap;

            // Reset/All button
            _btnResetGraphs = new Button
            {
                Text = "⚡ " + Loc.Get("ShowAllGraphs"),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                BackColor = Color.FromArgb(34, 34, 44),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(cardW - 28, 34),
                Location = new Point(14, nextY + 2),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _btnResetGraphs.FlatAppearance.BorderColor = Color.FromArgb(52, 52, 68);
            _btnResetGraphs.Click += (s, e) => {
                _chkStopwatch.Checked = true;
                _chkCpuGraph.Checked = true;
                _chkCpuCores.Checked = true;
                _chkTopProcesses.Checked = true;
                _chkRamGraph.Checked = true;
                _chkDiskGraph.Checked = true;
                _chkGpuGraph.Checked = true;
                _chkGpuTemp.Checked = true;
                _chkGpuFan.Checked = true;
                _chkVramGraph.Checked = true;
                _chkFanGraph.Checked = true;
                _chkTranslateModule.Checked = true;
                if (_chkShowAllGpus != null) _chkShowAllGpus.Checked = true;
                _settings.ShowCpuGraph = true;
                _settings.ShowCpuCores = true;
                _settings.ShowTopProcesses = true;
                _hardware.Processes.IsEnabled = true;
                _settings.ShowRamGraph = true;
                _settings.ShowDiskGraph = true;
                _settings.ShowGpuGraph = true;
                _settings.ShowGpuTempLine = true;
                _settings.ShowGpuFanSpeed = true;
                _settings.ShowVramGraph = true;
                _settings.ShowFanGraph = true;
                _settings.ShowTranslateModule = true;
                _settings.EnableFanAddon = true;
                _settings.FanVisualMode = 0;
                _settings.FanScalePercent = 100;
                if (_tbFanScale != null) _tbFanScale.Value = 100;
                if (_lblFanScale != null) _lblFanScale.Text = $"{Loc.Get("FanScale")}: 100%";
                UpdateFanModeUI();
                _hardware.Fans.SetEnabled(true);
                UpdateFanStatusUI();
                if (multiGpu) _settings.ShowAllGpus = true;
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            };
            cardGraphs.Controls.Add(_btnResetGraphs);
            _btnResetGraphs.BringToFront();

            curY += cardGraphsH + 8;

            // ================= 2b. STOPWATCH & AUTO-START CARD =================
            int cardSwH = 450;
            var cardSw = CreateCard(cardX, curY, cardW, cardSwH);
            _pnlBody.Controls.Add(cardSw);

            _lblSecStopwatch = CreateSectionHeader("⏱ " + Loc.Get("StopwatchSettings"), 12, 8);
            cardSw.Controls.Add(_lblSecStopwatch);
            RegisterCardSection("sw", cardSw, _lblSecStopwatch, () => "⏱ " + Loc.Get("StopwatchSettings"), cardSwH);

            int swY = 32;
            _chkStopwatchAuto = CreateCheckbox(Loc.Get("StopwatchAutoStartEnable"), 14, swY, _settings.StopwatchAutoStartEnabled, v => {
                _settings.StopwatchAutoStartEnabled = v;
                _hardware.Stopwatch.IsAutoArmed = v;
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            });
            cardSw.Controls.Add(_chkStopwatchAuto);

            // Source / Criterion Selector Title
            int srcY = swY + 26;
            _lblSwSourceTitle = new Label
            {
                Text = Loc.Get("StopwatchTriggerSource"),
                Font = new Font("Segoe UI", 8.0f, FontStyle.Bold),
                ForeColor = Color.FromArgb(170, 170, 185),
                Location = new Point(14, srcY),
                AutoSize = true
            };
            cardSw.Controls.Add(_lblSwSourceTitle);

            (string label, int srcVal)[] sources = new[]
            {
                ("CPU", 1),
                ("GPU", 2),
                ("CPU + GPU", 3),
                (Loc.Get("StopwatchSourceMax"), 0)
            };

            int srcBtnX = 14;
            int srcBtnW = (cardW - 28 - (sources.Length - 1) * 4) / sources.Length;
            int srcBtnH = 26;
            int srcBtnY = srcY + 18;

            foreach (var s in sources)
            {
                var btn = new Button
                {
                    Text = s.label,
                    Font = new Font("Segoe UI", 8.0f, FontStyle.Bold),
                    Size = new Size(srcBtnW, srcBtnH),
                    Location = new Point(srcBtnX, srcBtnY),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand,
                    Tag = s.srcVal,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Padding = Padding.Empty
                };
                btn.FlatAppearance.BorderSize = 0;
                int targetSrc = s.srcVal;
                btn.Click += (sender, e) => {
                    _settings.StopwatchTriggerSource = targetSrc;
                    UpdateSwSourceButtonsHighlight();
                    _settings.Save();
                    _onSettingsUpdated?.Invoke();
                };
                cardSw.Controls.Add(btn);
                _swSourceButtons.Add(btn);
                srcBtnX += srcBtnW + 4;
            }

            int descY = srcBtnY + srcBtnH + 6;
            _lblSwSourceDesc = new Label
            {
                Font = new Font("Segoe UI", 7.75f, FontStyle.Italic),
                ForeColor = Color.FromArgb(56, 189, 248),
                Location = new Point(14, descY),
                Size = new Size(cardW - 28, 18),
                AutoEllipsis = true
            };
            cardSw.Controls.Add(_lblSwSourceDesc);
            UpdateSwSourceButtonsHighlight();

            // Auto-start threshold slider
            int swSlY = descY + 22;
            _lblSwStartWatts = new Label
            {
                Text = $"{Loc.Get("StopwatchAutoStartWatts")}: {_settings.StopwatchAutoStartWatts}W",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 245),
                Location = new Point(14, swSlY),
                AutoSize = true
            };
            cardSw.Controls.Add(_lblSwStartWatts);

            _tbSwStartWatts = new TrackBar
            {
                AutoSize = false,
                Minimum = 10,
                Maximum = 350,
                Value = Math.Clamp(_settings.StopwatchAutoStartWatts, 10, 350),
                TickFrequency = 20,
                SmallChange = 5,
                LargeChange = 25,
                Size = new Size(cardW - 28, 22),
                Location = new Point(14, swSlY + 18),
                Cursor = Cursors.Hand
            };
            _tbSwStartWatts.ValueChanged += (s, e) => {
                _settings.StopwatchAutoStartWatts = _tbSwStartWatts.Value;
                _lblSwStartWatts.Text = $"{Loc.Get("StopwatchAutoStartWatts")}: {_tbSwStartWatts.Value}W";
                UpdateSwSourceButtonsHighlight();
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            };
            cardSw.Controls.Add(_tbSwStartWatts);

            // Auto-stop threshold slider
            int swStpY = swSlY + 44;
            _lblSwStopWatts = new Label
            {
                Text = $"{Loc.Get("StopwatchAutoStopWatts")}: {_settings.StopwatchAutoStopWatts}W",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 245),
                Location = new Point(14, swStpY),
                AutoSize = true
            };
            cardSw.Controls.Add(_lblSwStopWatts);

            _tbSwStopWatts = new TrackBar
            {
                AutoSize = false,
                Minimum = 5,
                Maximum = 300,
                Value = Math.Clamp(_settings.StopwatchAutoStopWatts, 5, 300),
                TickFrequency = 15,
                SmallChange = 5,
                LargeChange = 20,
                Size = new Size(cardW - 28, 22),
                Location = new Point(14, swStpY + 18),
                Cursor = Cursors.Hand
            };
            _tbSwStopWatts.ValueChanged += (s, e) => {
                _settings.StopwatchAutoStopWatts = _tbSwStopWatts.Value;
                _lblSwStopWatts.Text = $"{Loc.Get("StopwatchAutoStopWatts")}: {_tbSwStopWatts.Value}W";
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            };
            cardSw.Controls.Add(_tbSwStopWatts);

            // Hysteresis slider
            int swHystY = swStpY + 44;
            _lblSwHysteresis = new Label
            {
                Text = $"{Loc.Get("StopwatchHysteresis")}: {_settings.StopwatchHysteresisSeconds} {Loc.Get("Sec")}",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 245),
                Location = new Point(14, swHystY),
                AutoSize = true
            };
            _toolTip.SetToolTip(_lblSwHysteresis, Loc.Get("StopwatchHysteresisTip"));
            cardSw.Controls.Add(_lblSwHysteresis);

            _tbSwHysteresis = new TrackBar
            {
                AutoSize = false,
                Minimum = 1,
                Maximum = 15,
                Value = Math.Clamp(_settings.StopwatchHysteresisSeconds, 1, 15),
                TickFrequency = 1,
                SmallChange = 1,
                LargeChange = 2,
                Size = new Size(cardW - 28, 22),
                Location = new Point(14, swHystY + 18),
                Cursor = Cursors.Hand
            };
            _tbSwHysteresis.ValueChanged += (s, e) => {
                _settings.StopwatchHysteresisSeconds = _tbSwHysteresis.Value;
                _lblSwHysteresis.Text = $"{Loc.Get("StopwatchHysteresis")}: {_tbSwHysteresis.Value} {Loc.Get("Sec")}";
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            };
            _toolTip.SetToolTip(_tbSwHysteresis, Loc.Get("StopwatchHysteresisTip"));
            cardSw.Controls.Add(_tbSwHysteresis);

            // Stopwatch Profiles Management Section
            int swListY = swHystY + 46;
            _lblSwListTitle = CreateSectionHeader("⏱ " + Loc.Get("StopwatchListTitle"), 12, swListY);
            _lblSwListTitle.MaximumSize = new Size(cardW - 14 - 125, 24);
            _lblSwListTitle.AutoEllipsis = true;
            cardSw.Controls.Add(_lblSwListTitle);

            _btnSwAdd = new Button
            {
                Text = Loc.Get("StopwatchAdd"),
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Size = new Size(120, 28),
                Location = new Point(cardW - 14 - 120, swListY - 4),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(34, 197, 94),
                ForeColor = Color.FromArgb(18, 18, 22),
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = Padding.Empty,
                Cursor = Cursors.Hand
            };
            _btnSwAdd.FlatAppearance.BorderSize = 0;
            _btnSwAdd.Click += (s, e) => {
                using var dlg = new StopwatchEditDialog(null, _hardware.Processes)
                {
                    TopMost = this.TopMost
                };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _settings.Stopwatches.Add(dlg.Profile);
                    _settings.Save();
                    _hardware.Stopwatch.SyncWithSettings(_settings);
                    PopulateStopwatchList(_pnlSwListContainer, cardW - 28);
                    _onSettingsUpdated?.Invoke();
                }
            };
            cardSw.Controls.Add(_btnSwAdd);

            _pnlSwListContainer = new Panel
            {
                Location = new Point(14, swListY + 28),
                Size = new Size(cardW - 28, 125),
                AutoScroll = true,
                BackColor = Color.Transparent
            };
            _pnlSwListContainer.HorizontalScroll.Enabled = false;
            _pnlSwListContainer.HorizontalScroll.Visible = false;
            cardSw.Controls.Add(_pnlSwListContainer);
            PopulateStopwatchList(_pnlSwListContainer, cardW - 28);

            curY += cardSwH + 8;


            // ================= 2c. GPU ACOUSTIC & POWER TUNING CARD =================
            int cardGpuH = 312;
            var cardGpu = CreateCard(cardX, curY, cardW, cardGpuH);
            _pnlBody.Controls.Add(cardGpu);

            _lblSecGpuTuning = CreateSectionHeader("⚡ " + Loc.Get("GpuTuningTitle"), 12, 8);
            cardGpu.Controls.Add(_lblSecGpuTuning);
            RegisterCardSection("gpu", cardGpu, _lblSecGpuTuning, () => "⚡ " + Loc.Get("GpuTuningTitle"), cardGpuH);

            string gpuModel = _hardware.Gpu.GpuName;
            if (!string.IsNullOrEmpty(gpuModel))
            {
                var lblGpuBadge = new Label
                {
                    Text = $"{gpuModel} (Stock: {_hardware.GpuTuning.StockWatts}W)",
                    Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(168, 85, 247),
                    Location = new Point(12, 28),
                    AutoSize = true
                };
                cardGpu.Controls.Add(lblGpuBadge);
            }

            int gpuY = 48;
            _chkGpuTuning = CreateCheckbox(Loc.Get("GpuTuningEnable"), 14, gpuY, _settings.GpuTuningEnabled, v => {
                _settings.GpuTuningEnabled = v;
                _hardware.Fans.SetEnabled((_settings.ShowFanGraph && _settings.EnableFanAddon) || v);
                _hardware.GpuTuning.ApplyMode(_settings.GpuIsQuietMode);
                UpdateGpuTuningCardUI();
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            });
            cardGpu.Controls.Add(_chkGpuTuning);

            _chkSeparateCpuGpu = CreateCheckbox(Loc.Get("SeparateCpuGpu"), 14, gpuY + 24, _settings.SeparateCpuGpuControl, v => {
                _settings.SeparateCpuGpuControl = v;
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            });
            cardGpu.Controls.Add(_chkSeparateCpuGpu);

            // Divider
            var sepGpu1 = new Panel
            {
                Location = new Point(12, gpuY + 50),
                Size = new Size(cardW - 24, 1),
                BackColor = Color.FromArgb(40, 40, 52)
            };
            cardGpu.Controls.Add(sepGpu1);
            _cardDividers.Add(sepGpu1);

            // Power Limit Section
            int pwrY = gpuY + 56;
            _lblGpuPowerLimit = new Label
            {
                Text = $"{Loc.Get("GpuPowerLimit")}: {_settings.GpuSilentPowerWatts}W",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 245),
                Location = new Point(12, pwrY),
                AutoSize = true
            };
            _toolTip.SetToolTip(_lblGpuPowerLimit, Loc.Get("GpuPowerLimitTip"));
            cardGpu.Controls.Add(_lblGpuPowerLimit);

            int minW = Math.Min(100, _hardware.GpuTuning.MinSupportedWatts);
            int maxW = Math.Max(350, _hardware.GpuTuning.MaxSupportedWatts);

            _tbGpuPowerLimit = new TrackBar
            {
                AutoSize = false,
                Minimum = minW,
                Maximum = maxW,
                Value = Math.Clamp(_settings.GpuSilentPowerWatts, minW, maxW),
                TickFrequency = 20,
                SmallChange = 5,
                LargeChange = 20,
                Size = new Size(cardW - 24, 22),
                Location = new Point(12, pwrY + 20),
                Cursor = Cursors.Hand
            };
            _tbGpuPowerLimit.ValueChanged += (s, e) => {
                _settings.GpuSilentPowerWatts = _tbGpuPowerLimit.Value;
                _lblGpuPowerLimit.Text = $"{Loc.Get("GpuPowerLimit")}: {_tbGpuPowerLimit.Value}W";
                if (_settings.GpuIsQuietMode && _settings.GpuTuningEnabled)
                {
                    _hardware.GpuTuning.ApplyPowerLimit(_settings.GpuSilentPowerWatts);
                }
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            };
            _toolTip.SetToolTip(_tbGpuPowerLimit, Loc.Get("GpuPowerLimitTip"));
            cardGpu.Controls.Add(_tbGpuPowerLimit);

            // Power Presets
            int stockWatts = _hardware.GpuTuning.StockWatts > 0 ? _hardware.GpuTuning.StockWatts : 336;
            (string key, int w)[] powerPresets = new[]
            {
                ("GpuPresetEco", 180),
                ("GpuPresetQuiet", 240),
                ("GpuPresetBalanced", 280),
                ("GpuPresetStock", stockWatts)
            };
            int pwBtnX = 12;
            int pwBtnW = (cardW - 24 - (powerPresets.Length - 1) * 4) / powerPresets.Length;
            foreach (var p in powerPresets)
            {
                var btn = new Button
                {
                    Text = $"{Loc.Get(p.key)} ({p.w}W)",
                    Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                    Size = new Size(pwBtnW, 24),
                    Location = new Point(pwBtnX, pwrY + 46),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(32, 32, 40),
                    ForeColor = Color.FromArgb(170, 170, 185),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Padding = Padding.Empty,
                    Cursor = Cursors.Hand,
                    Tag = p.w
                };
                btn.FlatAppearance.BorderSize = 0;
                int targetW = p.w;
                btn.Click += (s, e) => {
                    _tbGpuPowerLimit.Value = Math.Clamp(targetW, _tbGpuPowerLimit.Minimum, _tbGpuPowerLimit.Maximum);
                };
                cardGpu.Controls.Add(btn);
                _gpuPowerPresetButtons.Add(btn);
                pwBtnX += pwBtnW + 4;
            }

            // Divider
            int fanDivY = pwrY + 76;
            var sepGpu2 = new Panel
            {
                Location = new Point(12, fanDivY),
                Size = new Size(cardW - 24, 1),
                BackColor = Color.FromArgb(40, 40, 52)
            };
            cardGpu.Controls.Add(sepGpu2);
            _cardDividers.Add(sepGpu2);

            // Fan Acoustic Cap Section
            int fanCapY = fanDivY + 6;
            _chkGpuFanCap = CreateCheckbox(Loc.Get("GpuFanCapEnable"), 14, fanCapY, _settings.GpuFanCapEnabled, v => {
                _settings.GpuFanCapEnabled = v;
                if (v && _settings.GpuIsQuietMode && _settings.GpuTuningEnabled)
                {
                    _hardware.GpuTuning.ApplyFanCap(_settings.GpuFanMaxPercent);
                }
                else
                {
                    _hardware.GpuTuning.ReleaseFanCap();
                }
                UpdateGpuTuningCardUI();
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            });
            cardGpu.Controls.Add(_chkGpuFanCap);

            _lblGpuFanCap = new Label
            {
                Text = $"{_settings.GpuFanMaxPercent}%",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                Location = new Point(cardW - 75, fanCapY),
                Size = new Size(65, 20),
                TextAlign = ContentAlignment.MiddleRight
            };
            cardGpu.Controls.Add(_lblGpuFanCap);

            _tbGpuFanCap = new TrackBar
            {
                AutoSize = false,
                Minimum = 30,
                Maximum = 100,
                Value = Math.Clamp(_settings.GpuFanMaxPercent, 30, 100),
                TickFrequency = 10,
                SmallChange = 5,
                LargeChange = 10,
                Size = new Size(cardW - 24, 22),
                Location = new Point(12, fanCapY + 24),
                Cursor = Cursors.Hand
            };
            _tbGpuFanCap.ValueChanged += (s, e) => {
                _settings.GpuFanMaxPercent = _tbGpuFanCap.Value;
                _lblGpuFanCap.Text = $"{_tbGpuFanCap.Value}%";
                if (_settings.GpuFanCapEnabled && _settings.GpuIsQuietMode && _settings.GpuTuningEnabled)
                {
                    _hardware.GpuTuning.ApplyFanCap(_settings.GpuFanMaxPercent);
                }
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            };
            _toolTip.SetToolTip(_tbGpuFanCap, Loc.Get("GpuFanCapTip"));
            cardGpu.Controls.Add(_tbGpuFanCap);

            // Fan Presets
            int[] fanPresets = new[] { 35, 45, 60, 100 };
            int fanBtnX = 12;
            int fanBtnW = (cardW - 24 - (fanPresets.Length - 1) * 4) / fanPresets.Length;
            foreach (var fp in fanPresets)
            {
                string label = fp == 100 ? "Auto (100%)" : $"{fp}%";
                var btn = new Button
                {
                    Text = label,
                    Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                    Size = new Size(fanBtnW, 24),
                    Location = new Point(fanBtnX, fanCapY + 50),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(32, 32, 40),
                    ForeColor = Color.FromArgb(170, 170, 185),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Padding = Padding.Empty,
                    Cursor = Cursors.Hand
                };
                btn.FlatAppearance.BorderSize = 0;
                int targetPct = fp;
                btn.Click += (s, e) => {
                    _tbGpuFanCap.Value = targetPct;
                };
                cardGpu.Controls.Add(btn);
                _gpuFanPresetButtons.Add(btn);
                fanBtnX += fanBtnW + 4;
            }

            // Fail-Safe Banner
            int failSafeY = fanCapY + 78;
            _pnlFailSafe = new Panel
            {
                Location = new Point(12, failSafeY),
                Size = new Size(cardW - 24, 26),
                BackColor = Color.FromArgb(32, 28, 38)
            };
            cardGpu.Controls.Add(_pnlFailSafe);

            _lblGpuFailSafe = new Label
            {
                Text = string.Format(Loc.Get("GpuFailSafeInfo"), _settings.GpuFailSafeTempC > 0 ? _settings.GpuFailSafeTempC : 83),
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = Color.FromArgb(248, 113, 113),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 0, 0)
            };
            _pnlFailSafe.Controls.Add(_lblGpuFailSafe);

            UpdateGpuTuningCardUI();

            curY += cardGpuH + 8;

            // ================= HDD AWAKE SCHEDULE CARD =================
            int cardHddH = 268;
            var cardHdd = CreateCard(cardX, curY, cardW, cardHddH);
            _pnlBody.Controls.Add(cardHdd);

            _lblSecHdd = CreateSectionHeader(Loc.Get("HddScheduleModule"), 12, 8);
            cardHdd.Controls.Add(_lblSecHdd);
            RegisterCardSection("hdd", cardHdd, _lblSecHdd, () => Loc.Get("HddScheduleModule"), cardHddH);

            _chkHddSchedule = CreateCheckbox(Loc.Get("HddScheduleEnable"), 14, 30, _settings.HddScheduleEnabled, v => {
                _settings.HddScheduleEnabled = v;
                _hardware.HddSchedule.ApplyImmediate();
                UpdateHddCardUI();
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            });
            cardHdd.Controls.Add(_chkHddSchedule);

            _pnlHddControls = new Panel
            {
                Location = new Point(14, 56),
                Size = new Size(cardW - 28, 204),
                BackColor = Color.Transparent
            };
            cardHdd.Controls.Add(_pnlHddControls);

            // Row 1: Work hours range
            _lblHddWorkHours = new Label
            {
                Text = Loc.Get("HddWorkHours"),
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = Color.FromArgb(200, 200, 215),
                Location = new Point(0, 5),
                AutoSize = true
            };
            _pnlHddControls.Controls.Add(_lblHddWorkHours);

            _cbHddStart = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                DrawMode = DrawMode.OwnerDrawFixed,
                Font = new Font("Segoe UI", 8.5f),
                BackColor = Color.FromArgb(24, 24, 32),
                ForeColor = Color.FromArgb(56, 189, 248),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(cardW - 28 - 156, 1),
                Size = new Size(72, 26),
                ItemHeight = 20,
                Cursor = Cursors.Hand
            };
            _cbHddStart.DrawItem += (s, e) => {
                if (e.Index < 0) return;
                var cb = (ComboBox)s!;
                bool isSelected = (e.State & DrawItemState.Selected) != 0;
                Color bg = isSelected ? Color.FromArgb(38, 56, 80) : Color.FromArgb(24, 24, 32);
                Color fg = isSelected ? Color.White : Color.FromArgb(56, 189, 248);
                using var b = new SolidBrush(bg);
                e.Graphics.FillRectangle(b, e.Bounds);
                string text = cb.Items[e.Index]?.ToString() ?? "";
                TextRenderer.DrawText(e.Graphics, text, cb.Font, e.Bounds, fg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            };

            _cbHddEnd = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                DrawMode = DrawMode.OwnerDrawFixed,
                Font = new Font("Segoe UI", 8.5f),
                BackColor = Color.FromArgb(24, 24, 32),
                ForeColor = Color.FromArgb(56, 189, 248),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(cardW - 28 - 72, 1),
                Size = new Size(72, 26),
                ItemHeight = 20,
                Cursor = Cursors.Hand
            };
            _cbHddEnd.DrawItem += (s, e) => {
                if (e.Index < 0) return;
                var cb = (ComboBox)s!;
                bool isSelected = (e.State & DrawItemState.Selected) != 0;
                Color bg = isSelected ? Color.FromArgb(38, 56, 80) : Color.FromArgb(24, 24, 32);
                Color fg = isSelected ? Color.White : Color.FromArgb(56, 189, 248);
                using var b = new SolidBrush(bg);
                e.Graphics.FillRectangle(b, e.Bounds);
                string text = cb.Items[e.Index]?.ToString() ?? "";
                TextRenderer.DrawText(e.Graphics, text, cb.Font, e.Bounds, fg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            };

            _lblHddDash = new Label
            {
                Text = "—",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = Color.FromArgb(140, 140, 155),
                Location = new Point(cardW - 28 - 84, 4),
                Size = new Size(12, 20),
                TextAlign = ContentAlignment.MiddleCenter
            };

            for (int h = 0; h < 24; h++)
            {
                string timeStr = $"{h:D2}:00";
                _cbHddStart.Items.Add(timeStr);
                _cbHddEnd.Items.Add(timeStr);
            }
            _cbHddStart.SelectedIndex = Math.Clamp(_settings.HddWorkStartHour, 0, 23);
            _cbHddEnd.SelectedIndex = Math.Clamp(_settings.HddWorkEndHour, 0, 23);

            _cbHddStart.SelectedIndexChanged += (s, e) => {
                if (_cbHddStart.SelectedIndex >= 0)
                {
                    _settings.HddWorkStartHour = _cbHddStart.SelectedIndex;
                    _settings.HddWorkStartMinute = 0;
                    _settings.Save();
                    _hardware.HddSchedule.ApplyImmediate();
                }
            };
            _cbHddEnd.SelectedIndexChanged += (s, e) => {
                if (_cbHddEnd.SelectedIndex >= 0)
                {
                    _settings.HddWorkEndHour = _cbHddEnd.SelectedIndex;
                    _settings.HddWorkEndMinute = 0;
                    _settings.Save();
                    _hardware.HddSchedule.ApplyImmediate();
                }
            };

            _pnlHddControls.Controls.Add(_cbHddStart);
            _pnlHddControls.Controls.Add(_lblHddDash);
            _pnlHddControls.Controls.Add(_cbHddEnd);

            // Row 2: Workdays only
            _chkHddWorkDaysOnly = CreateCheckbox(Loc.Get("HddWorkDaysOnly"), 0, 34, _settings.HddWorkDaysOnly, v => {
                _settings.HddWorkDaysOnly = v;
                _hardware.HddSchedule.ApplyImmediate();
            });
            _pnlHddControls.Controls.Add(_chkHddWorkDaysOnly);

            // Row 3: Off-Hours timeout label
            _lblHddOffHours = new Label
            {
                Text = Loc.Get("HddOffHoursTimeout"),
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = Color.FromArgb(200, 200, 215),
                Location = new Point(0, 66),
                AutoSize = true
            };
            _pnlHddControls.Controls.Add(_lblHddOffHours);

            // Row 4: 5 Preset buttons full-width
            int[] hddTimeoutPresets = new[] { 10, 15, 20, 30, 60 };
            int totalBtnW = cardW - 28;
            int hddBtnGap = 5;
            int hddBtnW = (totalBtnW - (hddTimeoutPresets.Length - 1) * hddBtnGap) / hddTimeoutPresets.Length;
            for (int i = 0; i < hddTimeoutPresets.Length; i++)
            {
                int p = hddTimeoutPresets[i];
                int btnLeft = i * (hddBtnW + hddBtnGap);
                int btnWidth = (i == hddTimeoutPresets.Length - 1) ? (totalBtnW - btnLeft) : hddBtnW;
                var pBtn = new Button
                {
                    Text = $"{p}m",
                    Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                    Size = new Size(btnWidth, 26),
                    Location = new Point(btnLeft, 88),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = (_settings.HddOffHoursTimeoutMinutes == p) ? Color.FromArgb(56, 189, 248) : Color.FromArgb(28, 28, 36),
                    ForeColor = (_settings.HddOffHoursTimeoutMinutes == p) ? Color.FromArgb(18, 18, 24) : Color.FromArgb(170, 170, 185),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Padding = Padding.Empty,
                    Cursor = Cursors.Hand,
                    Tag = p
                };
                pBtn.FlatAppearance.BorderSize = 0;
                int targetMin = p;
                pBtn.Click += (s, e) => {
                    _settings.HddOffHoursTimeoutMinutes = targetMin;
                    _settings.Save();
                    _hardware.HddSchedule.ApplyImmediate();
                    UpdateHddTimeoutButtonsHighlight();
                };
                _pnlHddControls.Controls.Add(pBtn);
                _hddOffHoursPresetButtons.Add(pBtn);
            }

            // Row 5: Keep-Alive pulse
            _chkHddKeepAlive = CreateCheckbox(Loc.Get("HddKeepAlivePulse"), 0, 122, _settings.HddKeepAlivePulse, v => {
                _settings.HddKeepAlivePulse = v;
            });
            _toolTip.SetToolTip(_chkHddKeepAlive, Loc.Get("HddKeepAliveTip"));
            _pnlHddControls.Controls.Add(_chkHddKeepAlive);

            // Row 6: Divider
            _sepHdd = new Panel
            {
                Location = new Point(0, 154),
                Size = new Size(cardW - 28, 1),
                BackColor = Color.FromArgb(44, 44, 56)
            };
            _pnlHddControls.Controls.Add(_sepHdd);

            // Row 7: Action Buttons & Status
            _btnHddWakeNow = new Button
            {
                Text = Loc.Get("HddWakeNow"),
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Size = new Size(110, 28),
                Location = new Point(0, 164),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(28, 36, 48),
                ForeColor = Color.FromArgb(56, 189, 248),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _btnHddWakeNow.FlatAppearance.BorderColor = Color.FromArgb(44, 60, 80);
            _toolTip.SetToolTip(_btnHddWakeNow, Loc.Get("HddWakeTip"));
            _btnHddWakeNow.Click += async (s, e) => {
                _btnHddWakeNow.Enabled = false;
                _lblHddWakeStatus.Text = Loc.Get("HddWaking");
                _lblHddWakeStatus.ForeColor = Color.FromArgb(245, 158, 11);
                await _hardware.HddSchedule.WakeUpAllHddsAsync();
                UpdateHddCardUI();
                _btnHddWakeNow.Enabled = true;
            };
            _pnlHddControls.Controls.Add(_btnHddWakeNow);

            _btnHddSleepNow = new Button
            {
                Text = Loc.Get("HddSleepNow"),
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Size = new Size(95, 28),
                Location = new Point(116, 164),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(32, 32, 42),
                ForeColor = Color.FromArgb(180, 180, 200),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _btnHddSleepNow.FlatAppearance.BorderColor = Color.FromArgb(48, 48, 62);
            _toolTip.SetToolTip(_btnHddSleepNow, Loc.Get("HddSleepTip"));
            _btnHddSleepNow.Click += (s, e) => {
                _hardware.HddSchedule.PutDisksToSleep();
                UpdateHddCardUI();
            };
            _pnlHddControls.Controls.Add(_btnHddSleepNow);

            _lblHddWakeStatus = new Label
            {
                Text = _hardware.HddSchedule.GetStatusString(),
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = _hardware.HddSchedule.IsDisksAwake ? Color.FromArgb(52, 211, 153) : Color.FromArgb(160, 160, 175),
                Location = new Point(218, 168),
                Size = new Size(Math.Max(50, cardW - 28 - 222), 20),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            _pnlHddControls.Controls.Add(_lblHddWakeStatus);

            UpdateHddCardUI();
            UpdateHddTimeoutButtonsHighlight();

            curY += cardHddH + 8;

            // ================= SERVERSYNC CARD =================
            int cardServerSyncH = 175;
            var cardServerSync = CreateCard(cardX, curY, cardW, cardServerSyncH);
            _pnlBody.Controls.Add(cardServerSync);

            _lblSecServerSync = CreateSectionHeader(Loc.Get("ServerSync_Module"), 12, 8);
            cardServerSync.Controls.Add(_lblSecServerSync);
            RegisterCardSection("serversync", cardServerSync, _lblSecServerSync, () => Loc.Get("ServerSync_Module"), cardServerSyncH);

            _chkServerSync = CreateCheckbox(Loc.Get("ServerSync_Enable"), 14, 30, _settings.ServerSyncEnabled, v => {
                _settings.ServerSyncEnabled = v;
                _hardware.ServerSync.SetEnabled(v);
                _pnlServerSyncControls.Enabled = v;
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            });
            cardServerSync.Controls.Add(_chkServerSync);

            _pnlServerSyncControls = new Panel
            {
                Location = new Point(14, 56),
                Size = new Size(cardW - 28, 105),
                BackColor = Color.Transparent,
                Enabled = _settings.ServerSyncEnabled
            };
            cardServerSync.Controls.Add(_pnlServerSyncControls);

            // Row 1: Host and Port (dynamically positioned so label never overlaps input)
            _lblServerSyncHost = new Label
            {
                Text = Loc.Get("ServerSync_Host"),
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = Color.FromArgb(200, 200, 215),
                Location = new Point(0, 6),
                AutoSize = true
            };
            _pnlServerSyncControls.Controls.Add(_lblServerSyncHost);

            _txtServerSyncHost = new TextBox
            {
                Location = new Point(68, 3),
                Size = new Size(140, 22),
                BackColor = Color.FromArgb(24, 24, 32),
                ForeColor = Color.FromArgb(45, 212, 191),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 8.5f),
                Text = _settings.ServerSyncHost
            };
            _txtServerSyncHost.Leave += (s, e) => SaveServerSyncConfig();
            _pnlServerSyncControls.Controls.Add(_txtServerSyncHost);

            _lblServerSyncPort = new Label
            {
                Text = Loc.Get("ServerSync_Port"),
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = Color.FromArgb(200, 200, 215),
                Location = new Point(218, 6),
                AutoSize = true
            };
            _pnlServerSyncControls.Controls.Add(_lblServerSyncPort);

            _txtServerSyncPort = new TextBox
            {
                Location = new Point(268, 3),
                Size = new Size(60, 22),
                BackColor = Color.FromArgb(24, 24, 32),
                ForeColor = Color.FromArgb(45, 212, 191),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 8.5f),
                Text = _settings.ServerSyncPort.ToString()
            };
            _txtServerSyncPort.Leave += (s, e) => SaveServerSyncConfig();
            _pnlServerSyncControls.Controls.Add(_txtServerSyncPort);

            // Row 2: Test button & status (30px height, bold font, no clipped descenders)
            _btnServerSyncTest = new Button
            {
                Text = Loc.Get("ServerSync_TestBtn"),
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Size = new Size(115, 30),
                Location = new Point(0, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(32, 36, 48),
                ForeColor = Color.FromArgb(56, 189, 248),
                Cursor = Cursors.Hand
            };
            _btnServerSyncTest.FlatAppearance.BorderSize = 1;
            _btnServerSyncTest.FlatAppearance.BorderColor = Color.FromArgb(50, 60, 80);
            _btnServerSyncTest.Click += async (s, e) => await TestServerSyncConnectionAsync();
            _pnlServerSyncControls.Controls.Add(_btnServerSyncTest);

            _lblServerSyncTestStatus = new Label
            {
                Location = new Point(125, 39),
                Size = new Size(cardW - 28 - 130, 22),
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = Color.FromArgb(160, 160, 175),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Text = ""
            };
            _pnlServerSyncControls.Controls.Add(_lblServerSyncTestStatus);

            // Row 3: ServerSync Hint / Help text explaining purpose and ports
            _lblServerSyncHint = new Label
            {
                Text = Loc.Get("ServerSync_Hint"),
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = Color.FromArgb(135, 140, 155),
                Location = new Point(0, 70),
                Size = new Size(cardW - 28, 32),
                AutoEllipsis = true
            };
            _pnlServerSyncControls.Controls.Add(_lblServerSyncHint);

            curY += cardServerSyncH + 8;

            // ================= KEYBOARD LAYOUT SWITCHER CARD =================
            int cardLayoutH = 142;
            var cardLayout = CreateCard(cardX, curY, cardW, cardLayoutH);
            _pnlBody.Controls.Add(cardLayout);

            _lblSecLayout = CreateSectionHeader(Loc.Get("SecLayoutSwitcher"), 12, 8);
            cardLayout.Controls.Add(_lblSecLayout);
            RegisterCardSection("layout", cardLayout, _lblSecLayout, () => Loc.Get("SecLayoutSwitcher"), cardLayoutH);

            _chkLayoutSwitch = new CheckBox
            {
                Text = Loc.Get("LayoutSwitch_Enable"),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(220, 220, 235),
                Location = new Point(14, 28),
                Size = new Size(cardW - 28, 22),
                AutoEllipsis = true,
                Checked = _settings.LayoutSwitchEnabled,
                Cursor = Cursors.Hand
            };
            _chkLayoutSwitch.CheckedChanged += (s, e) => {
                _settings.LayoutSwitchEnabled = _chkLayoutSwitch.Checked;
                _pnlLayoutControls.Enabled = _chkLayoutSwitch.Checked;
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            };
            cardLayout.Controls.Add(_chkLayoutSwitch);

            _pnlLayoutControls = new Panel
            {
                Location = new Point(14, 52),
                Size = new Size(cardW - 28, 82),
                BackColor = Color.Transparent,
                Enabled = _settings.LayoutSwitchEnabled
            };
            cardLayout.Controls.Add(_pnlLayoutControls);

            // Row 1: FlowLayoutPanel for Modifiers
            _flowModifiers = new FlowLayoutPanel
            {
                Location = new Point(0, 0),
                Size = new Size(cardW - 28, 25),
                BackColor = Color.Transparent,
                WrapContents = false,
                AutoSize = false
            };
            _pnlLayoutControls.Controls.Add(_flowModifiers);

            _lblLayoutHotkey = new Label
            {
                Text = Loc.Get("LayoutSwitch_Hotkey"),
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = Color.FromArgb(200, 200, 215),
                Margin = new Padding(0, 4, 8, 0),
                AutoSize = true
            };
            _flowModifiers.Controls.Add(_lblLayoutHotkey);

            _chkModCtrl = new CheckBox
            {
                Text = "Ctrl",
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = Color.FromArgb(220, 220, 235),
                Margin = new Padding(0, 2, 6, 0),
                AutoSize = true,
                Checked = _settings.LayoutSwitchModifiersCtrl,
                Cursor = Cursors.Hand
            };
            _chkModCtrl.CheckedChanged += (s, e) => {
                _settings.LayoutSwitchModifiersCtrl = _chkModCtrl.Checked;
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            };
            _flowModifiers.Controls.Add(_chkModCtrl);

            _chkModAlt = new CheckBox
            {
                Text = "Alt",
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = Color.FromArgb(220, 220, 235),
                Margin = new Padding(0, 2, 6, 0),
                AutoSize = true,
                Checked = _settings.LayoutSwitchModifiersAlt,
                Cursor = Cursors.Hand
            };
            _chkModAlt.CheckedChanged += (s, e) => {
                _settings.LayoutSwitchModifiersAlt = _chkModAlt.Checked;
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            };
            _flowModifiers.Controls.Add(_chkModAlt);

            _chkModShift = new CheckBox
            {
                Text = "Shift",
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = Color.FromArgb(220, 220, 235),
                Margin = new Padding(0, 2, 6, 0),
                AutoSize = true,
                Checked = _settings.LayoutSwitchModifiersShift,
                Cursor = Cursors.Hand
            };
            _chkModShift.CheckedChanged += (s, e) => {
                _settings.LayoutSwitchModifiersShift = _chkModShift.Checked;
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            };
            _flowModifiers.Controls.Add(_chkModShift);

            _chkModWin = new CheckBox
            {
                Text = "Win",
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = Color.FromArgb(220, 220, 235),
                Margin = new Padding(0, 2, 0, 0),
                AutoSize = true,
                Checked = _settings.LayoutSwitchModifiersWin,
                Cursor = Cursors.Hand
            };
            _chkModWin.CheckedChanged += (s, e) => {
                _settings.LayoutSwitchModifiersWin = _chkModWin.Checked;
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            };
            _flowModifiers.Controls.Add(_chkModWin);

            // Row 2: FlowLayoutPanel for Key & Reset
            _flowKeyRow = new FlowLayoutPanel
            {
                Location = new Point(0, 27),
                Size = new Size(cardW - 28, 28),
                BackColor = Color.Transparent,
                WrapContents = false,
                AutoSize = false
            };
            _pnlLayoutControls.Controls.Add(_flowKeyRow);

            _lblLayoutKey = new Label
            {
                Text = Loc.Get("LayoutSwitch_Key"),
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = Color.FromArgb(200, 200, 215),
                Margin = new Padding(0, 5, 8, 0),
                AutoSize = true
            };
            _flowKeyRow.Controls.Add(_lblLayoutKey);

            _cbLayoutKey = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(24, 24, 32),
                ForeColor = Color.FromArgb(45, 212, 191),
                Font = new Font("Segoe UI", 8.5f),
                Size = new Size(110, 24),
                Margin = new Padding(0, 1, 10, 0)
            };
            PopulateLayoutKeyComboBox();
            _cbLayoutKey.SelectedIndexChanged += (s, e) => {
                if (_cbLayoutKey.SelectedItem is KeyOptionItem item)
                {
                    _settings.LayoutSwitchVirtualKey = (int)item.Key;
                    _settings.Save();
                    _onSettingsUpdated?.Invoke();
                }
            };
            _flowKeyRow.Controls.Add(_cbLayoutKey);

            _btnResetLayoutHotkey = new Button
            {
                Text = Loc.Get("LayoutSwitch_Reset"),
                Font = new Font("Segoe UI", 8.25f),
                Size = new Size(72, 24),
                Margin = new Padding(0, 1, 0, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(32, 36, 48),
                ForeColor = Color.FromArgb(160, 160, 175),
                Cursor = Cursors.Hand
            };
            _btnResetLayoutHotkey.FlatAppearance.BorderSize = 1;
            _btnResetLayoutHotkey.FlatAppearance.BorderColor = Color.FromArgb(50, 60, 80);
            _toolTip.SetToolTip(_btnResetLayoutHotkey, Loc.Get("LayoutSwitch_ResetTip"));
            _btnResetLayoutHotkey.Click += (s, e) => {
                _settings.LayoutSwitchModifiersCtrl = true;
                _settings.LayoutSwitchModifiersAlt = false;
                _settings.LayoutSwitchModifiersShift = false;
                _settings.LayoutSwitchModifiersWin = false;
                _settings.LayoutSwitchVirtualKey = (int)Keys.Pause;
                _chkModCtrl.Checked = true;
                _chkModAlt.Checked = false;
                _chkModShift.Checked = false;
                _chkModWin.Checked = false;
                SelectKeyInComboBox(Keys.Pause);
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            };
            _flowKeyRow.Controls.Add(_btnResetLayoutHotkey);

            // Row 3: Hint
            _lblLayoutSwitchHint = new Label
            {
                Text = Loc.Get("LayoutSwitch_Hint"),
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = Color.FromArgb(135, 140, 155),
                Location = new Point(0, 56),
                Size = new Size(cardW - 28, 24),
                AutoEllipsis = true
            };
            _pnlLayoutControls.Controls.Add(_lblLayoutSwitchHint);

            curY += cardLayoutH + 8;

            // ================= DESKTOP ICONS CARD (MULTI-MONITOR) =================
            int cardDesktopIconsH = 154;
            var cardDesktopIcons = CreateCard(cardX, curY, cardW, cardDesktopIconsH);
            _pnlBody.Controls.Add(cardDesktopIcons);

            _lblSecDesktopIcons = CreateSectionHeader("🖥 " + Loc.Get("DesktopIcons_SecTitle"), 12, 8);
            cardDesktopIcons.Controls.Add(_lblSecDesktopIcons);
            RegisterCardSection("desktopicons", cardDesktopIcons, _lblSecDesktopIcons, () => "🖥 " + Loc.Get("DesktopIcons_SecTitle"), cardDesktopIconsH);

            _pnlDesktopIconsControls = new Panel
            {
                Location = new Point(14, 32),
                Size = new Size(cardW - 28, 114),
                BackColor = Color.Transparent
            };
            cardDesktopIcons.Controls.Add(_pnlDesktopIconsControls);

            int diBtnGap = 8;
            int diBtnW = (cardW - 28 - diBtnGap) / 2;
            int diBtnH = 28;

            _btnDesktopIconsSave = new Button
            {
                Text = Loc.Get("DesktopIcons_Save"),
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(0, 0),
                Size = new Size(diBtnW, diBtnH),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(28, 32, 44),
                ForeColor = Color.FromArgb(45, 212, 191),
                Cursor = Cursors.Hand
            };
            _btnDesktopIconsSave.FlatAppearance.BorderSize = 1;
            _btnDesktopIconsSave.FlatAppearance.BorderColor = Color.FromArgb(45, 212, 191, 100);
            _btnDesktopIconsSave.Click += (s, e) => {
                _btnDesktopIconsSave.Enabled = false;
                Task.Run(() => {
                    var res = DesktopIconService.SaveLayout();
                    this.BeginInvoke((Action)(() => {
                        _btnDesktopIconsSave.Enabled = true;
                        UpdateDesktopIconsUI();
                    }));
                });
            };
            _pnlDesktopIconsControls.Controls.Add(_btnDesktopIconsSave);

            _btnDesktopIconsRestore = new Button
            {
                Text = Loc.Get("DesktopIcons_Restore"),
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(diBtnW + diBtnGap, 0),
                Size = new Size(cardW - 28 - diBtnW - diBtnGap, diBtnH),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(28, 32, 44),
                ForeColor = Color.FromArgb(220, 220, 235),
                Cursor = Cursors.Hand,
                Enabled = DesktopIconService.HasSavedSnapshot()
            };
            _btnDesktopIconsRestore.FlatAppearance.BorderSize = 1;
            _btnDesktopIconsRestore.FlatAppearance.BorderColor = Color.FromArgb(60, 68, 88);
            _btnDesktopIconsRestore.Click += (s, e) => {
                _btnDesktopIconsRestore.Enabled = false;
                Task.Run(() => {
                    var res = DesktopIconService.RestoreLayout();
                    this.BeginInvoke((Action)(() => {
                        UpdateDesktopIconsUI();
                    }));
                });
            };
            _pnlDesktopIconsControls.Controls.Add(_btnDesktopIconsRestore);

            _chkDesktopIconsAutoRestore = new CheckBox
            {
                Text = Loc.Get("DesktopIcons_AutoRestore"),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(220, 220, 235),
                Location = new Point(0, 36),
                Size = new Size(cardW - 28, 22),
                AutoEllipsis = true,
                Checked = _settings.AutoRestoreDesktopIcons,
                Cursor = Cursors.Hand
            };
            _chkDesktopIconsAutoRestore.CheckedChanged += (s, e) => {
                _settings.AutoRestoreDesktopIcons = _chkDesktopIconsAutoRestore.Checked;
                _settings.Save();
                DesktopIconService.UpdateSettings(_settings);
                _onSettingsUpdated?.Invoke();
            };
            _pnlDesktopIconsControls.Controls.Add(_chkDesktopIconsAutoRestore);

            _lblDesktopIconsStatus = new Label
            {
                Text = DesktopIconService.GetStatusInfo(),
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(140, 185, 215),
                Location = new Point(0, 62),
                Size = new Size(cardW - 28, 18),
                AutoEllipsis = true
            };
            _pnlDesktopIconsControls.Controls.Add(_lblDesktopIconsStatus);

            _lblDesktopIconsHint = new Label
            {
                Text = Loc.Get("DesktopIcons_Hint"),
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = Color.FromArgb(135, 140, 155),
                Location = new Point(0, 82),
                Size = new Size(cardW - 28, 30),
                AutoEllipsis = true
            };
            _pnlDesktopIconsControls.Controls.Add(_lblDesktopIconsHint);

            curY += cardDesktopIconsH + 8;

            // ================= TRANSLATE & LLM CARD =================
            int cardTranslateH = 480;
            var cardTranslate = CreateCard(cardX, curY, cardW, cardTranslateH);
            _pnlBody.Controls.Add(cardTranslate);

            _lblSecTranslate = CreateSectionHeader(Loc.Get("Translate_Module"), 12, 8);
            cardTranslate.Controls.Add(_lblSecTranslate);
            RegisterCardSection("translate", cardTranslate, _lblSecTranslate, () => Loc.Get("Translate_Module"), cardTranslateH);

            _chkTranslateEnable = new CheckBox
            {
                Text = Loc.Get("Translate_Enable"),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(220, 220, 235),
                Location = new Point(14, 30),
                Size = new Size(cardW - 28, 22),
                Checked = _settings.ShowTranslateModule,
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            _chkTranslateEnable.CheckedChanged += (s, e) => {
                _settings.ShowTranslateModule = _chkTranslateEnable.Checked;
                if (_chkTranslateModule != null) _chkTranslateModule.Checked = _chkTranslateEnable.Checked;
                _pnlTranslateControls.Enabled = _chkTranslateEnable.Checked;
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            };
            cardTranslate.Controls.Add(_chkTranslateEnable);

            _pnlTranslateControls = new Panel
            {
                Location = new Point(14, 56),
                Size = new Size(cardW - 28, cardTranslateH - 62),
                BackColor = Color.Transparent,
                Enabled = _settings.ShowTranslateModule
            };
            cardTranslate.Controls.Add(_pnlTranslateControls);

            // Row 0: API Server URL label + Presets
            _lblTranslateApiUrl = new Label
            {
                Text = Loc.Get("Translate_ApiUrl"),
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = Color.FromArgb(170, 170, 185),
                Location = new Point(0, 4),
                Size = new Size(Math.Max(100, cardW - 28 - 200), 20),
                AutoEllipsis = true,
                UseMnemonic = false
            };
            _pnlTranslateControls.Controls.Add(_lblTranslateApiUrl);

            _btnPresetLms = new Button
            {
                Text = "LM Studio",
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                Location = new Point(cardW - 28 - 196, 0),
                Size = new Size(95, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(32, 36, 48),
                ForeColor = Color.FromArgb(56, 189, 248),
                Padding = Padding.Empty,
                TextAlign = ContentAlignment.MiddleCenter,
                UseMnemonic = false,
                Cursor = Cursors.Hand
            };
            _btnPresetLms.FlatAppearance.BorderSize = 1;
            _btnPresetLms.FlatAppearance.BorderColor = Color.FromArgb(56, 189, 248, 80);
            _btnPresetLms.Click += (s, e) => {
                _tbTranslateUrl.Text = "http://127.0.0.1:1234/v1";
                _settings.TranslateApiUrl = _tbTranslateUrl.Text;
                _settings.Save();
                _hardware.Translation.UpdateSettings(_settings);
                UpdateBackendButtonsHighlight();
                RunCheckConnection();
            };
            _pnlTranslateControls.Controls.Add(_btnPresetLms);
            _toolTip.SetToolTip(_btnPresetLms, Loc.Get("Translate_PresetLms"));

            _btnPresetOllama = new Button
            {
                Text = "Ollama",
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                Location = new Point(cardW - 28 - 95, 0),
                Size = new Size(95, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(32, 36, 48),
                ForeColor = Color.FromArgb(56, 189, 248),
                Padding = Padding.Empty,
                TextAlign = ContentAlignment.MiddleCenter,
                UseMnemonic = false,
                Cursor = Cursors.Hand
            };
            _btnPresetOllama.FlatAppearance.BorderSize = 1;
            _btnPresetOllama.FlatAppearance.BorderColor = Color.FromArgb(56, 189, 248, 80);
            _btnPresetOllama.Click += (s, e) => {
                _tbTranslateUrl.Text = "http://127.0.0.1:11434/v1";
                _settings.TranslateApiUrl = _tbTranslateUrl.Text;
                _settings.Save();
                _hardware.Translation.UpdateSettings(_settings);
                UpdateBackendButtonsHighlight();
                RunCheckConnection();
            };
            _pnlTranslateControls.Controls.Add(_btnPresetOllama);
            _toolTip.SetToolTip(_btnPresetOllama, Loc.Get("Translate_PresetOllama"));

            // Row 1: URL input & Test button
            int testBtnW = 88;
            _tbTranslateUrl = new TextBox
            {
                Text = _settings.TranslateApiUrl,
                Font = new Font("Segoe UI", 8.5f),
                BackColor = Color.FromArgb(24, 24, 32),
                ForeColor = Color.FromArgb(220, 220, 235),
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(0, 28),
                Size = new Size(cardW - 28 - testBtnW - 8, 25)
            };
            _tbTranslateUrl.TextChanged += (s, e) => {
                _settings.TranslateApiUrl = _tbTranslateUrl.Text.Trim();
                _settings.Save();
                _hardware.Translation.UpdateSettings(_settings);
                UpdateBackendButtonsHighlight();
            };
            _pnlTranslateControls.Controls.Add(_tbTranslateUrl);

            _btnTranslateCheck = new Button
            {
                Text = Loc.Get("Translate_CheckBtn"),
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(cardW - 28 - testBtnW, 28),
                Size = new Size(testBtnW, 25),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(32, 36, 48),
                ForeColor = Color.FromArgb(45, 212, 191),
                Padding = Padding.Empty,
                TextAlign = ContentAlignment.MiddleCenter,
                UseMnemonic = false,
                Cursor = Cursors.Hand
            };
            _btnTranslateCheck.FlatAppearance.BorderSize = 1;
            _btnTranslateCheck.FlatAppearance.BorderColor = Color.FromArgb(45, 212, 191, 100);
            _btnTranslateCheck.Click += (s, e) => RunCheckConnection();
            _pnlTranslateControls.Controls.Add(_btnTranslateCheck);
            _toolTip.SetToolTip(_btnTranslateCheck, Loc.Get("Translate_CheckBtn"));

            _lblTranslateCheckStatus = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = Color.FromArgb(140, 185, 215),
                Location = new Point(0, 56),
                Size = new Size(cardW - 28, 18),
                AutoEllipsis = true
            };
            _pnlTranslateControls.Controls.Add(_lblTranslateCheckStatus);

            // Row 2: Model & Target Language
            int modelW = (int)((cardW - 28 - 12) * 0.55);
            int langW = cardW - 28 - 12 - modelW;

            _lblTranslateModel = new Label
            {
                Text = Loc.Get("Translate_Model"),
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = Color.FromArgb(170, 170, 185),
                Location = new Point(0, 78),
                Size = new Size(modelW, 20),
                UseMnemonic = false
            };
            _pnlTranslateControls.Controls.Add(_lblTranslateModel);

            _lblTranslateLang = new Label
            {
                Text = Loc.Get("Translate_TargetLang"),
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = Color.FromArgb(170, 170, 185),
                Location = new Point(modelW + 12, 78),
                Size = new Size(langW, 20),
                UseMnemonic = false
            };
            _pnlTranslateControls.Controls.Add(_lblTranslateLang);

            _cbTranslateModel = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(24, 24, 32),
                ForeColor = Color.FromArgb(45, 212, 191),
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(0, 100),
                Size = new Size(modelW, 26)
            };
            if (!string.IsNullOrEmpty(_settings.TranslateModel))
            {
                _cbTranslateModel.Items.Add(_settings.TranslateModel);
                _cbTranslateModel.SelectedIndex = 0;
            }
            _cbTranslateModel.SelectedIndexChanged += (s, e) => {
                if (_cbTranslateModel.SelectedItem != null)
                {
                    _settings.TranslateModel = _cbTranslateModel.SelectedItem.ToString() ?? "";
                    _settings.Save();
                    _hardware.Translation.UpdateSettings(_settings);
                    _onSettingsUpdated?.Invoke();
                }
            };
            _pnlTranslateControls.Controls.Add(_cbTranslateModel);

            _cbTranslateTargetLang = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(24, 24, 32),
                ForeColor = Color.FromArgb(220, 220, 235),
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(modelW + 12, 100),
                Size = new Size(langW, 26)
            };
            foreach (var lang in TranslationService.SupportedLanguages)
            {
                _cbTranslateTargetLang.Items.Add(new KeyValuePair<string, string>(lang.Code, $"{lang.LocalName} ({lang.Code})"));
            }
            _cbTranslateTargetLang.DisplayMember = "Value";
            _cbTranslateTargetLang.ValueMember = "Key";
            for (int i = 0; i < _cbTranslateTargetLang.Items.Count; i++)
            {
                if (_cbTranslateTargetLang.Items[i] is KeyValuePair<string, string> kv && kv.Key == _settings.TranslateTargetLang)
                {
                    _cbTranslateTargetLang.SelectedIndex = i;
                    break;
                }
            }
            _cbTranslateTargetLang.SelectedIndexChanged += (s, e) => {
                if (_cbTranslateTargetLang.SelectedItem is KeyValuePair<string, string> kv)
                {
                    _settings.TranslateTargetLang = kv.Key;
                    _settings.Save();
                    _hardware.Translation.UpdateSettings(_settings);
                    _onSettingsUpdated?.Invoke();
                }
            };
            _pnlTranslateControls.Controls.Add(_cbTranslateTargetLang);

            // Row 3: VRAM release button & auto-unload timeout
            int vramBtnW = 155;
            _btnFreeVramNow = new Button
            {
                Text = "🧹 " + Loc.Get("Translate_FreeVramBtn"),
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(0, 136),
                Size = new Size(vramBtnW, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(32, 36, 48),
                ForeColor = Color.FromArgb(251, 146, 60),
                Padding = Padding.Empty,
                TextAlign = ContentAlignment.MiddleCenter,
                UseMnemonic = false,
                Cursor = Cursors.Hand
            };
            _btnFreeVramNow.FlatAppearance.BorderSize = 1;
            _btnFreeVramNow.FlatAppearance.BorderColor = Color.FromArgb(251, 146, 60, 100);
            _btnFreeVramNow.Click += (s, e) => {
                _btnFreeVramNow.Enabled = false;
                _lblVramStatus.Text = "...";
                Task.Run(async () => {
                    var res = await _hardware.Translation.FreeVramAsync();
                    this.BeginInvoke((Action)(() => {
                        _btnFreeVramNow.Enabled = true;
                        _lblVramStatus.Text = res.success ? Loc.Get("Translate_VramFreed") : "Error";
                        _lblVramStatus.ForeColor = res.success ? Color.FromArgb(45, 212, 191) : Color.FromArgb(248, 113, 113);
                    }));
                });
            };
            _pnlTranslateControls.Controls.Add(_btnFreeVramNow);
            _toolTip.SetToolTip(_btnFreeVramNow, Loc.Get("TipTranslateFreeVram"));

            _lblVramStatus = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = Color.FromArgb(45, 212, 191),
                Location = new Point(vramBtnW + 8, 141),
                Size = new Size(80, 20),
                AutoEllipsis = true
            };
            _pnlTranslateControls.Controls.Add(_lblVramStatus);

            int timeoutW = 90;
            int timeoutLblW = 75;
            _lblTranslateVramTimeout = new Label
            {
                Text = Loc.Get("Translate_VramTimeoutShort"),
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = Color.FromArgb(170, 170, 185),
                Location = new Point(cardW - 28 - timeoutW - timeoutLblW - 4, 140),
                Size = new Size(timeoutLblW, 20),
                TextAlign = ContentAlignment.MiddleRight,
                UseMnemonic = false
            };
            _toolTip?.SetToolTip(_lblTranslateVramTimeout, Loc.Get("Translate_VramTimeout"));
            _pnlTranslateControls.Controls.Add(_lblTranslateVramTimeout);

            _cbTranslateVramTimeout = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(24, 24, 32),
                ForeColor = Color.FromArgb(220, 220, 235),
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(cardW - 28 - timeoutW, 136),
                Size = new Size(timeoutW, 26)
            };
            int[] timeoutOptions = new[] { 0, 1, 2, 5, 10, 15, 30 };
            foreach (int to in timeoutOptions)
            {
                string label = to == 0 ? "Off" : $"{to} min";
                _cbTranslateVramTimeout.Items.Add(new KeyValuePair<int, string>(to, label));
            }
            _cbTranslateVramTimeout.DisplayMember = "Value";
            _cbTranslateVramTimeout.ValueMember = "Key";
            for (int i = 0; i < _cbTranslateVramTimeout.Items.Count; i++)
            {
                if (_cbTranslateVramTimeout.Items[i] is KeyValuePair<int, string> kv && kv.Key == _settings.TranslateVramTimeoutMinutes)
                {
                    _cbTranslateVramTimeout.SelectedIndex = i;
                    break;
                }
            }
            _cbTranslateVramTimeout.SelectedIndexChanged += (s, e) => {
                if (_cbTranslateVramTimeout.SelectedItem is KeyValuePair<int, string> kv)
                {
                    _settings.TranslateVramTimeoutMinutes = kv.Key;
                    _settings.Save();
                    _hardware.Translation.UpdateSettings(_settings);
                }
            };
            _pnlTranslateControls.Controls.Add(_cbTranslateVramTimeout);

            // Row 4: Auto-copy Checkbox
            _chkTranslateAutoCopy = new CheckBox
            {
                Text = Loc.Get("Translate_AutoCopy"),
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = Color.FromArgb(220, 220, 235),
                Location = new Point(0, 174),
                Size = new Size(cardW - 28, 22),
                Checked = _settings.TranslateAutoCopy,
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            _chkTranslateAutoCopy.CheckedChanged += (s, e) => {
                _settings.TranslateAutoCopy = _chkTranslateAutoCopy.Checked;
                _settings.Save();
            };
            _pnlTranslateControls.Controls.Add(_chkTranslateAutoCopy);

            // Row 5: Separator
            _sepTransHotkeys = new Panel
            {
                Location = new Point(0, 204),
                Size = new Size(cardW - 28, 1),
                BackColor = Color.FromArgb(40, 40, 52)
            };
            _pnlTranslateControls.Controls.Add(_sepTransHotkeys);

            // Rows 6, 7, 8: Hotkeys (2-line layout per hotkey for 100% clean display without clipping)
            _rowHk1 = CreateHotkeyRow(
                Loc.Get("Translate_HotkeyText"),
                _settings.HotkeyTranslateEnabled, v => { _settings.HotkeyTranslateEnabled = v; _settings.Save(); },
                _settings.HotkeyTranslateCtrl, v => { _settings.HotkeyTranslateCtrl = v; _settings.Save(); },
                _settings.HotkeyTranslateAlt, v => { _settings.HotkeyTranslateAlt = v; _settings.Save(); },
                _settings.HotkeyTranslateShift, v => { _settings.HotkeyTranslateShift = v; _settings.Save(); },
                _settings.HotkeyTranslateWin, v => { _settings.HotkeyTranslateWin = v; _settings.Save(); },
                _settings.HotkeyTranslateKey, v => { _settings.HotkeyTranslateKey = v; _settings.Save(); },
                214, cardW - 28
            );
            _pnlTranslateControls.Controls.Add(_rowHk1);

            _rowHk2 = CreateHotkeyRow(
                Loc.Get("Translate_HotkeyOcr"),
                _settings.HotkeyOcrEnabled, v => { _settings.HotkeyOcrEnabled = v; _settings.Save(); },
                _settings.HotkeyOcrCtrl, v => { _settings.HotkeyOcrCtrl = v; _settings.Save(); },
                _settings.HotkeyOcrAlt, v => { _settings.HotkeyOcrAlt = v; _settings.Save(); },
                _settings.HotkeyOcrShift, v => { _settings.HotkeyOcrShift = v; _settings.Save(); },
                _settings.HotkeyOcrWin, v => { _settings.HotkeyOcrWin = v; _settings.Save(); },
                _settings.HotkeyOcrKey, v => { _settings.HotkeyOcrKey = v; _settings.Save(); },
                272, cardW - 28
            );
            _pnlTranslateControls.Controls.Add(_rowHk2);

            _rowHk3 = CreateHotkeyRow(
                Loc.Get("Translate_HotkeyOcrTrans"),
                _settings.HotkeyOcrTranslateEnabled, v => { _settings.HotkeyOcrTranslateEnabled = v; _settings.Save(); },
                _settings.HotkeyOcrTranslateCtrl, v => { _settings.HotkeyOcrTranslateCtrl = v; _settings.Save(); },
                _settings.HotkeyOcrTranslateAlt, v => { _settings.HotkeyOcrTranslateAlt = v; _settings.Save(); },
                _settings.HotkeyOcrTranslateShift, v => { _settings.HotkeyOcrTranslateShift = v; _settings.Save(); },
                _settings.HotkeyOcrTranslateWin, v => { _settings.HotkeyOcrTranslateWin = v; _settings.Save(); },
                _settings.HotkeyOcrTranslateKey, v => { _settings.HotkeyOcrTranslateKey = v; _settings.Save(); },
                330, cardW - 28
            );
            _pnlTranslateControls.Controls.Add(_rowHk3);

            UpdateBackendButtonsHighlight();

            curY += cardTranslateH + 8;

            // ================= 3. TRAY ICON CARD =================
            var cardTray = CreateCard(cardX, curY, cardW, 70);
            _pnlBody.Controls.Add(cardTray);

            _lblSecTray = CreateSectionHeader(Loc.Get("TrayIconMenu"), 12, 8);
            cardTray.Controls.Add(_lblSecTray);
            RegisterCardSection("tray", cardTray, _lblSecTray, () => Loc.Get("TrayIconMenu"), 70);

            int trayBtnX = 12;
            int trayBtnW = (cardW - 24 - 15) / 4;
            int trayBtnH = 28;
            int trayBtnY = 32;

            (string labelKey, string tipKey, int metricVal, bool isLive)[] trayOptions = new[]
            {
                ("Cpu", "TrayMetricCpu", 0, true),
                ("Gpu", "TrayMetricGpu", 1, true),
                ("Ram", "TrayMetricRam", 2, true),
                ("TrayMetricNoneShort", "TrayMetricNone", -1, false)
            };

            foreach (var opt in trayOptions)
            {
                string text = opt.isLive ? $"{Loc.Get(opt.labelKey)} %" : Loc.Get(opt.labelKey);
                var btn = new Button
                {
                    Text = text,
                    Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                    Size = new Size(trayBtnW, trayBtnH),
                    Location = new Point(trayBtnX, trayBtnY),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand,
                    Tag = opt
                };
                btn.FlatAppearance.BorderSize = 0;
                _toolTip?.SetToolTip(btn, Loc.Get(opt.tipKey));
                btn.Click += (s, e) => {
                    if (opt.isLive)
                    {
                        _settings.LiveTrayIcon = true;
                        _settings.TrayMetric = opt.metricVal;
                    }
                    else
                    {
                        _settings.LiveTrayIcon = false;
                    }
                    _settings.Save();
                    UpdateTrayButtonsHighlight();
                    _onSettingsUpdated?.Invoke();
                };
                cardTray.Controls.Add(btn);
                _trayButtons.Add(btn);

                trayBtnX += trayBtnW + 5;
            }
            UpdateTrayButtonsHighlight();

            curY += 78;

            // ================= 3b. UI SCALE CARD =================
            int cardUiScaleH = 126;
            var cardUiScale = CreateCard(cardX, curY, cardW, cardUiScaleH);
            _pnlBody.Controls.Add(cardUiScale);

            _lblSecUiScale = CreateSectionHeader(Loc.Get("UiScale"), 12, 8);
            cardUiScale.Controls.Add(_lblSecUiScale);
            RegisterCardSection("uiscale", cardUiScale, _lblSecUiScale, () => Loc.Get("UiScale"), cardUiScaleH);

            _lblUiScaleVal = new Label
            {
                Text = $"{_settings.UiScalePercent}%",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                Location = new Point(cardW - 75, 8),
                Size = new Size(65, 20),
                TextAlign = ContentAlignment.MiddleRight
            };
            cardUiScale.Controls.Add(_lblUiScaleVal);

            _sliderUiScale = new TrackBar
            {
                AutoSize = false,
                Minimum = 70,
                Maximum = 140,
                Value = Math.Clamp(_settings.UiScalePercent, 70, 140),
                TickFrequency = 5,
                SmallChange = 5,
                LargeChange = 10,
                Size = new Size(cardW - 24, 22),
                Location = new Point(12, 30),
                Cursor = Cursors.Hand
            };
            _sliderUiScale.ValueChanged += (s, e) => {
                int val = _sliderUiScale.Value;
                _settings.UiScalePercent = val;
                if (_settings.AutoUiScale)
                {
                    _settings.AutoUiScale = false;
                    if (_chkAutoUiScale != null) _chkAutoUiScale.Checked = false;
                }
                _settings.Save();
                UpdateUiScaleButtonsHighlight();
                _onSettingsUpdated?.Invoke();
            };
            cardUiScale.Controls.Add(_sliderUiScale);

            // Presets row: 75%, 85%, 100%, 115%, 125%
            int[] scalePresets = new[] { 75, 85, 100, 115, 125 };
            int scX = 12;
            int scW = (cardW - 24 - (scalePresets.Length - 1) * 4) / scalePresets.Length;
            foreach (var p in scalePresets)
            {
                var pBtn = new Button
                {
                    Text = $"{p}%",
                    Font = new Font("Segoe UI", 8.25f),
                    Size = new Size(scW, 26),
                    Location = new Point(scX, 58),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = (_settings.UiScalePercent == p) ? Color.FromArgb(56, 189, 248) : Color.FromArgb(32, 32, 40),
                    ForeColor = (_settings.UiScalePercent == p) ? Color.FromArgb(18, 18, 24) : Color.FromArgb(170, 170, 185),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Padding = Padding.Empty,
                    Cursor = Cursors.Hand,
                    Tag = p
                };
                pBtn.FlatAppearance.BorderSize = 0;
                int target = p;
                pBtn.Click += (s, e) => {
                    if (_settings.AutoUiScale)
                    {
                        _settings.AutoUiScale = false;
                        if (_chkAutoUiScale != null) _chkAutoUiScale.Checked = false;
                    }
                    _sliderUiScale.Value = target;
                };
                cardUiScale.Controls.Add(pBtn);
                _uiScalePresetButtons.Add(pBtn);
                scX += scW + 4;
            }

            _chkAutoUiScale = CreateCheckbox(Loc.Get("UiScaleAuto"), 14, 92, _settings.AutoUiScale, v => {
                _settings.AutoUiScale = v;
                _settings.Save();
                UpdateUiScaleButtonsHighlight();
                _onSettingsUpdated?.Invoke();
            });
            cardUiScale.Controls.Add(_chkAutoUiScale);

            curY += cardUiScaleH + 8;

            // ================= 4. SYSTEM & OPACITY CARD =================
            // Clear separation between slider presets and checkboxes
            var cardSys = CreateCard(cardX, curY, cardW, 220);
            _pnlBody.Controls.Add(cardSys);

            _lblSecSystem = CreateSectionHeader(Loc.Get("Opacity"), 12, 8);
            cardSys.Controls.Add(_lblSecSystem);
            RegisterCardSection("sys", cardSys, _lblSecSystem, () => Loc.Get("Opacity"), 220);

            _lblOpacityVal = new Label
            {
                Text = $"{(int)Math.Round(_settings.WidgetOpacity * 100)}%",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                Location = new Point(cardW - 75, 8),
                Size = new Size(65, 20),
                TextAlign = ContentAlignment.MiddleRight
            };
            cardSys.Controls.Add(_lblOpacityVal);

            _sliderOpacity = new TrackBar
            {
                AutoSize = false,
                Minimum = 30,
                Maximum = 100,
                Value = Math.Clamp((int)Math.Round(_settings.WidgetOpacity * 100), 30, 100),
                TickFrequency = 10,
                SmallChange = 5,
                LargeChange = 10,
                Size = new Size(cardW - 24, 22),
                Location = new Point(12, 30),
                Cursor = Cursors.Hand
            };
            _sliderOpacity.ValueChanged += (s, e) => {
                int val = _sliderOpacity.Value;
                _lblOpacityVal.Text = $"{val}%";
                _settings.WidgetOpacity = val / 100.0;
                _settings.Save();
                if (this.Owner != null) this.Owner.Opacity = _settings.WidgetOpacity;
            };
            cardSys.Controls.Add(_sliderOpacity);

            // Presets row (100%, 95%, 90%, 80%, 70%) - increased height and padding to prevent clipping
            int[] presets = new[] { 100, 95, 90, 80, 70 };
            int pX = 12;
            int pW = (cardW - 24 - (presets.Length - 1) * 4) / presets.Length;
            foreach (var p in presets)
            {
                var pBtn = new Button
                {
                    Text = $"{p}%",
                    Font = new Font("Segoe UI", 8.25f),
                    Size = new Size(pW, 26),
                    Location = new Point(pX, 58),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(32, 32, 40),
                    ForeColor = Color.FromArgb(170, 170, 185),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Padding = Padding.Empty,
                    Cursor = Cursors.Hand
                };
                pBtn.FlatAppearance.BorderSize = 0;
                int target = p;
                pBtn.Click += (s, e) => _sliderOpacity.Value = target;
                cardSys.Controls.Add(pBtn);
                _opacityPresetButtons.Add(pBtn);
                pX += pW + 4;
            }

            // Divider between presets and checkboxes
            var sepSys = new Panel
            {
                Location = new Point(12, 92),
                Size = new Size(cardW - 24, 1),
                BackColor = Color.FromArgb(40, 40, 52)
            };
            cardSys.Controls.Add(sepSys);
            _cardDividers.Add(sepSys);

            // System Checkboxes - generous Y buffer to prevent any overlap
            int sysChkY = 104;
            int sysGap = 26;

            _chkAlwaysOnTop = CreateCheckbox(Loc.Get("AlwaysOnTop"), 14, sysChkY, _settings.AlwaysOnTop, v => {
                _settings.AlwaysOnTop = v;
                if (this.Owner != null) this.Owner.TopMost = v;
                this.TopMost = v;
            });
            _chkAutoQuiet = CreateCheckbox(Loc.Get("AutoQuiet"), 14, sysChkY + sysGap, _settings.AutoQuietOnIdle, v => {
                _settings.AutoQuietOnIdle = v;
            });
            _chkStartup = CreateCheckbox(Loc.Get("Startup"), 14, sysChkY + (sysGap * 2), StartupManager.IsStartupEnabled(), v => {
                StartupManager.SetStartup(v, _settings.StartMinimized);
            });
            _chkStartMinimized = CreateCheckbox(Loc.Get("StartMinimized"), 14, sysChkY + (sysGap * 3), _settings.StartMinimized, v => {
                _settings.StartMinimized = v;
                _settings.Save();
                if (StartupManager.IsStartupEnabled())
                {
                    StartupManager.UpdateStartupArgs(v);
                }
            });
            if (_toolTip != null)
            {
                _toolTip.SetToolTip(_chkStartMinimized, Loc.Get("StartMinimizedTip"));
            }

            cardSys.Controls.AddRange(new Control[] { _chkAlwaysOnTop, _chkAutoQuiet, _chkStartup, _chkStartMinimized });

            curY += 228;

            // Apply initial collapse state: all cards collapsed by default
            ApplyInitialCollapseState();

            // Bottom spacer for scrollable body
            _pnlBottomSpacer = new Panel
            {
                Location = new Point(cardX, curY),
                Size = new Size(cardW, 14),
                BackColor = Color.Transparent
            };
            _pnlBody.Controls.Add(_pnlBottomSpacer);
        }

        private Panel CreateCard(int x, int y, int w, int h)
        {
            var pnl = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
                BackColor = Color.FromArgb(32, 32, 42)
            };
            pnl.Paint += (s, e) => {
                using var borderPen = new Pen(Color.FromArgb(52, 52, 68), 1f);
                e.Graphics.DrawRectangle(borderPen, 0, 0, pnl.Width - 1, pnl.Height - 1);
            };
            _allCards.Add(pnl);
            return pnl;
        }

        private Label CreateSectionHeader(string text, int x, int y)
        {
            return new Label
            {
                Text = text.TrimEnd(':'),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                Location = new Point(x, y),
                AutoSize = true,
                UseMnemonic = false
            };
        }

        private CheckBox CreateCheckbox(string text, int x, int y, bool isChecked, Action<bool> onToggle)
        {
            var chk = new CheckBox
            {
                Text = text,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(220, 220, 235),
                Location = new Point(x, y),
                AutoSize = true,
                Checked = isChecked,
                Cursor = Cursors.Hand
            };
            chk.CheckedChanged += (s, e) => {
                onToggle(chk.Checked);
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            };
            return chk;
        }

        private void SelectLanguage(string code)
        {
            _settings.Language = code;
            _settings.Save();
            Loc.SetLanguage(code);
            UpdateLangButtonsHighlight();
            _onSettingsUpdated?.Invoke();
        }

        private void UpdateLangButtonsHighlight()
        {
            string cur = Loc.CurrentLanguage;
            foreach (var btn in _langButtons)
            {
                string code = (string)btn.Tag!;
                bool active = (code == cur);
                btn.BackColor = active ? Color.FromArgb(14, 165, 233) : Color.FromArgb(32, 32, 40);
                btn.ForeColor = active ? Color.White : Color.FromArgb(170, 170, 190);
            }
        }

        private void UpdateTrayButtonsHighlight()
        {
            foreach (var btn in _trayButtons)
            {
                var opt = ((string labelKey, string tipKey, int metricVal, bool isLive))btn.Tag!;
                bool active;
                if (!opt.isLive)
                {
                    active = !_settings.LiveTrayIcon;
                }
                else
                {
                    active = _settings.LiveTrayIcon && _settings.TrayMetric == opt.metricVal;
                }

                btn.BackColor = active ? Color.FromArgb(14, 165, 233) : Color.FromArgb(32, 32, 40);
                btn.ForeColor = active ? Color.White : Color.FromArgb(170, 170, 190);
            }
        }

        private void UpdateSwSourceButtonsHighlight()
        {
            int current = _settings.StopwatchTriggerSource;
            int watts = _settings.StopwatchAutoStartWatts;

            foreach (var btn in _swSourceButtons)
            {
                if (btn.Tag is int val && val == current)
                {
                    btn.BackColor = Color.FromArgb(245, 158, 11);
                    btn.ForeColor = Color.FromArgb(18, 18, 22);
                }
                else
                {
                    btn.BackColor = Color.FromArgb(32, 32, 40);
                    btn.ForeColor = Color.FromArgb(170, 170, 185);
                }

                if (btn.Tag is int tVal)
                {
                    string tip = tVal switch
                    {
                        1 => string.Format(Loc.Get("StopwatchDescCpu"), watts),
                        2 => string.Format(Loc.Get("StopwatchDescGpu"), watts),
                        3 => string.Format(Loc.Get("StopwatchDescTotal"), watts),
                        0 => string.Format(Loc.Get("StopwatchDescMax"), watts),
                        _ => ""
                    };
                    _toolTip.SetToolTip(btn, tip);
                }
            }

            if (_lblSwSourceDesc != null)
            {
                _lblSwSourceDesc.Text = current switch
                {
                    1 => string.Format(Loc.Get("StopwatchDescCpu"), watts),
                    2 => string.Format(Loc.Get("StopwatchDescGpu"), watts),
                    3 => string.Format(Loc.Get("StopwatchDescTotal"), watts),
                    0 => string.Format(Loc.Get("StopwatchDescMax"), watts),
                    _ => string.Format(Loc.Get("StopwatchDescMax"), watts)
                };
            }
        }

        private void PopulateStopwatchList(Panel pnlList, int width)
        {
            pnlList.SuspendLayout();
            pnlList.Controls.Clear();

            int rowY = 0;
            int rowH = 34;
            int gap = 4;

            bool willScrollV = (_settings.Stopwatches.Count * (rowH + gap)) > pnlList.Height;
            int vScrollW = willScrollV ? SystemInformation.VerticalScrollBarWidth + 2 : 0;
            int rowW = Math.Max(180, width - vScrollW - 4);

            for (int i = 0; i < _settings.Stopwatches.Count; i++)
            {
                var profile = _settings.Stopwatches[i];
                var row = new Panel
                {
                    Location = new Point(0, rowY),
                    Size = new Size(rowW, rowH),
                    BackColor = Color.FromArgb(26, 26, 36)
                };

                var colorBar = new Panel
                {
                    Location = new Point(4, 5),
                    Size = new Size(6, rowH - 10),
                    BackColor = StopwatchItem.ParseColor(profile.ColorHex, Color.FromArgb(52, 211, 153))
                };
                row.Controls.Add(colorBar);

                var currentProfile = profile;
                var chkVisible = new CheckBox
                {
                    Checked = profile.IsVisible,
                    Location = new Point(14, 8),
                    Size = new Size(18, 18),
                    Cursor = Cursors.Hand,
                    BackColor = Color.Transparent
                };
                _toolTip.SetToolTip(chkVisible, Loc.Get("StopwatchVisibleTip"));
                row.Controls.Add(chkVisible);

                string targetDesc = string.IsNullOrWhiteSpace(profile.TargetExe)
                    ? Loc.Get("StopwatchAnyProgram")
                    : (!string.IsNullOrWhiteSpace(profile.TargetFriendlyName) ? profile.TargetFriendlyName : profile.TargetExe);

                if (profile.ShowOnCpuGraph && profile.GraphScale != -1)
                {
                    string scaleTag = profile.GraphScale switch
                    {
                        0 => Loc.Get("StopwatchScale_Auto"),
                        1 => "1x",
                        _ => $"x{profile.GraphScale}"
                    };
                    targetDesc += $" [📈 {scaleTag}]";
                }

                bool hasCustomName = !string.IsNullOrWhiteSpace(profile.Name) &&
                    !profile.Name.Equals(profile.TargetFriendlyName, StringComparison.OrdinalIgnoreCase) &&
                    !profile.Name.Equals(profile.TargetExe, StringComparison.OrdinalIgnoreCase);

                int btnDelW = 26;
                int btnEditW = 68;
                int btnDelX = rowW - btnDelW - 4;
                int btnEditX = btnDelX - btnEditW - 6;
                int textStartX = 36;
                int textAvailW = Math.Max(40, btnEditX - textStartX - 8);

                Label lblName;
                Label? lblTarget = null;

                if (hasCustomName)
                {
                    int nameW = Math.Min(90, textAvailW / 2);
                    lblName = new Label
                    {
                        Text = profile.Name,
                        Location = new Point(textStartX, 7),
                        Size = new Size(nameW, 20),
                        Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                        ForeColor = profile.IsVisible ? Color.FromArgb(240, 240, 248) : Color.FromArgb(120, 120, 135),
                        AutoEllipsis = true
                    };
                    _toolTip.SetToolTip(lblName, profile.Name);
                    row.Controls.Add(lblName);

                    lblTarget = new Label
                    {
                        Text = targetDesc,
                        Location = new Point(textStartX + nameW + 6, 7),
                        Size = new Size(Math.Max(30, textAvailW - nameW - 6), 20),
                        Font = new Font("Segoe UI", 8.0f),
                        ForeColor = profile.IsVisible ? Color.FromArgb(56, 189, 248) : Color.FromArgb(90, 100, 115),
                        AutoEllipsis = true
                    };
                    _toolTip.SetToolTip(lblTarget, targetDesc);
                    row.Controls.Add(lblTarget);
                }
                else
                {
                    lblName = new Label
                    {
                        Text = targetDesc,
                        Location = new Point(textStartX, 7),
                        Size = new Size(textAvailW, 20),
                        Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                        ForeColor = profile.IsVisible ? Color.FromArgb(240, 240, 248) : Color.FromArgb(120, 120, 135),
                        AutoEllipsis = true
                    };
                    _toolTip.SetToolTip(lblName, targetDesc);
                    row.Controls.Add(lblName);
                }

                chkVisible.CheckedChanged += (s, e) => {
                    currentProfile.IsVisible = chkVisible.Checked;
                    _settings.Save();
                    _hardware.Stopwatch.SyncWithSettings(_settings);
                    _onSettingsUpdated?.Invoke();

                    lblName.ForeColor = currentProfile.IsVisible ? Color.FromArgb(240, 240, 248) : Color.FromArgb(120, 120, 135);
                    if (lblTarget != null)
                    {
                        lblTarget.ForeColor = currentProfile.IsVisible ? Color.FromArgb(56, 189, 248) : Color.FromArgb(90, 100, 115);
                    }
                };

                var btnEdit = new Button
                {
                    Text = Loc.Get("StopwatchEdit"),
                    Location = new Point(btnEditX, 3),
                    Size = new Size(btnEditW, 28),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(38, 38, 50),
                    ForeColor = Color.FromArgb(220, 220, 235),
                    Font = new Font("Segoe UI", 8.0f),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Padding = Padding.Empty,
                    Cursor = Cursors.Hand
                };
                btnEdit.FlatAppearance.BorderSize = 1;
                btnEdit.FlatAppearance.BorderColor = Color.FromArgb(55, 55, 70);
                btnEdit.Click += (s, e) => {
                    using var dlg = new StopwatchEditDialog(currentProfile, _hardware.Processes)
                    {
                        TopMost = this.TopMost
                    };
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        _settings.Save();
                        _hardware.Stopwatch.SyncWithSettings(_settings);
                        PopulateStopwatchList(pnlList, width);
                        _onSettingsUpdated?.Invoke();
                    }
                };
                row.Controls.Add(btnEdit);

                var btnDel = new Button
                {
                    Text = "✕",
                    Location = new Point(btnDelX, 3),
                    Size = new Size(btnDelW, 28),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(38, 38, 50),
                    ForeColor = Color.FromArgb(244, 63, 94),
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Padding = Padding.Empty,
                    Cursor = Cursors.Hand,
                    Enabled = _settings.Stopwatches.Count > 1
                };
                btnDel.FlatAppearance.BorderSize = 1;
                btnDel.FlatAppearance.BorderColor = Color.FromArgb(55, 55, 70);
                btnDel.Click += (s, e) => {
                    if (_settings.Stopwatches.Count > 1)
                    {
                        _settings.Stopwatches.Remove(currentProfile);
                        _settings.Save();
                        _hardware.Stopwatch.SyncWithSettings(_settings);
                        PopulateStopwatchList(pnlList, width);
                        _onSettingsUpdated?.Invoke();
                    }
                };
                row.Controls.Add(btnDel);

                pnlList.Controls.Add(row);
                rowY += rowH + gap;
            }

            pnlList.HorizontalScroll.Enabled = false;
            pnlList.HorizontalScroll.Visible = false;
            pnlList.ResumeLayout(true);
        }

        private void RelayoutCards()
        {
            if (_pnlBody == null || _allCards.Count == 0) return;

            int w = this.ClientSize.Width;
            int cardMargin = 14;
            int scrollbarAllowance = 22;
            int cardW = Math.Max(260, w - (cardMargin * 2) - scrollbarAllowance);

            // Resize each card panel
            foreach (var card in _allCards)
            {
                card.Width = cardW;
                card.Invalidate();
            }

            // Language Card elements
            if (_langButtons.Count > 0)
            {
                int langGap = 3;
                int btnLangW = Math.Max(24, (cardW - 26 - (_langButtons.Count - 1) * langGap) / _langButtons.Count);
                int btnLangX = 13;
                for (int i = 0; i < _langButtons.Count; i++)
                {
                    _langButtons[i].Width = btnLangW;
                    _langButtons[i].Left = btnLangX;
                    btnLangX += btnLangW + langGap;
                }
            }

            // GPU Card elements
            if (_tbGpuPowerLimit != null) _tbGpuPowerLimit.Width = cardW - 24;
            if (_tbGpuFanCap != null) _tbGpuFanCap.Width = cardW - 24;
            if (_lblGpuFanCap != null) _lblGpuFanCap.Left = cardW - 75;
            if (_pnlFailSafe != null) _pnlFailSafe.Width = cardW - 24;

            if (_gpuPowerPresetButtons.Count > 0)
            {
                int pwBtnW = (cardW - 24 - (_gpuPowerPresetButtons.Count - 1) * 4) / _gpuPowerPresetButtons.Count;
                for (int i = 0; i < _gpuPowerPresetButtons.Count; i++)
                {
                    _gpuPowerPresetButtons[i].Width = pwBtnW;
                    _gpuPowerPresetButtons[i].Left = 12 + i * (pwBtnW + 4);
                }
            }

            if (_gpuFanPresetButtons.Count > 0)
            {
                int fanBtnW = (cardW - 24 - (_gpuFanPresetButtons.Count - 1) * 4) / _gpuFanPresetButtons.Count;
                for (int i = 0; i < _gpuFanPresetButtons.Count; i++)
                {
                    _gpuFanPresetButtons[i].Width = fanBtnW;
                    _gpuFanPresetButtons[i].Left = 12 + i * (fanBtnW + 4);
                }
            }

            // Stopwatch Card elements
            if (_tbSwStartWatts != null) _tbSwStartWatts.Width = cardW - 28;
            if (_tbSwStopWatts != null) _tbSwStopWatts.Width = cardW - 28;
            if (_tbSwHysteresis != null) _tbSwHysteresis.Width = cardW - 28;
            if (_lblSwSourceDesc != null) _lblSwSourceDesc.Width = cardW - 28;
            if (_btnSwAdd != null) _btnSwAdd.Left = cardW - 14 - _btnSwAdd.Width;
            if (_pnlSwListContainer != null)
            {
                _pnlSwListContainer.Width = cardW - 28;
                PopulateStopwatchList(_pnlSwListContainer, cardW - 28);
            }

            if (_swSourceButtons.Count > 0)
            {
                int swBtnW = (cardW - 28 - (_swSourceButtons.Count - 1) * 4) / _swSourceButtons.Count;
                for (int i = 0; i < _swSourceButtons.Count; i++)
                {
                    _swSourceButtons[i].Width = swBtnW;
                    _swSourceButtons[i].Left = 14 + i * (swBtnW + 4);
                }
            }


            // Graphs Card elements
            if (_btnResetGraphs != null) _btnResetGraphs.Width = cardW - 28;
            if (_cpuScaleButtons.Count > 0)
            {
                int scStartX = 14 + 16;
                int scAvailW = cardW - 28 - 16;
                int scBtnGap = 4;
                int scBtnW = Math.Max(24, (scAvailW - (_cpuScaleButtons.Count - 1) * scBtnGap) / _cpuScaleButtons.Count);
                int scaleBtnsY = 32 + (26 * 1) + 24 + 22;
                for (int i = 0; i < _cpuScaleButtons.Count; i++)
                {
                    _cpuScaleButtons[i].Width = (i == _cpuScaleButtons.Count - 1)
                        ? (scAvailW - i * (scBtnW + scBtnGap))
                        : scBtnW;
                    _cpuScaleButtons[i].Left = scStartX + i * (scBtnW + scBtnGap);
                    _cpuScaleButtons[i].Top = scaleBtnsY;
                    _cpuScaleButtons[i].Height = 24;
                }
            }
            if (_btnFanToggle != null) _btnFanToggle.Left = cardW - 140;
            if (_btnFanFolder != null) _btnFanFolder.Left = cardW - 30;
            if (_chkFanGraph != null) _chkFanGraph.Width = cardW - 148;
            if (_tbFanScale != null) _tbFanScale.Width = Math.Max(100, cardW - 148 - 14);

            if (_btnFanModeGraph != null && _btnFanModeIcons != null && _btnFanModeGrid != null)
            {
                int totalBtnSpace = cardW - 28;
                int btnGap = 5;
                int modeBtnW = (totalBtnSpace - (btnGap * 2)) / 3;
                _btnFanModeGraph.Width = modeBtnW;
                _btnFanModeIcons.Width = modeBtnW;
                _btnFanModeIcons.Left = 14 + modeBtnW + btnGap;
                _btnFanModeGrid.Left = 14 + (modeBtnW + btnGap) * 2;
                _btnFanModeGrid.Width = totalBtnSpace - ((modeBtnW + btnGap) * 2);
            }

            // Tray buttons
            if (_trayButtons.Count > 0)
            {
                int trayBtnW = (cardW - 28 - (_trayButtons.Count - 1) * 6) / _trayButtons.Count;
                for (int i = 0; i < _trayButtons.Count; i++)
                {
                    _trayButtons[i].Width = trayBtnW;
                    _trayButtons[i].Left = 14 + i * (trayBtnW + 6);
                }
            }

            // UI Scale elements
            if (_sliderUiScale != null) _sliderUiScale.Width = cardW - 24;
            if (_lblUiScaleVal != null) _lblUiScaleVal.Left = cardW - 75;
            if (_uiScalePresetButtons.Count > 0)
            {
                int scW = (cardW - 24 - (_uiScalePresetButtons.Count - 1) * 4) / _uiScalePresetButtons.Count;
                for (int i = 0; i < _uiScalePresetButtons.Count; i++)
                {
                    _uiScalePresetButtons[i].Width = scW;
                    _uiScalePresetButtons[i].Left = 12 + i * (scW + 4);
                }
            }

            // Opacity & System elements
            if (_sliderOpacity != null) _sliderOpacity.Width = cardW - 24;
            if (_lblOpacityVal != null) _lblOpacityVal.Left = cardW - 75;

            if (_opacityPresetButtons.Count > 0)
            {
                int pW = (cardW - 24 - (_opacityPresetButtons.Count - 1) * 4) / _opacityPresetButtons.Count;
                for (int i = 0; i < _opacityPresetButtons.Count; i++)
                {
                    _opacityPresetButtons[i].Width = pW;
                    _opacityPresetButtons[i].Left = 12 + i * (pW + 4);
                }
            }

            foreach (var div in _cardDividers)
            {
                div.Width = cardW - 24;
            }

            // HDD Awake Schedule elements
            if (_pnlHddControls != null) _pnlHddControls.Width = cardW - 28;
            if (_cbHddEnd != null) _cbHddEnd.Left = cardW - 28 - 72;
            if (_lblHddDash != null) _lblHddDash.Left = cardW - 28 - 84;
            if (_cbHddStart != null) _cbHddStart.Left = cardW - 28 - 156;
            if (_hddOffHoursPresetButtons.Count > 0)
            {
                int totalHddBtnsW = cardW - 28;
                int btnGap = 5;
                int hddBtnW = (totalHddBtnsW - (_hddOffHoursPresetButtons.Count - 1) * btnGap) / _hddOffHoursPresetButtons.Count;
                for (int i = 0; i < _hddOffHoursPresetButtons.Count; i++)
                {
                    int btnLeft = i * (hddBtnW + btnGap);
                    _hddOffHoursPresetButtons[i].Left = btnLeft;
                    _hddOffHoursPresetButtons[i].Top = 88;
                    _hddOffHoursPresetButtons[i].Width = (i == _hddOffHoursPresetButtons.Count - 1)
                        ? (totalHddBtnsW - btnLeft)
                        : hddBtnW;
                    _hddOffHoursPresetButtons[i].Height = 26;
                }
            }
            if (_sepHdd != null) _sepHdd.Width = cardW - 28;
            if (_lblHddWakeStatus != null)
            {
                _lblHddWakeStatus.Left = 215;
                _lblHddWakeStatus.Width = Math.Max(20, cardW - 28 - 215);
            }

            // ServerSync Card elements
            if (_pnlServerSyncControls != null)
            {
                _pnlServerSyncControls.Width = cardW - 28;
                if (_lblServerSyncTestStatus != null) _lblServerSyncTestStatus.Width = Math.Max(50, cardW - 28 - 140);
            }

            // Keyboard Layout Switcher Card elements
            if (_pnlLayoutControls != null)
            {
                _pnlLayoutControls.Width = cardW - 28;
                if (_chkLayoutSwitch != null) _chkLayoutSwitch.Width = cardW - 28;
                if (_flowModifiers != null) _flowModifiers.Width = cardW - 28;
                if (_flowKeyRow != null) _flowKeyRow.Width = cardW - 28;
                if (_lblLayoutSwitchHint != null) _lblLayoutSwitchHint.Width = cardW - 28;
            }

            // Desktop Icons Card elements
            if (_pnlDesktopIconsControls != null)
            {
                _pnlDesktopIconsControls.Width = cardW - 28;
                int diBtnGap = 8;
                int diBtnW = (cardW - 28 - diBtnGap) / 2;
                if (_btnDesktopIconsSave != null) _btnDesktopIconsSave.Width = diBtnW;
                if (_btnDesktopIconsRestore != null)
                {
                    _btnDesktopIconsRestore.Left = diBtnW + diBtnGap;
                    _btnDesktopIconsRestore.Width = cardW - 28 - diBtnW - diBtnGap;
                }
                if (_chkDesktopIconsAutoRestore != null) _chkDesktopIconsAutoRestore.Width = cardW - 28;
                if (_lblDesktopIconsStatus != null) _lblDesktopIconsStatus.Width = cardW - 28;
                if (_lblDesktopIconsHint != null) _lblDesktopIconsHint.Width = cardW - 28;
            }

            // Translate & LLM Card elements
            if (_pnlTranslateControls != null)
            {
                _pnlTranslateControls.Width = cardW - 28;
                int testBtnW = 88;
                if (_lblTranslateApiUrl != null) _lblTranslateApiUrl.Width = Math.Max(100, cardW - 28 - 200);
                if (_btnPresetLms != null) _btnPresetLms.Left = cardW - 28 - 196;
                if (_btnPresetOllama != null) _btnPresetOllama.Left = cardW - 28 - 95;
                if (_btnTranslateCheck != null) _btnTranslateCheck.Left = cardW - 28 - testBtnW;
                if (_tbTranslateUrl != null) _tbTranslateUrl.Width = cardW - 28 - testBtnW - 8;
                if (_lblTranslateCheckStatus != null) _lblTranslateCheckStatus.Width = cardW - 28;

                int modelW = (int)((cardW - 28 - 12) * 0.55);
                int langW = cardW - 28 - 12 - modelW;
                if (_lblTranslateModel != null) _lblTranslateModel.Width = modelW;
                if (_lblTranslateLang != null)
                {
                    _lblTranslateLang.Left = modelW + 12;
                    _lblTranslateLang.Width = langW;
                }
                if (_cbTranslateModel != null) _cbTranslateModel.Width = modelW;
                if (_cbTranslateTargetLang != null)
                {
                    _cbTranslateTargetLang.Left = modelW + 12;
                    _cbTranslateTargetLang.Width = langW;
                }

                int timeoutW = 90;
                int timeoutLblW = 75;
                if (_cbTranslateVramTimeout != null) _cbTranslateVramTimeout.Left = cardW - 28 - timeoutW;
                if (_lblTranslateVramTimeout != null) _lblTranslateVramTimeout.Left = cardW - 28 - timeoutW - timeoutLblW - 4;
                if (_chkTranslateAutoCopy != null) _chkTranslateAutoCopy.Width = cardW - 28;
                if (_sepTransHotkeys != null) _sepTransHotkeys.Width = cardW - 28;
                if (_rowHk1 != null) _rowHk1.Width = cardW - 28;
                if (_rowHk2 != null) _rowHk2.Width = cardW - 28;
                if (_rowHk3 != null) _rowHk3.Width = cardW - 28;
            }

            // Footer buttons layout
            int footerBtnX = cardMargin;
            int fBtnW = 36;
            int fBtnGap = 6;
            if (_btnSpecs != null)
            {
                _btnSpecs.Size = new Size(fBtnW, 32);
                _btnSpecs.Location = new Point(footerBtnX, 6);
            }
            if (_btnTools != null)
            {
                _btnTools.Size = new Size(fBtnW, 32);
                _btnTools.Location = new Point(footerBtnX + (fBtnW + fBtnGap), 6);
            }
            if (_btnExpandAll != null)
            {
                _btnExpandAll.Size = new Size(fBtnW, 32);
                _btnExpandAll.Location = new Point(footerBtnX + (fBtnW + fBtnGap) * 2, 6);
            }
            if (_btnCollapseAll != null)
            {
                _btnCollapseAll.Size = new Size(fBtnW, 32);
                _btnCollapseAll.Location = new Point(footerBtnX + (fBtnW + fBtnGap) * 3, 6);
            }
            if (_btnClose != null)
            {
                int closeW = 90;
                _btnClose.Size = new Size(closeW, 32);
                _btnClose.Location = new Point(footerBtnX + cardW - closeW, 6);
            }

            if (_pnlBottomSpacer != null)
            {
                _pnlBottomSpacer.Left = cardMargin;
                _pnlBottomSpacer.Width = cardW;
            }

            ReflowAllCardSections();

            _pnlBody.HorizontalScroll.Enabled = false;
            _pnlBody.HorizontalScroll.Visible = false;
            _pnlBody.HorizontalScroll.Maximum = 0;
        }

        private void RefreshLocalizedTexts()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(RefreshLocalizedTexts));
                return;
            }

            _lblHeaderTitle.Text = Loc.Get("MenuSettings").TrimEnd(':');
            _lblSecLang.Text = Loc.Get("Language").TrimEnd(':');
            _lblSecGraphs.Text = Loc.Get("MenuGraphs").TrimEnd(':');
            _lblSecTray.Text = Loc.Get("TrayIconMenu").TrimEnd(':');
            if (_lblSecUiScale != null) _lblSecUiScale.Text = Loc.Get("UiScale").TrimEnd(':');
            if (_chkAutoUiScale != null) _chkAutoUiScale.Text = Loc.Get("UiScaleAuto");
            _lblSecSystem.Text = Loc.Get("Opacity").TrimEnd(':');

            if (_chkStopwatch != null) _chkStopwatch.Text = Loc.Get("StopwatchModule");
            _chkCpuGraph.Text = Loc.Get("CpuGraph");
            _chkCpuCores.Text = Loc.Get("CpuCores");
            _chkTopProcesses.Text = Loc.Get("TopProcesses");
            _chkRamGraph.Text = Loc.Get("RamGraph");
            _chkGpuGraph.Text = Loc.Get("GpuGraph");
            _chkGpuTemp.Text = Loc.Get("GpuTemp");
            _chkGpuFan.Text = Loc.Get("GpuFan");
            _chkVramGraph.Text = Loc.Get("VramGraph");
            if (_chkTopVramProcesses != null) _chkTopVramProcesses.Text = Loc.Get("VramProcesses");
            _chkDiskGraph.Text = Loc.Get("DiskGraph");
            _chkEnhancedDiskMode.Text = Loc.Get("EnhancedDiskMode");
            _chkFanGraph.Text = Loc.Get("FanGraph");

            if (_lblSecStopwatch != null) _lblSecStopwatch.Text = "⏱ " + Loc.Get("StopwatchSettings");
            if (_chkStopwatchAuto != null) _chkStopwatchAuto.Text = Loc.Get("StopwatchAutoStartEnable");
            if (_lblSwSourceTitle != null) _lblSwSourceTitle.Text = Loc.Get("StopwatchTriggerSource");
            if (_lblSwStartWatts != null) _lblSwStartWatts.Text = $"{Loc.Get("StopwatchAutoStartWatts")}: {_settings.StopwatchAutoStartWatts}W";
            if (_lblSwStopWatts != null) _lblSwStopWatts.Text = $"{Loc.Get("StopwatchAutoStopWatts")}: {_settings.StopwatchAutoStopWatts}W";
            if (_lblSwHysteresis != null) _lblSwHysteresis.Text = $"{Loc.Get("StopwatchHysteresis")}: {_settings.StopwatchHysteresisSeconds} {Loc.Get("Sec")}";
            if (_toolTip != null && _tbSwHysteresis != null) _toolTip.SetToolTip(_tbSwHysteresis, Loc.Get("StopwatchHysteresisTip"));
            if (_toolTip != null && _lblSwHysteresis != null) _toolTip.SetToolTip(_lblSwHysteresis, Loc.Get("StopwatchHysteresisTip"));
            if (_lblSwListTitle != null) _lblSwListTitle.Text = "⏱ " + Loc.Get("StopwatchListTitle");
            if (_btnSwAdd != null) _btnSwAdd.Text = Loc.Get("StopwatchAdd");
            if (_pnlSwListContainer != null) PopulateStopwatchList(_pnlSwListContainer, _pnlSwListContainer.Width);

            foreach (var btn in _swSourceButtons)
            {
                if (btn.Tag is int val)
                {
                    btn.Text = val switch
                    {
                        1 => "CPU",
                        2 => "GPU",
                        3 => "CPU + GPU",
                        0 => Loc.Get("StopwatchSourceMax"),
                        _ => "CPU"
                    };
                }
            }
            UpdateSwSourceButtonsHighlight();
            _btnFanModeGraph.Text = Loc.Get("FanVisualMode_Graph");
            _btnFanModeIcons.Text = Loc.Get("FanVisualMode_Icons");
            _btnFanModeGrid.Text = Loc.Get("FanVisualMode_Grid");
            _chkFanDemo.Text = Loc.Get("FanDemoMode");
            if (_toolTip != null) _toolTip.SetToolTip(_chkFanDemo, Loc.Get("FanDemoModeTooltip"));
            _lblFanScale.Text = $"{Loc.Get("FanScale")}: {_settings.FanScalePercent}%";
            if (_toolTip != null && _tbFanScale != null) _toolTip.SetToolTip(_tbFanScale, Loc.Get("FanScaleTip"));
            if (_toolTip != null && _lblFanScale != null) _toolTip.SetToolTip(_lblFanScale, Loc.Get("FanScaleTip"));
            if (_btnFanSelect != null) _btnFanSelect.Text = Loc.Get("FanSelectFans");
            if (_toolTip != null && _btnFanSelect != null) _toolTip.SetToolTip(_btnFanSelect, Loc.Get("FanSelectionTitle"));
            if (_toolTip != null && _btnFanFolder != null) _toolTip.SetToolTip(_btnFanFolder, Loc.Get("FanPluginFolder"));
            if (_chkShowAllGpus != null) _chkShowAllGpus.Text = Loc.Get("ShowAllGpus");

            _btnResetGraphs.Text = "⚡ " + Loc.Get("ShowAllGraphs");
            if (_lblCpuScaleTitle != null) _lblCpuScaleTitle.Text = Loc.Get("CpuGraphScaleTitle");
            foreach (var btn in _cpuScaleButtons)
            {
                if (btn.Tag is int val && val == 0)
                {
                    btn.Text = Loc.Get("ScaleAuto");
                }
            }
            UpdateCpuScaleButtonsHighlight();

            foreach (var btn in _trayButtons)
            {
                var opt = ((string labelKey, string tipKey, int metricVal, bool isLive))btn.Tag!;
                btn.Text = opt.isLive ? $"{Loc.Get(opt.labelKey)} %" : Loc.Get(opt.labelKey);
                if (_toolTip != null) _toolTip.SetToolTip(btn, Loc.Get(opt.tipKey));
            }

            _chkAlwaysOnTop.Text = Loc.Get("AlwaysOnTop");
            _chkAutoQuiet.Text = Loc.Get("AutoQuiet");
            _chkStartup.Text = Loc.Get("Startup");
            if (_chkStartMinimized != null)
            {
                _chkStartMinimized.Text = Loc.Get("StartMinimized");
                if (_toolTip != null) _toolTip.SetToolTip(_chkStartMinimized, Loc.Get("StartMinimizedTip"));
            }

            if (_lblSecGpuTuning != null) _lblSecGpuTuning.Text = "⚡ " + Loc.Get("GpuTuningTitle");
            if (_chkGpuTuning != null) _chkGpuTuning.Text = Loc.Get("GpuTuningEnable");
            if (_chkSeparateCpuGpu != null) _chkSeparateCpuGpu.Text = Loc.Get("SeparateCpuGpu");
            if (_lblGpuPowerLimit != null) _lblGpuPowerLimit.Text = $"{Loc.Get("GpuPowerLimit")}: {_settings.GpuSilentPowerWatts}W";
            if (_toolTip != null && _lblGpuPowerLimit != null) _toolTip.SetToolTip(_lblGpuPowerLimit, Loc.Get("GpuPowerLimitTip"));
            if (_toolTip != null && _tbGpuPowerLimit != null) _toolTip.SetToolTip(_tbGpuPowerLimit, Loc.Get("GpuPowerLimitTip"));

            if (_chkGpuFanCap != null) _chkGpuFanCap.Text = Loc.Get("GpuFanCapEnable");
            if (_lblGpuFanCap != null) _lblGpuFanCap.Text = $"{_settings.GpuFanMaxPercent}%";
            if (_toolTip != null && _tbGpuFanCap != null) _toolTip.SetToolTip(_tbGpuFanCap, Loc.Get("GpuFanCapTip"));

            if (_lblGpuFailSafe != null)
            {
                _lblGpuFailSafe.Text = string.Format(Loc.Get("GpuFailSafeInfo"), _settings.GpuFailSafeTempC > 0 ? _settings.GpuFailSafeTempC : 83);
            }

            int stockWatts = _hardware.GpuTuning.StockWatts > 0 ? _hardware.GpuTuning.StockWatts : 336;
            (string key, int w)[] powerPresets = new[]
            {
                ("GpuPresetEco", 180),
                ("GpuPresetQuiet", 240),
                ("GpuPresetBalanced", 280),
                ("GpuPresetStock", stockWatts)
            };
            for (int i = 0; i < _gpuPowerPresetButtons.Count && i < powerPresets.Length; i++)
            {
                _gpuPowerPresetButtons[i].Text = $"{Loc.Get(powerPresets[i].key)} ({powerPresets[i].w}W)";
            }

            if (_lblSecHdd != null) _lblSecHdd.Text = Loc.Get("HddScheduleModule");
            if (_chkHddSchedule != null) _chkHddSchedule.Text = Loc.Get("HddScheduleEnable");
            if (_lblHddWorkHours != null) _lblHddWorkHours.Text = Loc.Get("HddWorkHours");
            if (_chkHddWorkDaysOnly != null) _chkHddWorkDaysOnly.Text = Loc.Get("HddWorkDaysOnly");
            if (_lblHddOffHours != null) _lblHddOffHours.Text = Loc.Get("HddOffHoursTimeout");
            if (_chkHddKeepAlive != null) _chkHddKeepAlive.Text = Loc.Get("HddKeepAlivePulse");
            if (_toolTip != null && _chkHddKeepAlive != null) _toolTip.SetToolTip(_chkHddKeepAlive, Loc.Get("HddKeepAliveTip"));
            if (_btnHddWakeNow != null) _btnHddWakeNow.Text = Loc.Get("HddWakeNow");
            if (_btnHddSleepNow != null) _btnHddSleepNow.Text = Loc.Get("HddSleepNow");
            if (_toolTip != null && _btnHddWakeNow != null) _toolTip.SetToolTip(_btnHddWakeNow, Loc.Get("HddWakeTip"));
            if (_toolTip != null && _btnHddSleepNow != null) _toolTip.SetToolTip(_btnHddSleepNow, Loc.Get("HddSleepTip"));
            if (_lblHddWakeStatus != null)
            {
                _lblHddWakeStatus.Text = _hardware.HddSchedule.GetStatusString();
                _lblHddWakeStatus.ForeColor = _hardware.HddSchedule.IsDisksAwake
                    ? Color.FromArgb(52, 211, 153)
                    : Color.FromArgb(160, 160, 175);
            }

            if (_btnSpecs != null)
            {
                _btnSpecs.Text = "ℹ";
                _toolTip?.SetToolTip(_btnSpecs, Loc.Get("TipInfo"));
            }
            if (_btnTools != null) _toolTip?.SetToolTip(_btnTools, "https://ph-cu-s.com/tools");
            if (_btnExpandAll != null)
            {
                _btnExpandAll.Text = "▼";
                _toolTip?.SetToolTip(_btnExpandAll, Loc.Get("TipSettingsExpandAll"));
            }
            if (_btnCollapseAll != null)
            {
                _btnCollapseAll.Text = "▲";
                _toolTip?.SetToolTip(_btnCollapseAll, Loc.Get("TipSettingsCollapseAll"));
            }
            if (_btnClose != null) _btnClose.Text = Loc.Get("InfoClose");

            if (_lblSecServerSync != null) _lblSecServerSync.Text = Loc.Get("ServerSync_Module");
            if (_chkServerSync != null) _chkServerSync.Text = Loc.Get("ServerSync_Enable");
            if (_lblServerSyncHost != null) _lblServerSyncHost.Text = Loc.Get("ServerSync_Host");
            if (_lblServerSyncPort != null) _lblServerSyncPort.Text = Loc.Get("ServerSync_Port");
            if (_btnServerSyncTest != null) _btnServerSyncTest.Text = Loc.Get("ServerSync_TestBtn");
            if (_lblServerSyncHint != null) _lblServerSyncHint.Text = Loc.Get("ServerSync_Hint");

            if (_lblSecLayout != null) _lblSecLayout.Text = Loc.Get("SecLayoutSwitcher");
            if (_chkLayoutSwitch != null) _chkLayoutSwitch.Text = Loc.Get("LayoutSwitch_Enable");
            if (_lblLayoutHotkey != null) _lblLayoutHotkey.Text = Loc.Get("LayoutSwitch_Hotkey");
            if (_lblLayoutKey != null) _lblLayoutKey.Text = Loc.Get("LayoutSwitch_Key");
            if (_btnResetLayoutHotkey != null)
            {
                _btnResetLayoutHotkey.Text = Loc.Get("LayoutSwitch_Reset");
                _toolTip?.SetToolTip(_btnResetLayoutHotkey, Loc.Get("LayoutSwitch_ResetTip"));
            }
            if (_lblLayoutSwitchHint != null) _lblLayoutSwitchHint.Text = Loc.Get("LayoutSwitch_Hint");

            if (_lblSecDesktopIcons != null) _lblSecDesktopIcons.Text = "🖥 " + Loc.Get("DesktopIcons_SecTitle");
            if (_btnDesktopIconsSave != null) _btnDesktopIconsSave.Text = Loc.Get("DesktopIcons_Save");
            if (_btnDesktopIconsRestore != null) _btnDesktopIconsRestore.Text = Loc.Get("DesktopIcons_Restore");
            if (_chkDesktopIconsAutoRestore != null) _chkDesktopIconsAutoRestore.Text = Loc.Get("DesktopIcons_AutoRestore");
            if (_lblDesktopIconsHint != null) _lblDesktopIconsHint.Text = Loc.Get("DesktopIcons_Hint");
            UpdateDesktopIconsUI();

            if (_lblSecTranslate != null) _lblSecTranslate.Text = "🌐 " + Loc.Get("Translate_Module").TrimEnd(':');
            if (_chkTranslateEnable != null) _chkTranslateEnable.Text = Loc.Get("Translate_Enable");
            if (_btnTranslateCheck != null) _btnTranslateCheck.Text = Loc.Get("Translate_CheckBtn");
            if (_btnFreeVramNow != null) _btnFreeVramNow.Text = "🧹 " + Loc.Get("Translate_FreeVramBtn");
            if (_chkTranslateAutoCopy != null) _chkTranslateAutoCopy.Text = Loc.Get("Translate_AutoCopy");

            foreach (var sec in _cardSections)
            {
                ApplySectionState(sec);
            }
            if (_lblServerSyncHost != null && _txtServerSyncHost != null && _lblServerSyncPort != null && _txtServerSyncPort != null)
            {
                int hostX = Math.Max(68, _lblServerSyncHost.Right + 8);
                _txtServerSyncHost.Location = new Point(hostX, 3);
                _lblServerSyncPort.Location = new Point(_txtServerSyncHost.Right + 12, 6);
                _txtServerSyncPort.Location = new Point(_lblServerSyncPort.Right + 8, 3);
            }

            UpdateFanModeUI();
            UpdateFanStatusUI();
            UpdateGpuTuningCardUI();
            UpdateHddCardUI();
            UpdateHddTimeoutButtonsHighlight();
            UpdateUiScaleButtonsHighlight();
        }

        private void UpdateUiScaleButtonsHighlight()
        {
            int activePercent = _settings.AutoUiScale 
                ? _settings.GetEffectiveUiScalePercent(Screen.FromControl(this)) 
                : _settings.UiScalePercent;

            if (_lblUiScaleVal != null)
            {
                _lblUiScaleVal.Text = _settings.AutoUiScale 
                    ? $"{activePercent}% ({Loc.Get("UiScaleAuto")})" 
                    : $"{activePercent}%";
            }

            foreach (var btn in _uiScalePresetButtons)
            {
                bool isSelected = (btn.Tag is int v && v == activePercent);
                btn.BackColor = isSelected ? Color.FromArgb(56, 189, 248) : Color.FromArgb(32, 32, 40);
                btn.ForeColor = isSelected ? Color.FromArgb(18, 18, 24) : Color.FromArgb(170, 170, 185);
            }
        }

        private void UpdateHddCardUI()
        {
            if (_chkHddSchedule == null) return;
            bool enabled = _chkHddSchedule.Checked;
            if (_pnlHddControls != null) _pnlHddControls.Enabled = enabled;
            if (_lblHddWakeStatus != null)
            {
                _lblHddWakeStatus.Text = _hardware.HddSchedule.GetStatusString();
                _lblHddWakeStatus.ForeColor = _hardware.HddSchedule.IsDisksAwake
                    ? Color.FromArgb(52, 211, 153)
                    : Color.FromArgb(160, 160, 175);
            }
        }

        private void UpdateHddTimeoutButtonsHighlight()
        {
            foreach (var btn in _hddOffHoursPresetButtons)
            {
                bool isSelected = (btn.Tag is int v && v == _settings.HddOffHoursTimeoutMinutes);
                btn.BackColor = isSelected ? Color.FromArgb(56, 189, 248) : Color.FromArgb(24, 24, 32);
                btn.ForeColor = isSelected ? Color.FromArgb(18, 18, 24) : Color.FromArgb(170, 170, 185);
            }
        }

        private void UpdateCpuScaleButtonsHighlight()
        {
            foreach (var btn in _cpuScaleButtons)
            {
                bool isSelected = (btn.Tag is int v && v == _settings.CpuGraphScale);
                if (isSelected)
                {
                    btn.BackColor = Color.FromArgb(38, 54, 75);
                    btn.ForeColor = Color.FromArgb(56, 189, 248);
                    btn.FlatAppearance.BorderColor = Color.FromArgb(56, 189, 248);
                }
                else
                {
                    btn.BackColor = Color.FromArgb(24, 24, 32);
                    btn.ForeColor = Color.FromArgb(140, 140, 155);
                    btn.FlatAppearance.BorderColor = Color.FromArgb(45, 45, 58);
                }
            }
        }

        private void UpdateGpuTuningCardUI()
        {
            if (_chkGpuTuning == null) return;

            bool enabled = _chkGpuTuning.Checked;
            if (_chkSeparateCpuGpu != null) _chkSeparateCpuGpu.Enabled = enabled;
            if (_tbGpuPowerLimit != null) _tbGpuPowerLimit.Enabled = enabled;
            if (_lblGpuPowerLimit != null)
            {
                _lblGpuPowerLimit.ForeColor = enabled ? Color.FromArgb(240, 240, 245) : Color.FromArgb(120, 120, 130);
            }
            foreach (var btn in _gpuPowerPresetButtons) btn.Enabled = enabled;

            bool fanCapEnabled = enabled && _chkGpuFanCap != null && _chkGpuFanCap.Checked;
            if (_chkGpuFanCap != null) _chkGpuFanCap.Enabled = enabled;
            if (_tbGpuFanCap != null) _tbGpuFanCap.Enabled = fanCapEnabled;
            if (_lblGpuFanCap != null)
            {
                _lblGpuFanCap.ForeColor = fanCapEnabled ? Color.FromArgb(56, 189, 248) : Color.FromArgb(120, 120, 130);
            }
            foreach (var btn in _gpuFanPresetButtons) btn.Enabled = fanCapEnabled;
        }

        private void UpdateFanModeUI()
        {
            int mode = _settings.FanVisualMode;

            // Mode 0: Graph
            _btnFanModeGraph.BackColor = (mode == 0) ? Color.FromArgb(38, 54, 75) : Color.FromArgb(28, 28, 36);
            _btnFanModeGraph.ForeColor = (mode == 0) ? Color.FromArgb(56, 189, 248) : Color.FromArgb(140, 140, 155);
            _btnFanModeGraph.FlatAppearance.BorderColor = (mode == 0) ? Color.FromArgb(56, 189, 248) : Color.FromArgb(45, 45, 58);

            // Mode 1: 1 Row
            _btnFanModeIcons.BackColor = (mode == 1) ? Color.FromArgb(38, 54, 75) : Color.FromArgb(28, 28, 36);
            _btnFanModeIcons.ForeColor = (mode == 1) ? Color.FromArgb(56, 189, 248) : Color.FromArgb(140, 140, 155);
            _btnFanModeIcons.FlatAppearance.BorderColor = (mode == 1) ? Color.FromArgb(56, 189, 248) : Color.FromArgb(45, 45, 58);

            // Mode 2: Multi-row Grid
            _btnFanModeGrid.BackColor = (mode == 2) ? Color.FromArgb(38, 54, 75) : Color.FromArgb(28, 28, 36);
            _btnFanModeGrid.ForeColor = (mode == 2) ? Color.FromArgb(56, 189, 248) : Color.FromArgb(140, 140, 155);
            _btnFanModeGrid.FlatAppearance.BorderColor = (mode == 2) ? Color.FromArgb(56, 189, 248) : Color.FromArgb(45, 45, 58);

            // Enable slider only for icon/grid modes
            bool isIconMode = (mode == 1 || mode == 2);
            _tbFanScale.Enabled = isIconMode;
            _lblFanScale.ForeColor = isIconMode ? Color.FromArgb(200, 205, 220) : Color.FromArgb(90, 95, 110);
        }

        private void ToggleFanService()
        {
            if (_hardware.Fans.IsRunning)
            {
                _settings.EnableFanAddon = false;
                _settings.Save();
                _hardware.Fans.StopService();
                UpdateFanStatusUI();
                _onSettingsUpdated?.Invoke();
            }
            else
            {
                _settings.EnableFanAddon = true;
                _settings.Save();
                _hardware.Fans.StartService();
                UpdateFanStatusUI();
                _onSettingsUpdated?.Invoke();
            }
        }

        private void UpdateFanStatusUI()
        {
            switch (_hardware.Fans.Status)
            {
                case FanPluginStatus.Connected:
                    string activeText = Loc.Get("FanPluginStatus_Connected");
                    if (!activeText.StartsWith("•") && !activeText.StartsWith("●"))
                    {
                        activeText = "• " + activeText;
                    }
                    _btnFanToggle.Text = activeText;
                    _btnFanToggle.ForeColor = Color.FromArgb(74, 222, 128);
                    _btnFanToggle.BackColor = Color.FromArgb(20, 38, 26);
                    _btnFanToggle.FlatAppearance.BorderColor = Color.FromArgb(34, 110, 58);
                    _toolTip.SetToolTip(_btnFanToggle, Loc.Get("FanPlugin_TipToggle"));
                    break;

                case FanPluginStatus.Connecting:
                case FanPluginStatus.Starting:
                    string connText = Loc.Get("FanPluginStatus_Connecting");
                    if (!connText.StartsWith("•") && !connText.StartsWith("●"))
                    {
                        connText = "• " + connText;
                    }
                    _btnFanToggle.Text = connText;
                    _btnFanToggle.ForeColor = Color.FromArgb(250, 204, 21);
                    _btnFanToggle.BackColor = Color.FromArgb(38, 34, 20);
                    _btnFanToggle.FlatAppearance.BorderColor = Color.FromArgb(100, 85, 30);
                    _toolTip.SetToolTip(_btnFanToggle, Loc.Get("FanPluginStatus_Connecting"));
                    break;

                case FanPluginStatus.NotInstalled:
                    _btnFanToggle.Text = "⚪ " + Loc.Get("FanPlugin_Start");
                    _btnFanToggle.ForeColor = Color.FromArgb(140, 145, 160);
                    _btnFanToggle.BackColor = Color.FromArgb(28, 30, 38);
                    _btnFanToggle.FlatAppearance.BorderColor = Color.FromArgb(50, 55, 70);
                    _toolTip.SetToolTip(_btnFanToggle, Loc.Get("FanPluginStatus_NotInstalled"));
                    break;

                case FanPluginStatus.Error:
                    _btnFanToggle.Text = "⚠️ " + Loc.Get("FanPlugin_Start");
                    _btnFanToggle.ForeColor = Color.FromArgb(251, 146, 60);
                    _btnFanToggle.BackColor = Color.FromArgb(36, 26, 20);
                    _btnFanToggle.FlatAppearance.BorderColor = Color.FromArgb(100, 60, 30);
                    _toolTip.SetToolTip(_btnFanToggle, Loc.Get("FanPluginStatus_NeedAdmin"));
                    break;

                default:
                    // Stopped state - steel color and Start button
                    string defStart = Loc.Get("FanPlugin_Start");
                    if (!defStart.StartsWith("▶")) defStart = "▶ " + defStart;
                    _btnFanToggle.Text = defStart;
                    _btnFanToggle.ForeColor = Color.FromArgb(160, 168, 185); // Steel color
                    _btnFanToggle.BackColor = Color.FromArgb(28, 30, 38);   // Dark steel
                    _btnFanToggle.FlatAppearance.BorderColor = Color.FromArgb(60, 68, 85); // Steel border
                    _toolTip.SetToolTip(_btnFanToggle, Loc.Get("FanPlugin_TipToggle"));
                    break;
            }

            UpdateLangButtonsHighlight();
            UpdateTrayButtonsHighlight();
        }

        private static void OpenToolsUrl()
        {
            const string url = "https://ph-cu-s.com/tools";
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                return;
            }
            catch { }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c start \"\" \"{url}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                return;
            }
            catch { }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{url}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            catch { }
        }

        private void OpenSpecsDialog()
        {
            if (_infoForm == null || _infoForm.IsDisposed)
            {
                _infoForm = new HardwareInfoForm(_hardware);
            }
            _infoForm.TopMost = this.TopMost;

            int x = this.Location.X + (this.Width - _infoForm.Width) / 2;
            int y = this.Location.Y + (this.Height - _infoForm.Height) / 2;
            var screen = Screen.FromPoint(this.Location);
            int minX = screen.WorkingArea.Left + 10;
            int maxX = Math.Max(minX, screen.WorkingArea.Right - _infoForm.Width - 10);
            x = Math.Clamp(x, minX, maxX);

            int minY = screen.WorkingArea.Top + 10;
            int maxY = Math.Max(minY, screen.WorkingArea.Bottom - _infoForm.Height - 10);
            y = Math.Clamp(y, minY, maxY);
            _infoForm.Location = new Point(x, y);

            if (!_infoForm.Visible)
            {
                _infoForm.Show(this);
            }
            _infoForm.BringToFront();
            _infoForm.Activate();
        }

        private void OpenFanSelectionDialog()
        {
            if (_fanSelectionForm == null || _fanSelectionForm.IsDisposed)
            {
                _fanSelectionForm = new FanSelectionForm(_hardware, _settings, () => {
                    UpdateFanStatusUI();
                    _onSettingsUpdated?.Invoke();
                });
            }
            _fanSelectionForm.StartPosition = FormStartPosition.Manual;
            _fanSelectionForm.TopMost = this.TopMost;

            int x = this.Location.X + (this.Width - _fanSelectionForm.Width) / 2;
            int y = this.Location.Y + (this.Height - _fanSelectionForm.Height) / 2;
            var screen = Screen.FromPoint(this.Location);
            int minX = screen.WorkingArea.Left + 10;
            int maxX = Math.Max(minX, screen.WorkingArea.Right - _fanSelectionForm.Width - 10);
            x = Math.Clamp(x, minX, maxX);

            int minY = screen.WorkingArea.Top + 10;
            int maxY = Math.Max(minY, screen.WorkingArea.Bottom - _fanSelectionForm.Height - 10);
            y = Math.Clamp(y, minY, maxY);
            _fanSelectionForm.Location = new Point(x, y);

            if (!_fanSelectionForm.Visible)
            {
                _fanSelectionForm.ShowDialog(this);
            }
            else
            {
                _fanSelectionForm.BringToFront();
                _fanSelectionForm.Activate();
            }
        }

        private void OnHeaderDrag(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(this.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }

        public void UpdateTopProcessesCheckbox()
        {
            if (_chkTopProcesses != null && _chkTopProcesses.Checked != _settings.ShowTopProcesses)
            {
                _chkTopProcesses.Checked = _settings.ShowTopProcesses;
            }
        }

        public void UpdateTopVramProcessesCheckbox()
        {
            if (_chkTopVramProcesses != null && _chkTopVramProcesses.Checked != _settings.ShowTopVramProcesses)
            {
                _chkTopVramProcesses.Checked = _settings.ShowTopVramProcesses;
            }
        }
        private void SaveServerSyncConfig()
        {
            if (_txtServerSyncHost == null || _txtServerSyncPort == null) return;
            string host = _txtServerSyncHost.Text.Trim();
            if (int.TryParse(_txtServerSyncPort.Text.Trim(), out int port) && port > 0)
            {
                _hardware.ServerSync.UpdateConfig(host, port);
            }
        }

        private async Task TestServerSyncConnectionAsync()
        {
            if (_btnServerSyncTest == null || _lblServerSyncTestStatus == null) return;

            string host = _txtServerSyncHost.Text.Trim();
            if (!int.TryParse(_txtServerSyncPort.Text.Trim(), out int port) || port <= 0)
            {
                port = 19888;
                _txtServerSyncPort.Text = "19888";
            }

            _btnServerSyncTest.Enabled = false;
            _lblServerSyncTestStatus.Text = Loc.Get("ServerSync_Testing");
            _lblServerSyncTestStatus.ForeColor = Color.FromArgb(250, 204, 21);

            var res = await _hardware.ServerSync.TestConnectionAsync(host, port);

            if (!this.IsDisposed)
            {
                _btnServerSyncTest.Enabled = true;
                if (res.success)
                {
                    _lblServerSyncTestStatus.Text = $"✓ {res.message}";
                    _lblServerSyncTestStatus.ForeColor = Color.FromArgb(52, 211, 153);
                    SaveServerSyncConfig();
                }
                else
                {
                    _lblServerSyncTestStatus.Text = $"✕ {res.message}";
                    _lblServerSyncTestStatus.ForeColor = Color.FromArgb(248, 113, 113);
                }
            }
        }

        private class KeyOptionItem
        {
            public string Name { get; set; } = "";
            public Keys Key { get; set; }
            public override string ToString() => Name;
        }

        private void PopulateLayoutKeyComboBox()
        {
            if (_cbLayoutKey == null) return;
            _cbLayoutKey.Items.Clear();

            var keyList = new List<KeyOptionItem>
            {
                new() { Name = "Pause", Key = Keys.Pause },
                new() { Name = "ScrollLock", Key = Keys.Scroll },
                new() { Name = "Insert", Key = Keys.Insert },
                new() { Name = "~ (Tilde)", Key = Keys.Oemtilde },
                new() { Name = "F1", Key = Keys.F1 },
                new() { Name = "F2", Key = Keys.F2 },
                new() { Name = "F3", Key = Keys.F3 },
                new() { Name = "F4", Key = Keys.F4 },
                new() { Name = "F5", Key = Keys.F5 },
                new() { Name = "F6", Key = Keys.F6 },
                new() { Name = "F7", Key = Keys.F7 },
                new() { Name = "F8", Key = Keys.F8 },
                new() { Name = "F9", Key = Keys.F9 },
                new() { Name = "F10", Key = Keys.F10 },
                new() { Name = "F11", Key = Keys.F11 },
                new() { Name = "F12", Key = Keys.F12 },
                new() { Name = "Q", Key = Keys.Q },
                new() { Name = "R", Key = Keys.R },
                new() { Name = "T", Key = Keys.T },
                new() { Name = "Y", Key = Keys.Y },
                new() { Name = "Space", Key = Keys.Space }
            };

            foreach (var item in keyList)
            {
                _cbLayoutKey.Items.Add(item);
            }

            SelectKeyInComboBox((Keys)_settings.LayoutSwitchVirtualKey);
        }

        private void SelectKeyInComboBox(Keys key)
        {
            if (_cbLayoutKey == null) return;
            for (int i = 0; i < _cbLayoutKey.Items.Count; i++)
            {
                if (_cbLayoutKey.Items[i] is KeyOptionItem item && item.Key == key)
                {
                    _cbLayoutKey.SelectedIndex = i;
                    return;
                }
            }
            if (_cbLayoutKey.Items.Count > 0)
                _cbLayoutKey.SelectedIndex = 0;
        }

        private void UpdateDesktopIconsUI()
        {
            if (_lblDesktopIconsStatus != null)
            {
                _lblDesktopIconsStatus.Text = DesktopIconService.GetStatusInfo();
            }
            if (_btnDesktopIconsRestore != null)
            {
                _btnDesktopIconsRestore.Enabled = DesktopIconService.HasSavedSnapshot();
            }
            if (_chkDesktopIconsAutoRestore != null)
            {
                _chkDesktopIconsAutoRestore.Checked = _settings.AutoRestoreDesktopIcons;
            }
        }

        private void RegisterCardSection(string key, Panel card, Label header, Func<string> titleGetter, int fullHeight)
        {
            bool isCollapsed = true;
            if (_settings.SettingsCardsCollapsed != null && _settings.SettingsCardsCollapsed.TryGetValue(key, out bool savedCollapsed))
            {
                isCollapsed = savedCollapsed;
            }

            var sec = new CardSection
            {
                Key = key,
                Card = card,
                Header = header,
                TitleGetter = titleGetter,
                FullHeight = fullHeight,
                IsCollapsed = isCollapsed
            };
            _cardSections.Add(sec);

            header.Cursor = Cursors.Hand;
            header.Click += (s, e) => ToggleCardSection(sec);
            card.Click += (s, e) => {
                if (e is MouseEventArgs me && me.Y <= 32)
                {
                    ToggleCardSection(sec);
                }
            };
        }

        private void ToggleCardSection(CardSection sec)
        {
            sec.IsCollapsed = !sec.IsCollapsed;
            if (_settings.SettingsCardsCollapsed == null)
            {
                _settings.SettingsCardsCollapsed = new();
            }
            _settings.SettingsCardsCollapsed[sec.Key] = sec.IsCollapsed;
            _settings.Save();
            ApplySectionState(sec);
            ReflowAllCardSections();
        }

        private void ApplySectionState(CardSection sec)
        {
            string title = sec.TitleGetter();
            sec.Header.Text = (sec.IsCollapsed ? "▶ " : "▼ ") + title;
            sec.Header.UseMnemonic = false;
            sec.Card.Height = sec.IsCollapsed ? 32 : sec.FullHeight;
            foreach (Control c in sec.Card.Controls)
            {
                if (c != sec.Header)
                {
                    c.Visible = !sec.IsCollapsed;
                }
            }
        }

        private void SetAllSectionsCollapsed(bool collapsed)
        {
            if (_settings.SettingsCardsCollapsed == null)
            {
                _settings.SettingsCardsCollapsed = new();
            }
            foreach (var sec in _cardSections)
            {
                sec.IsCollapsed = collapsed;
                _settings.SettingsCardsCollapsed[sec.Key] = collapsed;
                ApplySectionState(sec);
            }
            _settings.Save();
            ReflowAllCardSections();
        }

        private void ApplyInitialCollapseState()
        {
            foreach (var sec in _cardSections)
            {
                if (_settings.SettingsCardsCollapsed != null && _settings.SettingsCardsCollapsed.TryGetValue(sec.Key, out bool savedCollapsed))
                {
                    sec.IsCollapsed = savedCollapsed;
                }
                ApplySectionState(sec);
            }
            ReflowAllCardSections();
        }

        private void ReflowAllCardSections()
        {
            if (_pnlBody == null) return;
            int curY = 12;
            foreach (var sec in _cardSections)
            {
                sec.Card.Top = curY;
                curY += sec.Card.Height + 8;
            }
            int cardW = (_allCards.Count > 0) ? _allCards[0].Width : (_pnlBody.ClientSize.Width - 28);
            if (_pnlBottomSpacer != null)
            {
                _pnlBottomSpacer.Top = curY;
                _pnlBottomSpacer.Left = 14;
                _pnlBottomSpacer.Width = cardW;
            }
            else if (_pnlBody.Controls.Count > 0)
            {
                var lastCtrl = _pnlBody.Controls[_pnlBody.Controls.Count - 1];
                if (lastCtrl != null && !_cardSections.Any(s => s.Card == lastCtrl))
                {
                    lastCtrl.Top = curY;
                    lastCtrl.Left = 14;
                    lastCtrl.Width = cardW;
                }
            }
            _pnlBody.HorizontalScroll.Enabled = false;
            _pnlBody.HorizontalScroll.Visible = false;
            _pnlBody.HorizontalScroll.Maximum = 0;
        }

        private void UpdateBackendButtonsHighlight()
        {
            if (_btnPresetLms == null || _btnPresetOllama == null) return;
            string url = _tbTranslateUrl != null ? _tbTranslateUrl.Text : (_settings.TranslateApiUrl ?? "");
            bool isLms = url.Contains(":1234");
            bool isOllama = url.Contains(":11434");

            // LM Studio button
            _btnPresetLms.BackColor = isLms ? Color.FromArgb(14, 165, 233) : Color.FromArgb(28, 30, 40);
            _btnPresetLms.ForeColor = isLms ? Color.White : Color.FromArgb(150, 155, 175);
            _btnPresetLms.Font = new Font("Segoe UI", 8f, isLms ? FontStyle.Bold : FontStyle.Regular);
            _btnPresetLms.FlatAppearance.BorderColor = isLms ? Color.FromArgb(56, 189, 248) : Color.FromArgb(55, 60, 75);

            // Ollama button
            _btnPresetOllama.BackColor = isOllama ? Color.FromArgb(14, 165, 233) : Color.FromArgb(28, 30, 40);
            _btnPresetOllama.ForeColor = isOllama ? Color.White : Color.FromArgb(150, 155, 175);
            _btnPresetOllama.Font = new Font("Segoe UI", 8f, isOllama ? FontStyle.Bold : FontStyle.Regular);
            _btnPresetOllama.FlatAppearance.BorderColor = isOllama ? Color.FromArgb(56, 189, 248) : Color.FromArgb(55, 60, 75);
        }

        private Panel CreateHotkeyRow(
            string title,
            bool initialEnabled, Action<bool> onEnabled,
            bool initialCtrl, Action<bool> onCtrl,
            bool initialAlt, Action<bool> onAlt,
            bool initialShift, Action<bool> onShift,
            bool initialWin, Action<bool> onWin,
            int initialKey, Action<int> onKey,
            int y, int width)
        {
            var pnl = new Panel
            {
                Location = new Point(0, y),
                Size = new Size(width, 52),
                BackColor = Color.Transparent
            };

            // Line 1: Full action title in bold with checkbox
            var chkEnable = new CheckBox
            {
                Text = title,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = Color.FromArgb(220, 220, 235),
                Location = new Point(0, 0),
                Size = new Size(width, 22),
                Checked = initialEnabled,
                AutoEllipsis = true,
                Cursor = Cursors.Hand,
                UseMnemonic = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            // Line 2: Indented modifiers and key dropdown
            var chkCtrl = new CheckBox
            {
                Text = "Ctrl",
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = Color.FromArgb(200, 200, 215),
                Location = new Point(14, 24),
                Size = new Size(54, 22),
                Checked = initialCtrl,
                Enabled = initialEnabled,
                Cursor = Cursors.Hand
            };

            var chkAlt = new CheckBox
            {
                Text = "Alt",
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = Color.FromArgb(200, 200, 215),
                Location = new Point(70, 24),
                Size = new Size(50, 22),
                Checked = initialAlt,
                Enabled = initialEnabled,
                Cursor = Cursors.Hand
            };

            var chkShift = new CheckBox
            {
                Text = "Shift",
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = Color.FromArgb(200, 200, 215),
                Location = new Point(122, 24),
                Size = new Size(58, 22),
                Checked = initialShift,
                Enabled = initialEnabled,
                Cursor = Cursors.Hand
            };

            var chkWin = new CheckBox
            {
                Text = "Win",
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = Color.FromArgb(200, 200, 215),
                Location = new Point(182, 24),
                Size = new Size(54, 22),
                Checked = initialWin,
                Enabled = initialEnabled,
                Cursor = Cursors.Hand
            };

            int cbKeyWidth = 85;
            int lblKeyWidth = 65;

            var cbKey = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(24, 24, 32),
                ForeColor = Color.FromArgb(45, 212, 191),
                Font = new Font("Segoe UI", 8.25f),
                Location = new Point(width - cbKeyWidth, 23),
                Size = new Size(cbKeyWidth, 24),
                Enabled = initialEnabled,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            var lblKey = new Label
            {
                Text = Loc.Get("LayoutSwitch_Key"),
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = Color.FromArgb(170, 170, 185),
                Location = new Point(width - cbKeyWidth - lblKeyWidth - 4, 27),
                Size = new Size(lblKeyWidth, 18),
                TextAlign = ContentAlignment.MiddleRight,
                UseMnemonic = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            var keyList = GetKeyOptionList();
            foreach (var k in keyList)
            {
                cbKey.Items.Add(k);
            }
            for (int i = 0; i < cbKey.Items.Count; i++)
            {
                if (cbKey.Items[i] is KeyOptionItem item && (int)item.Key == initialKey)
                {
                    cbKey.SelectedIndex = i;
                    break;
                }
            }
            if (cbKey.SelectedIndex < 0 && cbKey.Items.Count > 0)
                cbKey.SelectedIndex = 0;

            chkEnable.CheckedChanged += (s, e) => {
                bool en = chkEnable.Checked;
                chkCtrl.Enabled = en;
                chkAlt.Enabled = en;
                chkShift.Enabled = en;
                chkWin.Enabled = en;
                lblKey.Enabled = en;
                cbKey.Enabled = en;
                onEnabled(en);
                _onSettingsUpdated?.Invoke();
            };

            chkCtrl.CheckedChanged += (s, e) => { onCtrl(chkCtrl.Checked); _onSettingsUpdated?.Invoke(); };
            chkAlt.CheckedChanged += (s, e) => { onAlt(chkAlt.Checked); _onSettingsUpdated?.Invoke(); };
            chkShift.CheckedChanged += (s, e) => { onShift(chkShift.Checked); _onSettingsUpdated?.Invoke(); };
            chkWin.CheckedChanged += (s, e) => { onWin(chkWin.Checked); _onSettingsUpdated?.Invoke(); };

            cbKey.SelectedIndexChanged += (s, e) => {
                if (cbKey.SelectedItem is KeyOptionItem ki)
                {
                    onKey((int)ki.Key);
                    _onSettingsUpdated?.Invoke();
                }
            };

            pnl.Controls.AddRange(new Control[] { chkEnable, chkCtrl, chkAlt, chkShift, chkWin, lblKey, cbKey });
            return pnl;
        }

        private static List<KeyOptionItem> GetKeyOptionList()
        {
            var list = new List<KeyOptionItem>();
            var keys = new[]
            {
                Keys.T, Keys.P, Keys.O, Keys.Q, Keys.R, Keys.Y, Keys.U, Keys.I, Keys.A, Keys.S, Keys.D, Keys.F,
                Keys.G, Keys.H, Keys.J, Keys.K, Keys.L, Keys.Z, Keys.X, Keys.C, Keys.V, Keys.B, Keys.N, Keys.M,
                Keys.F1, Keys.F2, Keys.F3, Keys.F4, Keys.F5, Keys.F6, Keys.F7, Keys.F8, Keys.F9, Keys.F10, Keys.F11, Keys.F12,
                Keys.Pause, Keys.Scroll, Keys.Insert, Keys.Space, Keys.Oemtilde
            };
            foreach (var k in keys)
            {
                string name = k switch
                {
                    Keys.Oemtilde => "~ (Tilde)",
                    Keys.Scroll => "ScrollLock",
                    _ => k.ToString()
                };
                list.Add(new KeyOptionItem { Name = name, Key = k });
            }
            return list;
        }

        private void RunCheckConnection()
        {
            if (_btnTranslateCheck == null || _lblTranslateCheckStatus == null) return;
            _btnTranslateCheck.Enabled = false;
            _lblTranslateCheckStatus.Text = Loc.Get("Translate_Checking");
            _lblTranslateCheckStatus.ForeColor = Color.FromArgb(56, 189, 248);

            Task.Run(async () =>
            {
                var (isOnline, models, activeModel, statusMessage) = await _hardware.Translation.CheckConnectionAndGetModelsAsync();
                this.BeginInvoke((Action)(() =>
                {
                    _btnTranslateCheck.Enabled = true;
                    _lblTranslateCheckStatus.Text = statusMessage;

                    if (isOnline)
                    {
                        _lblTranslateCheckStatus.ForeColor = Color.FromArgb(45, 212, 191);

                        _cbTranslateModel.Items.Clear();
                        foreach (var m in models)
                        {
                            _cbTranslateModel.Items.Add(m);
                        }
                        if (!string.IsNullOrEmpty(activeModel) && models.Contains(activeModel))
                        {
                            _cbTranslateModel.SelectedItem = activeModel;
                        }
                        else if (_cbTranslateModel.Items.Count > 0)
                        {
                            _cbTranslateModel.SelectedIndex = 0;
                        }
                    }
                    else
                    {
                        _lblTranslateCheckStatus.ForeColor = Color.FromArgb(248, 113, 113);
                    }
                }));
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Loc.LanguageChanged -= RefreshLocalizedTexts;
                _toolTip.Dispose();
                _infoForm?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
