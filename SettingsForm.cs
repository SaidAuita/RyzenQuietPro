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
            AutoPopDelay = 5000
        };

        // UI Controls
        private Label _lblHeaderTitle = null!;
        private Label _lblSecLang = null!;
        private Label _lblSecGraphs = null!;
        private Label _lblSecTray = null!;
        private Label _lblSecSystem = null!;
        private Label _lblSecGpuTuning = null!;

        private readonly List<Button> _langButtons = new();
        private readonly List<Button> _trayButtons = new();
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

        private readonly List<Panel> _allCards = new();
        private readonly List<Panel> _cardDividers = new();
        private readonly List<Button> _opacityPresetButtons = new();
        private Panel _pnlFailSafe = null!;
        private Panel _pnlBody = null!;
        private Label _lblResizeGrip = null!;

        private CheckBox _chkCpuGraph = null!;
        private CheckBox _chkCpuCores = null!;
        private CheckBox _chkTopProcesses = null!;
        private CheckBox _chkRamGraph = null!;
        private CheckBox _chkGpuGraph = null!;
        private CheckBox _chkGpuTemp = null!;
        private CheckBox _chkGpuFan = null!;
        private CheckBox _chkVramGraph = null!;
        private CheckBox _chkDiskGraph = null!;
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

        private CheckBox _chkAlwaysOnTop = null!;
        private CheckBox _chkAutoQuiet = null!;
        private CheckBox _chkStartup = null!;

        private Button _btnSpecs = null!;
        private Button _btnTools = null!;
        private Button _btnClose = null!;

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
            int totalFormH = multiGpu ? 1610 : 1580;

            var screen = Screen.FromPoint(Cursor.Position);
            int availH = screen.WorkingArea.Height - 30;
            int formH = Math.Min(totalFormH, availH);
            int formW = Math.Clamp(_settings.SettingsWidth >= 480 ? _settings.SettingsWidth : 500, 480, 850);

            this.AutoScaleMode = AutoScaleMode.None;
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.ShowInTaskbar = false;
            this.ClientSize = new Size(formW, formH);
            this.MinimumSize = new Size(480, 460);
            this.BackColor = Color.FromArgb(24, 24, 32);
            this.ForeColor = Color.White;
            this.DoubleBuffered = true;
            this.Icon = AppIcons.QuietIcon;

            InitializeComponents();
            Loc.LanguageChanged += RefreshLocalizedTexts;

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
            int cardW = Math.Max(380, w - (cardMargin * 2) - scrollbarAllowance);
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

            _btnSpecs = new Button
            {
                Text = "ℹ",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 215),
                BackColor = Color.FromArgb(32, 32, 40),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(95, 32),
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
                Size = new Size(95, 32),
                Location = new Point(cardX + 101, 6),
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = Padding.Empty,
                Cursor = Cursors.Hand
            };
            _btnTools.FlatAppearance.BorderColor = Color.FromArgb(50, 50, 65);
            _toolTip.SetToolTip(_btnTools, "https://ph-cu-s.com/tools");
            _btnTools.Click += (s, e) => OpenToolsUrl();
            pnlFooter.Controls.Add(_btnTools);

            _btnClose = new Button
            {
                Text = Loc.Get("InfoClose"),
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(14, 165, 233), // Sky Blue Accent
                FlatStyle = FlatStyle.Flat,
                Size = new Size(Math.Max(120, cardW - 202), 32),
                Location = new Point(cardX + 202, 6),
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = Padding.Empty,
                Cursor = Cursors.Hand
            };
            _btnClose.FlatAppearance.BorderSize = 0;
            _btnClose.Click += (s, e) => this.Close();
            pnlFooter.Controls.Add(_btnClose);

            _lblResizeGrip = new Label
            {
                Text = "◢",
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = Color.FromArgb(80, 80, 95),
                Size = new Size(16, 16),
                TextAlign = ContentAlignment.BottomRight,
                Cursor = Cursors.SizeNWSE,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Location = new Point(w - 18, 44 - 18)
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
            int cardGraphsH = multiGpu ? 526 : 496;
            var cardGraphs = CreateCard(cardX, curY, cardW, cardGraphsH);
            _pnlBody.Controls.Add(cardGraphs);

            _lblSecGraphs = CreateSectionHeader(Loc.Get("MenuGraphs"), 12, 8);
            cardGraphs.Controls.Add(_lblSecGraphs);

            int chkX = 14;
            int chkY = 32;
            int chkGap = 26;

            _chkStopwatch = CreateCheckbox(Loc.Get("StopwatchModule"), chkX, chkY, _settings.ShowStopwatch, v => {
                _settings.ShowStopwatch = v;
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            });
            cardGraphs.Controls.Add(_chkStopwatch);

            _chkCpuGraph = CreateCheckbox(Loc.Get("CpuGraph"), chkX, chkY + (chkGap * 1), _settings.ShowCpuGraph, v => _settings.ShowCpuGraph = v);
            _chkCpuCores = CreateCheckbox(Loc.Get("CpuCores"), chkX, chkY + (chkGap * 2), _settings.ShowCpuCores, v => _settings.ShowCpuCores = v);
            _chkTopProcesses = CreateCheckbox(Loc.Get("TopProcesses"), chkX, chkY + (chkGap * 3), _settings.ShowTopProcesses, v => {
                _settings.ShowTopProcesses = v;
                _hardware.Processes.IsEnabled = v;
            });
            _chkRamGraph = CreateCheckbox(Loc.Get("RamGraph"), chkX, chkY + (chkGap * 4), _settings.ShowRamGraph, v => _settings.ShowRamGraph = v);
            _chkGpuGraph = CreateCheckbox(Loc.Get("GpuGraph"), chkX, chkY + (chkGap * 5), _settings.ShowGpuGraph, v => _settings.ShowGpuGraph = v);
            _chkGpuTemp = CreateCheckbox(Loc.Get("GpuTemp"), chkX, chkY + (chkGap * 6), _settings.ShowGpuTempLine, v => _settings.ShowGpuTempLine = v);
            _chkGpuFan = CreateCheckbox(Loc.Get("GpuFan"), chkX, chkY + (chkGap * 7), _settings.ShowGpuFanSpeed, v => _settings.ShowGpuFanSpeed = v);
            _chkVramGraph = CreateCheckbox(Loc.Get("VramGraph"), chkX, chkY + (chkGap * 8), _settings.ShowVramGraph, v => _settings.ShowVramGraph = v);
            _chkDiskGraph = CreateCheckbox(Loc.Get("DiskGraph"), chkX, chkY + (chkGap * 9), _settings.ShowDiskGraph, v => _settings.ShowDiskGraph = v);

            // Tier 1: Checkbox on left + Active/Start status button on right + Folder button
            int fanY = chkY + (chkGap * 10);
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
                _chkVramGraph, _chkDiskGraph, _chkFanGraph,
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
            int cardSwH = 282;
            var cardSw = CreateCard(cardX, curY, cardW, cardSwH);
            _pnlBody.Controls.Add(cardSw);

            _lblSecStopwatch = CreateSectionHeader("⏱ " + Loc.Get("StopwatchSettings"), 12, 8);
            cardSw.Controls.Add(_lblSecStopwatch);

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

            curY += cardSwH + 8;

            // ================= 2c. GPU ACOUSTIC & POWER TUNING CARD =================
            int cardGpuH = 312;
            var cardGpu = CreateCard(cardX, curY, cardW, cardGpuH);
            _pnlBody.Controls.Add(cardGpu);

            _lblSecGpuTuning = CreateSectionHeader("⚡ " + Loc.Get("GpuTuningTitle"), 12, 8);
            cardGpu.Controls.Add(_lblSecGpuTuning);

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

            // ================= 3. TRAY ICON CARD =================
            var cardTray = CreateCard(cardX, curY, cardW, 70);
            _pnlBody.Controls.Add(cardTray);

            _lblSecTray = CreateSectionHeader(Loc.Get("TrayIconMenu"), 12, 8);
            cardTray.Controls.Add(_lblSecTray);

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
                _toolTip.SetToolTip(btn, Loc.Get(opt.tipKey));
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

            // ================= 4. SYSTEM & OPACITY CARD =================
            // Clear separation between slider presets and checkboxes
            var cardSys = CreateCard(cardX, curY, cardW, 192);
            _pnlBody.Controls.Add(cardSys);

            _lblSecSystem = CreateSectionHeader(Loc.Get("Opacity"), 12, 8);
            cardSys.Controls.Add(_lblSecSystem);

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
                StartupManager.SetStartup(v);
            });

            cardSys.Controls.AddRange(new Control[] { _chkAlwaysOnTop, _chkAutoQuiet, _chkStartup });

            curY += 200;

            // Bottom spacer for scrollable body
            _pnlBody.Controls.Add(new Panel
            {
                Location = new Point(cardX, curY),
                Size = new Size(cardW, 14),
                BackColor = Color.Transparent
            });
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
                AutoSize = true
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

        private void RelayoutCards()
        {
            if (_pnlBody == null || _allCards.Count == 0) return;

            int w = this.ClientSize.Width;
            int cardMargin = 14;
            int scrollbarAllowance = 22;
            int cardW = Math.Max(380, w - (cardMargin * 2) - scrollbarAllowance);

            // Resize each card panel
            foreach (var card in _allCards)
            {
                card.Width = cardW;
                card.Invalidate();
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

            // Footer close button
            if (_btnClose != null)
            {
                _btnClose.Width = Math.Max(120, cardW - 202);
            }

            _pnlBody.HorizontalScroll.Maximum = 0;
            _pnlBody.HorizontalScroll.Visible = false;
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
            _chkDiskGraph.Text = Loc.Get("DiskGraph");
            _chkFanGraph.Text = Loc.Get("FanGraph");

            if (_lblSecStopwatch != null) _lblSecStopwatch.Text = "⏱ " + Loc.Get("StopwatchSettings");
            if (_chkStopwatchAuto != null) _chkStopwatchAuto.Text = Loc.Get("StopwatchAutoStartEnable");
            if (_lblSwSourceTitle != null) _lblSwSourceTitle.Text = Loc.Get("StopwatchTriggerSource");
            if (_lblSwStartWatts != null) _lblSwStartWatts.Text = $"{Loc.Get("StopwatchAutoStartWatts")}: {_settings.StopwatchAutoStartWatts}W";
            if (_lblSwStopWatts != null) _lblSwStopWatts.Text = $"{Loc.Get("StopwatchAutoStopWatts")}: {_settings.StopwatchAutoStopWatts}W";
            if (_lblSwHysteresis != null) _lblSwHysteresis.Text = $"{Loc.Get("StopwatchHysteresis")}: {_settings.StopwatchHysteresisSeconds} {Loc.Get("Sec")}";
            if (_toolTip != null && _tbSwHysteresis != null) _toolTip.SetToolTip(_tbSwHysteresis, Loc.Get("StopwatchHysteresisTip"));
            if (_toolTip != null && _lblSwHysteresis != null) _toolTip.SetToolTip(_lblSwHysteresis, Loc.Get("StopwatchHysteresisTip"));

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
            _lblFanScale.Text = $"{Loc.Get("FanScale")}: {_settings.FanScalePercent}%";
            if (_toolTip != null && _tbFanScale != null) _toolTip.SetToolTip(_tbFanScale, Loc.Get("FanScaleTip"));
            if (_toolTip != null && _lblFanScale != null) _toolTip.SetToolTip(_lblFanScale, Loc.Get("FanScaleTip"));
            if (_btnFanSelect != null) _btnFanSelect.Text = Loc.Get("FanSelectFans");
            if (_toolTip != null && _btnFanSelect != null) _toolTip.SetToolTip(_btnFanSelect, Loc.Get("FanSelectionTitle"));
            if (_toolTip != null && _btnFanFolder != null) _toolTip.SetToolTip(_btnFanFolder, Loc.Get("FanPluginFolder"));
            if (_chkShowAllGpus != null) _chkShowAllGpus.Text = Loc.Get("ShowAllGpus");

            _btnResetGraphs.Text = "⚡ " + Loc.Get("ShowAllGraphs");

            foreach (var btn in _trayButtons)
            {
                var opt = ((string labelKey, string tipKey, int metricVal, bool isLive))btn.Tag!;
                btn.Text = opt.isLive ? $"{Loc.Get(opt.labelKey)} %" : Loc.Get(opt.labelKey);
                if (_toolTip != null) _toolTip.SetToolTip(btn, Loc.Get(opt.tipKey));
            }

            _chkAlwaysOnTop.Text = Loc.Get("AlwaysOnTop");
            _chkAutoQuiet.Text = Loc.Get("AutoQuiet");
            _chkStartup.Text = Loc.Get("Startup");

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

            if (_btnSpecs != null)
            {
                _btnSpecs.Text = "ℹ";
                _toolTip?.SetToolTip(_btnSpecs, Loc.Get("TipInfo"));
            }
            if (_btnTools != null) _toolTip?.SetToolTip(_btnTools, "https://ph-cu-s.com/tools");
            if (_btnClose != null) _btnClose.Text = Loc.Get("InfoClose");

            UpdateFanModeUI();
            UpdateFanStatusUI();
            UpdateGpuTuningCardUI();
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
            x = Math.Clamp(x, screen.WorkingArea.Left + 10, screen.WorkingArea.Right - _infoForm.Width - 10);
            y = Math.Clamp(y, screen.WorkingArea.Top + 10, screen.WorkingArea.Bottom - _infoForm.Height - 10);
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
            x = Math.Clamp(x, screen.WorkingArea.Left + 10, screen.WorkingArea.Right - _fanSelectionForm.Width - 10);
            y = Math.Clamp(y, screen.WorkingArea.Top + 10, screen.WorkingArea.Bottom - _fanSelectionForm.Height - 10);
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
