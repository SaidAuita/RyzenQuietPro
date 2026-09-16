using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RyzenQuietPro
{
    public class DashboardPro : Form
    {
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

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
        private readonly Action<PowerPlanManager.Mode> _onModeChangeRequested;
        private readonly Action _onSettingsChanged;

        private bool _isQuietMode;
        private bool _isDetached;
        private bool _alwaysOnTop;
        private bool _isInitialized = false;

        // UI Header
        private Panel _headerPanel = null!;
        private PictureBox _picAppIcon = null!;
        private Label _lblTitle = null!;
        private Label _btnInfo = null!;
        private Label _btnPin = null!;
        private Label _btnDetach = null!;
        private Label _btnClose = null!;

        // Module Separators
        private ModuleSeparator _sepCpu = null!;
        private ModuleSeparator _sepRam = null!;
        private ModuleSeparator _sepGpu = null!;
        private ModuleSeparator _sepVram = null!;
        private ModuleSeparator _sepDisk = null!;
        private ModuleSeparator _sepFans = null!;

        // Metrics Headers & Graphs
        private Label _lblCpu = null!;
        private Label _lblCpuSub = null!;
        private SmoothPanel _pnlCpuGraph = null!;
        private SmoothPanel _pnlCoresMatrix = null!;
        private SmoothPanel _pnlTopProcesses = null!;

        private Label _lblRam = null!;
        private Label _lblRamSub = null!;
        private SmoothPanel _pnlRamGraph = null!;

        private Label _lblGpu = null!;
        private GpuSubHeaderPanel _pnlGpuSub = null!;
        private SmoothPanel _pnlGpuGraph = null!;
        private CheckBox _chkShowAllGpus = null!;

        private static readonly Color GpuTempColor = Color.FromArgb(160, 115, 225); // 50% purple tint for GPU temperature

        private static readonly Color[] MultiGpuColors = new Color[]
        {
            Color.FromArgb(168, 85, 247), // GPU 0: Purple
            Color.FromArgb(56, 189, 248),  // GPU 1: Sky Blue
            Color.FromArgb(234, 179, 8),   // GPU 2: Amber
            Color.FromArgb(34, 197, 94)    // GPU 3: Emerald
        };

        private static readonly Color[] MultiVramColors = new Color[]
        {
            Color.FromArgb(236, 72, 153), // VRAM 0: Pink
            Color.FromArgb(14, 165, 233),  // VRAM 1: Light Blue
            Color.FromArgb(249, 115, 22),  // VRAM 2: Orange
            Color.FromArgb(16, 185, 129)   // VRAM 3: Teal
        };

        private Label _lblVram = null!;
        private Label _lblVramSub = null!;
        private SmoothPanel _pnlVramGraph = null!;

        private Label _lblDisk = null!;
        private Label _lblDiskSub = null!;
        private SmoothPanel _pnlDiskGraph = null!;

        private Label _lblFans = null!;
        private Label _lblFansSub = null!;
        private SmoothPanel _pnlFansGraph = null!;

        // Fan Visual Mode & Animation
        private readonly Dictionary<string, float> _fanAngles = new();
        private System.Windows.Forms.Timer? _fanAnimTimer;
        private int _fanScrollX = 0;
        private int _maxFanScroll = 0;
        private bool _isFanDragging = false;
        private int _fanDragStartX = 0;
        private int _fanScrollStartX = 0;

        // Bottom Footer Panel & Mode Controls
        private Panel _footerPanel = null!;
        private Button _btnSilent = null!;
        private Button _btnBoost = null!;
        private Button _btnGear = null!;
        private bool _gearHover;
        private Button _btnGpuSilent = null!;
        private Button _btnGpuBoost = null!;
        private Label _lblResizeGrip = null!;
        private ToolTip _toolTip = null!;
        private HardwareInfoForm? _infoForm;
        private SettingsForm? _settingsForm;
        private bool _isStartupEnabled;

        private System.Windows.Forms.Timer _uiRefreshTimer = null!;

        public DashboardPro(
            HardwareMonitor hardware,
            AppSettings settings,
            bool initialQuietMode,
            bool isStartupEnabled,
            Action<PowerPlanManager.Mode> onModeChangeRequested,
            Action onSettingsChanged)
        {
            _hardware = hardware;
            _settings = settings;
            _isQuietMode = initialQuietMode;
            _isDetached = settings.IsDetached;
            _alwaysOnTop = settings.AlwaysOnTop;
            _isStartupEnabled = isStartupEnabled;
            _onModeChangeRequested = onModeChangeRequested;
            _onSettingsChanged = onSettingsChanged;

            _hardware.Processes.IsEnabled = _settings.ShowTopProcesses;
            _hardware.Fans.DemoMode = _settings.EnableFanDemo;
            _hardware.Fans.SetEnabled((_settings.ShowFanGraph && _settings.EnableFanAddon) || _settings.GpuTuningEnabled);
            _hardware.Fans.FansUpdated += () => {
                if (this.IsHandleCreated && !this.IsDisposed)
                {
                    try { 
                        this.BeginInvoke((Action)(() => {
                            EnsureFanLayout();
                            UpdateMetricsUI();
                        })); 
                    } catch { }
                }
            };

            _hardware.GpuTuning.StateChanged += () => {
                if (this.IsHandleCreated && !this.IsDisposed)
                {
                    try { this.BeginInvoke((Action)UpdateModeUI); } catch { }
                }
            };

            Loc.Initialize(_settings.Language);
            Loc.LanguageChanged += OnLanguageChanged;

            InitializeComponents();

            this.Deactivate += OnWindowDeactivated;
            this.LocationChanged += OnWindowLocationChanged;

            _uiRefreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _uiRefreshTimer.Tick += (s, e) => {
                if (this.Visible)
                {
                    UpdateMetricsUI();
                }
            };
            _uiRefreshTimer.Start();

            _fanAnimTimer = new System.Windows.Forms.Timer { Interval = 33 };
            _fanAnimTimer.Tick += (s, e) => {
                if (this.Visible && _pnlFansGraph.Visible && (_settings.FanVisualMode == 1 || _settings.FanVisualMode == 2))
                {
                    UpdateFanAnimation();
                }
            };
            _fanAnimTimer.Start();
        }

        private int _lastLayoutFanCount = -1;
        private int _lastLayoutFanRows = -1;
        private int _lastLayoutFanScale = -1;
        private int _lastLayoutVisualMode = -1;

        private void EnsureFanLayout()
        {
            if (!_settings.ShowFanGraph) return;

            var allFans = _hardware.Fans.Fans;
            if (allFans.Count > 0 && _settings.LastKnownFanCount != allFans.Count)
            {
                _settings.LastKnownFanCount = allFans.Count;
                _settings.Save();
            }

            int visibleFanCount = allFans.Count(f => _settings.IsFanVisible(f.Id, f.Name));
            int contentW = Math.Max(200, this.ClientSize.Width - 28);
            float fanScale = _settings.FanScale;
            int targetCardW = (int)(92 * fanScale);
            int cols = Math.Max(2, (contentW - 4) / targetCardW);
            if (fanScale >= 1.6f && cols > 2) cols = 2;
            else if (fanScale >= 1.25f && cols > 3) cols = 3;
            else if (contentW < 360 && cols > 3) cols = 3;

            int rows = (int)Math.Ceiling((double)Math.Max(1, visibleFanCount) / cols);

            if (_lastLayoutFanCount != visibleFanCount || _lastLayoutFanRows != rows || 
                _lastLayoutFanScale != _settings.FanScalePercent || _lastLayoutVisualMode != _settings.FanVisualMode)
            {
                _lastLayoutFanCount = visibleFanCount;
                _lastLayoutFanRows = rows;
                _lastLayoutFanScale = _settings.FanScalePercent;
                _lastLayoutVisualMode = _settings.FanVisualMode;
                LayoutComponents();
                this.Invalidate();
            }
        }

        private void InitializeComponents()
        {
            this.AutoScaleMode = AutoScaleMode.None;
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.TopMost = _alwaysOnTop;
            this.BackColor = Color.FromArgb(22, 22, 26);
            this.ForeColor = Color.White;
            this.DoubleBuffered = true;
            this.Opacity = _settings.WidgetOpacity;
            this.Icon = _isQuietMode ? AppIcons.QuietIcon : AppIcons.NormalIcon;

            int initW = Math.Max(380, _settings.WindowWidth > 0 ? _settings.WindowWidth : 420);
            int defaultH = _settings.ShowTopProcesses ? 590 : 495;
            if (_settings.ShowFanGraph)
            {
                float fanScale = _settings.FanScale;
                if (_settings.FanVisualMode == 1)
                {
                    defaultH += (int)Math.Round(72 * fanScale) + 16;
                }
                else if (_settings.FanVisualMode == 2)
                {
                    int cols;
                    if (fanScale >= 1.40f)
                    {
                        cols = Math.Max(2, (initW - 28 - 4) / (int)(95 * fanScale));
                        if ((initW - 28) < 460) cols = 2;
                    }
                    else
                    {
                        cols = Math.Max(2, (initW - 28 - 4) / 115);
                        if ((initW - 28) < 460) cols = 3;
                        if ((initW - 28) < 320) cols = 2;
                    }

                    int fanCount = _settings.LastKnownFanCount > 0 ? _settings.LastKnownFanCount : 4;
                    int expectedRows = (int)Math.Ceiling((double)fanCount / cols);
                    if (expectedRows < 1) expectedRows = 1;
                    int testCardW = (initW - 28 - 4 - (5 * (cols - 1))) / cols;
                    bool isWideCard = (testCardW >= 140);
                    int cardH = isWideCard 
                        ? (int)Math.Round(48 + 20 * fanScale) 
                        : (int)Math.Round(42 + 10 * fanScale);
                    defaultH += (expectedRows * (cardH + 5)) + 40;
                }
                else defaultH += 60;
            }
            int initH = Math.Max(420, _settings.WindowHeight > 0 ? _settings.WindowHeight : defaultH);
            this.ClientSize = new Size(initW, initH);
            this.MinimumSize = new Size(380, 420);

            // 1. Header Panel
            _headerPanel = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(this.ClientSize.Width, 36),
                BackColor = Color.FromArgb(18, 18, 22),
                Cursor = Cursors.SizeAll
            };
            _headerPanel.MouseDown += OnHeaderMouseDown;
            this.Controls.Add(_headerPanel);

            _picAppIcon = new PictureBox
            {
                Location = new Point(10, 7),
                Size = new Size(22, 22),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = _isQuietMode ? AppIcons.QuietBitmap : AppIcons.NormalBitmap,
                Cursor = Cursors.SizeAll
            };
            _picAppIcon.MouseDown += OnHeaderMouseDown;
            _headerPanel.Controls.Add(_picAppIcon);

            _lblTitle = new Label
            {
                Text = "RyzenQuiet PRO v3.0",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 245),
                Location = new Point(36, 8),
                AutoSize = true,
                Cursor = Cursors.SizeAll
            };
            _lblTitle.MouseDown += OnHeaderMouseDown;
            _headerPanel.Controls.Add(_lblTitle);

            _toolTip = new ToolTip();

            // Info / Hardware Support Button
            _btnInfo = new Label
            {
                Text = "ℹ",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(160, 160, 175),
                Size = new Size(26, 24),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _btnInfo.MouseEnter += (s, e) => _btnInfo.ForeColor = Color.White;
            _btnInfo.MouseLeave += (s, e) => _btnInfo.ForeColor = Color.FromArgb(160, 160, 175);
            _btnInfo.Click += (s, e) => ShowHardwareInfoDialog();
            _headerPanel.Controls.Add(_btnInfo);

            // Pin / Always-On-Top Button
            _btnPin = new Label
            {
                Text = _alwaysOnTop ? "📌" : "📍",
                Font = new Font("Segoe UI", 11f),
                ForeColor = _alwaysOnTop ? Color.FromArgb(80, 220, 140) : Color.FromArgb(130, 130, 140),
                Size = new Size(26, 24),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _btnPin.Click += (s, e) => ToggleAlwaysOnTop();
            _headerPanel.Controls.Add(_btnPin);

            // Detach / Dock Button
            _btnDetach = new Label
            {
                Text = _isDetached ? "⤡" : "⤢",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = _isDetached ? Color.FromArgb(56, 189, 248) : Color.FromArgb(160, 160, 170),
                Size = new Size(26, 24),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _btnDetach.Click += (s, e) => ToggleDetachedMode();
            _headerPanel.Controls.Add(_btnDetach);

            // Close Button
            _btnClose = new Label
            {
                Text = "✕",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(160, 160, 170),
                Size = new Size(24, 24),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _btnClose.Click += (s, e) => this.Hide();
            _headerPanel.Controls.Add(_btnClose);

            UpdateTooltips();

            // 2. Metric Labels & Panels (Graphs)
            Color cpuColor = _isQuietMode ? Color.FromArgb(80, 220, 140) : Color.FromArgb(250, 180, 80);

            // CPU Module
            _sepCpu = new ModuleSeparator(cpuColor);
            this.Controls.Add(_sepCpu);

            _lblCpu = CreateMetricHeader("CPU: 0%", cpuColor);
            _lblCpuSub = CreateMetricSubHeader("0.0 GHz | Cores");
            this.Controls.Add(_lblCpu);
            this.Controls.Add(_lblCpuSub);

            _pnlCpuGraph = CreateGraphPanel();
            _pnlCpuGraph.Paint += DrawCpuGraph;
            this.Controls.Add(_pnlCpuGraph);

            _pnlCoresMatrix = new SmoothPanel
            {
                BackColor = Color.FromArgb(16, 16, 20)
            };
            _pnlCoresMatrix.Paint += DrawCoresMatrix;
            this.Controls.Add(_pnlCoresMatrix);

            _pnlTopProcesses = new SmoothPanel
            {
                BackColor = Color.FromArgb(16, 16, 20),
                Visible = _settings.ShowTopProcesses
            };
            _pnlTopProcesses.Paint += DrawTopProcesses;
            this.Controls.Add(_pnlTopProcesses);

            // RAM Module
            _sepRam = new ModuleSeparator(Color.FromArgb(56, 189, 248));
            this.Controls.Add(_sepRam);

            _lblRam = CreateMetricHeader("RAM: 0%", Color.FromArgb(56, 189, 248));
            _lblRamSub = CreateMetricSubHeader("0.0 / 0.0 GB");
            this.Controls.Add(_lblRam);
            this.Controls.Add(_lblRamSub);

            _pnlRamGraph = CreateGraphPanel();
            _pnlRamGraph.Paint += DrawRamGraph;
            this.Controls.Add(_pnlRamGraph);

            // GPU Module
            _sepGpu = new ModuleSeparator(Color.FromArgb(168, 85, 247));
            this.Controls.Add(_sepGpu);

            _lblGpu = CreateMetricHeader("GPU: 0%", Color.FromArgb(168, 85, 247));
            _pnlGpuSub = new GpuSubHeaderPanel();
            this.Controls.Add(_lblGpu);
            this.Controls.Add(_pnlGpuSub);

            _chkShowAllGpus = new CheckBox
            {
                Text = "Show all",
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = Color.FromArgb(160, 160, 180),
                AutoSize = true,
                Checked = _settings.ShowAllGpus,
                Cursor = Cursors.Hand,
                Visible = _hardware.Gpu.GpuCount > 1
            };
            _chkShowAllGpus.CheckedChanged += (s, e) =>
            {
                _settings.ShowAllGpus = _chkShowAllGpus.Checked;
                _settings.Save();
                UpdateMetricsUI();
            };
            this.Controls.Add(_chkShowAllGpus);

            _pnlGpuGraph = CreateGraphPanel();
            _pnlGpuGraph.Paint += DrawGpuGraph;
            this.Controls.Add(_pnlGpuGraph);

            // VRAM Module
            _sepVram = new ModuleSeparator(Color.FromArgb(236, 72, 153));
            this.Controls.Add(_sepVram);

            _lblVram = CreateMetricHeader("VRAM: 0%", Color.FromArgb(236, 72, 153));
            _lblVramSub = CreateMetricSubHeader("0.0 / 0.0 GB");
            this.Controls.Add(_lblVram);
            this.Controls.Add(_lblVramSub);

            _pnlVramGraph = CreateGraphPanel();
            _pnlVramGraph.Paint += DrawVramGraph;
            this.Controls.Add(_pnlVramGraph);

            // DISK Module
            _sepDisk = new ModuleSeparator(Color.FromArgb(6, 182, 212));
            this.Controls.Add(_sepDisk);

            _lblDisk = CreateMetricHeader("DISK: 0%", Color.FromArgb(6, 182, 212));
            _lblDiskSub = CreateMetricSubHeader("R:0.0 W:0.0 MB/s [Idle]");
            this.Controls.Add(_lblDisk);
            this.Controls.Add(_lblDiskSub);

            _pnlDiskGraph = CreateGraphPanel();
            _pnlDiskGraph.Paint += DrawDiskGraph;
            this.Controls.Add(_pnlDiskGraph);

            // FANS Module
            _sepFans = new ModuleSeparator(Color.FromArgb(14, 165, 233));
            this.Controls.Add(_sepFans);

            _lblFans = CreateMetricHeader("FANS: 0 RPM", Color.FromArgb(14, 165, 233));
            _lblFansSub = CreateMetricSubHeader(Loc.Get("FanPluginStatus_Disabled"));
            this.Controls.Add(_lblFans);
            this.Controls.Add(_lblFansSub);

            _pnlFansGraph = CreateGraphPanel();
            _pnlFansGraph.Paint += DrawFansGraph;
            _pnlFansGraph.MouseWheel += OnFansPanelMouseWheel;
            _pnlFansGraph.MouseDown += OnFansPanelMouseDown;
            _pnlFansGraph.MouseMove += OnFansPanelMouseMove;
            _pnlFansGraph.MouseUp += OnFansPanelMouseUp;
            _pnlFansGraph.MouseClick += OnFansPanelMouseClick;
            _pnlFansGraph.MouseEnter += (s, e) => {
                if (_settings.FanVisualMode == 1) _pnlFansGraph.Focus();
            };
            this.Controls.Add(_pnlFansGraph);

            // 3. Footer Panel (Fixed height at bottom: Mode switch + Checkboxes)
            // 3. Footer Panel (Slim row: Silent, Boost, Gear)
            _footerPanel = new Panel
            {
                BackColor = Color.FromArgb(18, 18, 22)
            };
            this.Controls.Add(_footerPanel);

            _btnSilent = new Button
            {
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };
            _btnSilent.FlatAppearance.BorderSize = 0;
            _btnSilent.Click += (s, e) => {
                if (!_isQuietMode) _onModeChangeRequested(PowerPlanManager.Mode.Quiet);
            };
            _footerPanel.Controls.Add(_btnSilent);

            _btnBoost = new Button
            {
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };
            _btnBoost.FlatAppearance.BorderSize = 0;
            _btnBoost.Click += (s, e) => {
                if (_isQuietMode) _onModeChangeRequested(PowerPlanManager.Mode.Normal);
            };
            _footerPanel.Controls.Add(_btnBoost);

            _btnGear = new Button
            {
                Text = "",
                BackColor = Color.FromArgb(28, 28, 34),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _btnGear.FlatAppearance.BorderSize = 0;
            _btnGear.MouseEnter += (s, e) => {
                _gearHover = true;
                _btnGear.BackColor = Color.FromArgb(42, 42, 52);
                _btnGear.Invalidate();
            };
            _btnGear.MouseLeave += (s, e) => {
                _gearHover = false;
                _btnGear.BackColor = Color.FromArgb(28, 28, 34);
                _btnGear.Invalidate();
            };
            _btnGear.Paint += (s, e) => {
                DrawGearIcon(e.Graphics, _btnGear.ClientRectangle, _gearHover ? Color.White : Color.FromArgb(175, 175, 190));
            };
            _btnGear.Click += (s, e) => ShowSettingsDialog();
            _footerPanel.Controls.Add(_btnGear);

            _btnGpuSilent = new Button
            {
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };
            _btnGpuSilent.FlatAppearance.BorderSize = 0;
            _btnGpuSilent.Click += (s, e) => {
                if (!_hardware.GpuTuning.IsGpuQuietMode)
                {
                    _hardware.GpuTuning.ApplyMode(true);
                    UpdateModeUI();
                }
            };
            _footerPanel.Controls.Add(_btnGpuSilent);

            _btnGpuBoost = new Button
            {
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };
            _btnGpuBoost.FlatAppearance.BorderSize = 0;
            _btnGpuBoost.Click += (s, e) => {
                if (_hardware.GpuTuning.IsGpuQuietMode)
                {
                    _hardware.GpuTuning.ApplyMode(false);
                    UpdateModeUI();
                }
            };
            _footerPanel.Controls.Add(_btnGpuBoost);

            // Resize Grip in bottom-right corner
            _lblResizeGrip = new Label
            {
                Text = "◢",
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = Color.FromArgb(80, 80, 95),
                Size = new Size(16, 16),
                TextAlign = ContentAlignment.BottomRight,
                Cursor = Cursors.SizeNWSE
            };
            _lblResizeGrip.MouseDown += OnResizeGripMouseDown;
            _footerPanel.Controls.Add(_lblResizeGrip);

            UpdateModeUI();
            _isInitialized = true;
            LayoutComponents();
        }

        private void LayoutComponents()
        {
            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;

            int marginX = 14;
            int contentW = Math.Max(200, w - (marginX * 2));

            // 1. Header Layout
            _headerPanel.Location = new Point(0, 0);
            _headerPanel.Size = new Size(w, 36);
            _picAppIcon.Location = new Point(10, 7);
            _lblTitle.Location = new Point(36, 8);
            _btnClose.Location = new Point(w - 32, 7);
            _btnDetach.Location = new Point(w - 60, 5);
            _btnPin.Location = new Point(w - 88, 6);
            _btnInfo.Location = new Point(w - 116, 6);

            // 2. Footer Layout (Single row unified, or two rows when separate CPU/GPU control is enabled)
            bool separate = _settings.SeparateCpuGpuControl;
            int footerH = separate ? 78 : 44;
            int footerY = h - footerH;
            _footerPanel.Location = new Point(0, footerY);
            _footerPanel.Size = new Size(w, footerH);
            _footerPanel.BringToFront();

            int gearW = 34;
            int buttonGap = 4;
            int availableBtnW = contentW - gearW - buttonGap;
            int halfW = (availableBtnW - buttonGap) / 2;

            // Row 1: CPU Controls
            _btnSilent.Location = new Point(marginX, 6);
            _btnSilent.Size = new Size(halfW, 32);

            _btnBoost.Location = new Point(marginX + halfW + buttonGap, 6);
            _btnBoost.Size = new Size(availableBtnW - halfW - buttonGap, 32);

            // Single unified Gear button on the right (centered vertically across 1 or 2 rows)
            int gearH = separate ? (42 + 32 - 6) : 32;
            _btnGear.Location = new Point(marginX + contentW - gearW, 6);
            _btnGear.Size = new Size(gearW, gearH);

            // Row 2: GPU Controls (visible only when separate controls are enabled)
            _btnGpuSilent.Visible = separate;
            _btnGpuBoost.Visible = separate;

            if (separate)
            {
                _btnGpuSilent.Location = new Point(marginX, 42);
                _btnGpuSilent.Size = new Size(halfW, 32);

                _btnGpuBoost.Location = new Point(marginX + halfW + buttonGap, 42);
                _btnGpuBoost.Size = new Size(availableBtnW - halfW - buttonGap, 32);
            }

            _lblResizeGrip.Location = new Point(w - 18, footerH - 18);

            // 3. Middle Area: Dynamic Graph Sections
            int topY = 38;
            int availableH = Math.Max(180, (footerY - 8) - topY);

            bool showCpu = _settings.ShowCpuGraph || _settings.ShowCpuCores || _settings.ShowTopProcesses;
            bool showRam = _settings.ShowRamGraph;
            bool showGpu = _settings.ShowGpuGraph;
            bool showVram = _settings.ShowVramGraph;
            bool showDisk = _settings.ShowDiskGraph;
            bool showFans = _settings.ShowFanGraph;

            int activeGraphCount = 0;
            if (_settings.ShowCpuGraph) activeGraphCount++;
            if (_settings.ShowRamGraph) activeGraphCount++;
            if (_settings.ShowGpuGraph) activeGraphCount++;
            if (_settings.ShowVramGraph) activeGraphCount++;
            if (_settings.ShowDiskGraph) activeGraphCount++;
            if (_settings.ShowFanGraph) activeGraphCount++;
            if (activeGraphCount == 0) activeGraphCount = 1;

            int fixedH = 0;
            if (showCpu)
            {
                fixedH += 3 + 5 + 20; // separator + header
                if (_settings.ShowCpuCores) fixedH += 22 + 4; // matrix
                if (_settings.ShowTopProcesses) fixedH += 90 + 6; // top 5
            }
            if (showRam) fixedH += 3 + 5 + 20 + 6;
            if (showGpu) fixedH += 3 + 5 + 20 + 6;
            if (showVram) fixedH += 3 + 5 + 20 + (showDisk || showFans ? 6 : 0);
            if (showDisk) fixedH += 3 + 5 + 20 + (showFans ? 6 : 0);
            if (showFans) fixedH += 3 + 6 + 24;

            int fanH;
            int graphH;
            if (showFans && _settings.FanVisualMode == 1)
            {
                float fanScale = _settings.FanScale;
                fanH = (int)Math.Round(72 * fanScale) + 4;
                fixedH += fanH;
                int nonFanGraphs = activeGraphCount - 1;
                if (nonFanGraphs <= 0) nonFanGraphs = 1;
                int remainingForGraphs = Math.Max(30, availableH - fixedH);
                graphH = Math.Max(24, remainingForGraphs / nonFanGraphs);
            }
            else if (showFans && _settings.FanVisualMode == 2)
            {
                var allFans = _hardware.Fans.Fans;
                int visibleFanCount = allFans.Count(f => _settings.IsFanVisible(f.Id, f.Name));
                if (visibleFanCount == 0) visibleFanCount = 1;
                float fanScale = _settings.FanScale;
                int cols;
                if (fanScale >= 1.40f)
                {
                    cols = Math.Max(2, (contentW - 4) / (int)(95 * fanScale));
                    if (contentW < 460) cols = 2;
                }
                else
                {
                    cols = Math.Max(2, (contentW - 4) / 115);
                    if (contentW < 460) cols = 3;
                    if (contentW < 320) cols = 2;
                }

                int rows = (int)Math.Ceiling((double)visibleFanCount / cols);
                if (rows < 1) rows = 1;
                int cardW = (contentW - 4 - (5 * (cols - 1))) / cols;
                bool isWideCard = (cardW >= 140);
                int cardH = isWideCard 
                    ? (int)Math.Round(48 + 20 * fanScale) 
                    : (int)Math.Round(42 + 10 * fanScale);
                int gapY = 5;
                fanH = (rows * cardH) + ((rows - 1) * gapY) + 8;
                fixedH += fanH;
                int nonFanGraphs = activeGraphCount - 1;
                if (nonFanGraphs <= 0) nonFanGraphs = 1;
                int remainingForGraphs = Math.Max(30, availableH - fixedH);
                graphH = Math.Max(24, remainingForGraphs / nonFanGraphs);
            }
            else
            {
                int remainingForGraphs = Math.Max(40, availableH - fixedH);
                graphH = Math.Max(24, remainingForGraphs / activeGraphCount);
                fanH = graphH;
            }

            int curY = topY;

            // --- CPU ---
            _sepCpu.Visible = showCpu;
            _lblCpu.Visible = showCpu;
            _lblCpuSub.Visible = showCpu;
            _pnlCpuGraph.Visible = _settings.ShowCpuGraph;
            _pnlCoresMatrix.Visible = _settings.ShowCpuCores;

            if (showCpu)
            {
                _sepCpu.Location = new Point(0, curY);
                _sepCpu.Size = new Size(w, 3);
                curY += 5;

                _lblCpu.Location = new Point(marginX, curY);
                _lblCpuSub.Location = new Point(marginX + 95, curY);
                _lblCpuSub.Size = new Size(contentW - 95, 20);
                curY += 20;

                if (_settings.ShowCpuGraph)
                {
                    _pnlCpuGraph.Location = new Point(marginX, curY);
                    _pnlCpuGraph.Size = new Size(contentW, graphH);
                    curY += graphH + 2;
                }

                if (_settings.ShowCpuCores)
                {
                    _pnlCoresMatrix.Location = new Point(marginX, curY);
                    _pnlCoresMatrix.Size = new Size(contentW, 22);
                    curY += 22 + 4;
                }

                if (_settings.ShowTopProcesses)
                {
                    _pnlTopProcesses.Visible = true;
                    _pnlTopProcesses.Location = new Point(marginX, curY);
                    _pnlTopProcesses.Size = new Size(contentW, 90);
                    curY += 90 + 6;
                }
                else
                {
                    _pnlTopProcesses.Visible = false;
                    curY += 2;
                }
            }
            else
            {
                _pnlTopProcesses.Visible = false;
            }

            // --- RAM ---
            _sepRam.Visible = showRam;
            _lblRam.Visible = showRam;
            _lblRamSub.Visible = showRam;
            _pnlRamGraph.Visible = showRam;

            if (showRam)
            {
                _sepRam.Location = new Point(0, curY);
                _sepRam.Size = new Size(w, 3);
                curY += 5;

                _lblRam.Location = new Point(marginX, curY);
                _lblRamSub.Location = new Point(marginX + 95, curY);
                _lblRamSub.Size = new Size(contentW - 95, 20);
                curY += 20;

                _pnlRamGraph.Location = new Point(marginX, curY);
                _pnlRamGraph.Size = new Size(contentW, graphH);
                curY += graphH + 6;
            }

            // --- GPU ---
            _sepGpu.Visible = showGpu;
            _lblGpu.Visible = showGpu;
            _pnlGpuSub.Visible = showGpu;
            _pnlGpuGraph.Visible = showGpu;

            bool multiGpu = _hardware.Gpu.GpuCount > 1;
            _chkShowAllGpus.Visible = showGpu && multiGpu;

            if (showGpu)
            {
                _sepGpu.Location = new Point(0, curY);
                _sepGpu.Size = new Size(w, 3);
                curY += 5;

                _lblGpu.Location = new Point(marginX, curY);

                if (multiGpu)
                {
                    int chkW = 74;
                    _chkShowAllGpus.Location = new Point(w - marginX - chkW, curY + 2);
                    _pnlGpuSub.Location = new Point(marginX + 95, curY);
                    _pnlGpuSub.Size = new Size(Math.Max(50, w - marginX - chkW - (marginX + 95) - 4), 20);
                }
                else
                {
                    _pnlGpuSub.Location = new Point(marginX + 95, curY);
                    _pnlGpuSub.Size = new Size(contentW - 95, 20);
                }
                curY += 20;

                _pnlGpuGraph.Location = new Point(marginX, curY);
                _pnlGpuGraph.Size = new Size(contentW, graphH);
                curY += graphH + 6;
            }

            // --- VRAM ---
            _sepVram.Visible = showVram;
            _lblVram.Visible = showVram;
            _lblVramSub.Visible = showVram;
            _pnlVramGraph.Visible = showVram;

            if (showVram)
            {
                _sepVram.Location = new Point(0, curY);
                _sepVram.Size = new Size(w, 3);
                curY += 5;

                _lblVram.Location = new Point(marginX, curY);
                _lblVramSub.Location = new Point(marginX + 95, curY);
                _lblVramSub.Size = new Size(contentW - 95, 20);
                curY += 20;

                _pnlVramGraph.Location = new Point(marginX, curY);
                _pnlVramGraph.Size = new Size(contentW, graphH);
                curY += graphH + (showDisk || showFans ? 6 : 0);
            }

            // --- DISK ---
            _sepDisk.Visible = showDisk;
            _lblDisk.Visible = showDisk;
            _lblDiskSub.Visible = showDisk;
            _pnlDiskGraph.Visible = showDisk;

            if (showDisk)
            {
                _sepDisk.Location = new Point(0, curY);
                _sepDisk.Size = new Size(w, 3);
                curY += 5;

                _lblDisk.Location = new Point(marginX, curY);
                _lblDiskSub.Location = new Point(marginX + 95, curY);
                _lblDiskSub.Size = new Size(contentW - 95, 20);
                curY += 20;

                _pnlDiskGraph.Location = new Point(marginX, curY);
                _pnlDiskGraph.Size = new Size(contentW, graphH);
                curY += graphH + (showFans ? 6 : 0);
            }

            // --- FANS ---
            _sepFans.Visible = showFans;
            _lblFans.Visible = showFans;
            _lblFansSub.Visible = showFans;
            _pnlFansGraph.Visible = showFans;

            if (showFans)
            {
                _sepFans.Location = new Point(0, curY);
                _sepFans.Size = new Size(w, 3);
                curY += 6;

                _lblFans.Location = new Point(marginX, curY);
                _lblFansSub.Location = new Point(marginX + 95, curY);
                _lblFansSub.Size = new Size(contentW - 95, 18);
                curY += 24;

                _pnlFansGraph.Location = new Point(marginX, curY);
                _pnlFansGraph.Size = new Size(contentW, fanH);
            }
        }

        private void ShowHardwareInfoDialog()
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

        private void ShowSettingsDialog()
        {
            if (_settingsForm == null || _settingsForm.IsDisposed)
            {
                _settingsForm = new SettingsForm(_hardware, _settings, () => {
                    _hardware.Fans.DemoMode = _settings.EnableFanDemo;
                    _hardware.Fans.SetEnabled((_settings.ShowFanGraph && _settings.EnableFanAddon) || _settings.GpuTuningEnabled);
                    LayoutComponents();
                    this.Invalidate();
                    _onSettingsChanged?.Invoke();
                }, _onModeChangeRequested);

                _settingsForm.FormClosed += (s, e) => {
                    this.Opacity = _settings.WidgetOpacity;
                };
            }
            _settingsForm.TopMost = this.TopMost;

            int x = this.Location.X + (this.Width - _settingsForm.Width) / 2;
            int y = this.Location.Y + (this.Height - _settingsForm.Height) / 2;
            var screen = Screen.FromPoint(this.Location);
            x = Math.Clamp(x, screen.WorkingArea.Left + 10, screen.WorkingArea.Right - _settingsForm.Width - 10);
            y = Math.Clamp(y, screen.WorkingArea.Top + 10, screen.WorkingArea.Bottom - _settingsForm.Height - 10);
            _settingsForm.Location = new Point(x, y);

            this.Opacity = 0.35; // Dim the background dashboard ("в дымке")

            if (!_settingsForm.Visible)
            {
                _settingsForm.Show(this);
            }
            _settingsForm.BringToFront();
            _settingsForm.Activate();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (_settingsForm != null && !_settingsForm.IsDisposed && _settingsForm.Visible)
            {
                _settingsForm.BringToFront();
                _settingsForm.Activate();
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!_isInitialized) return;
            LayoutComponents();
            if (this.WindowState == FormWindowState.Normal && this.Visible)
            {
                _settings.WindowWidth = this.ClientSize.Width;
                _settings.WindowHeight = this.ClientSize.Height;
                _settings.Save();
            }
        }

        private static void DrawGearIcon(Graphics g, Rectangle bounds, Color color)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = bounds.X + bounds.Width / 2f;
            float cy = bounds.Y + bounds.Height / 2f;
            float ro = 10.5f;
            float ri = 7.4f;
            float rh = 3.6f;

            var pts = new List<PointF>(36);
            for (int i = 0; i < 6; i++)
            {
                float baseAngle = i * 60f;
                float[] angles = new float[] {
                    baseAngle - 15f,
                    baseAngle - 9f,
                    baseAngle + 9f,
                    baseAngle + 15f,
                    baseAngle + 30f,
                    baseAngle + 45f
                };
                float[] radii = new float[] { ri, ro, ro, ri, ri, ri };
                for (int j = 0; j < 6; j++)
                {
                    float rad = (float)(angles[j] * Math.PI / 180.0);
                    pts.Add(new PointF(cx + (float)(radii[j] * Math.Cos(rad)), cy + (float)(radii[j] * Math.Sin(rad))));
                }
            }

            using var path = new GraphicsPath(FillMode.Alternate);
            path.AddPolygon(pts.ToArray());
            path.AddEllipse(cx - rh, cy - rh, rh * 2f, rh * 2f);

            using var brush = new SolidBrush(color);
            g.FillPath(brush, path);
        }

        private Label CreateMetricHeader(string text, Color color)
        {
            return new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = color,
                AutoSize = true
            };
        }

        private Label CreateMetricSubHeader(string text)
        {
            return new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 8.0f),
                ForeColor = Color.FromArgb(165, 165, 180),
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true
            };
        }

        private class GpuSubHeaderPanel : Control
        {
            public string GpuName { get; set; } = "GPU";
            public float Temperature { get; set; } = 0f;
            public float PowerWatts { get; set; } = 0f;
            public uint FanSpeedPercent { get; set; } = 0;
            public int FanRpm { get; set; } = -1;
            public bool ShowFanSpeed { get; set; } = true;
            public string MultiGpuText { get; set; } = "";
            public bool IsMultiGpu { get; set; } = false;
            public bool ShowTempSquare { get; set; } = true;

            public GpuSubHeaderPanel()
            {
                this.DoubleBuffered = true;
                this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
                this.UpdateStyles();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                int w = this.Width;
                int h = this.Height;
                if (w <= 0 || h <= 0) return;

                using var font = new Font("Segoe UI", 8.0f);

                if (IsMultiGpu)
                {
                    TextRenderer.DrawText(g, MultiGpuText, font, new Rectangle(0, 0, w, h),
                        Color.FromArgb(165, 165, 180),
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    return;
                }

                int curRight = w;

                // 1. Temperature (rightmost)
                if (Temperature > 0)
                {
                    string tempStr = $"{Temperature:F0}°C";
                    Size tempSize = TextRenderer.MeasureText(g, tempStr, font, Size.Empty, TextFormatFlags.NoPadding);
                    int tempW = tempSize.Width + 4;
                    int tempX = curRight - tempW;

                    TextRenderer.DrawText(g, tempStr, font, new Rectangle(tempX, 0, tempW, h),
                        Color.FromArgb(215, 215, 225),
                        TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
                    curRight = tempX;

                    if (ShowTempSquare)
                    {
                        int sqSize = 8;
                        int sqX = curRight - sqSize - 4;
                        int sqY = (h - sqSize) / 2;

                        using var sqBrush = new SolidBrush(GpuTempColor);
                        g.FillRectangle(sqBrush, sqX, sqY, sqSize, sqSize);
                        using var sqPen = new Pen(Color.FromArgb(195, 155, 245), 1f);
                        g.DrawRectangle(sqPen, sqX, sqY, sqSize, sqSize);

                        curRight = sqX - 6;
                    }
                    else
                    {
                        curRight -= 6;
                    }
                }

                // 2. Fan Speed
                if (ShowFanSpeed)
                {
                    string fanStr = "";
                    Color fanColor = Color.FromArgb(56, 189, 248);

                    if (FanRpm > 0)
                    {
                        fanStr = (FanSpeedPercent > 0)
                            ? $"💨 {FanSpeedPercent}% ({FanRpm:N0})"
                            : $"💨 {FanRpm:N0} RPM";
                    }
                    else if (FanRpm == 0)
                    {
                        fanStr = "💤 0 RPM (0dB)";
                        fanColor = Color.FromArgb(135, 140, 155);
                    }
                    else if (FanSpeedPercent > 0)
                    {
                        fanStr = $"💨 {FanSpeedPercent}%";
                    }

                    if (!string.IsNullOrEmpty(fanStr))
                    {
                        Size fanSize = TextRenderer.MeasureText(g, fanStr, font, Size.Empty, TextFormatFlags.NoPadding);
                        int fanW = fanSize.Width + 4;
                        int fanX = curRight - fanW;

                        TextRenderer.DrawText(g, fanStr, font, new Rectangle(fanX, 0, fanW, h),
                            fanColor,
                            TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);

                        curRight = fanX - 8;
                    }
                }

                // 3. GPU Name (left of fan/temp) - now has full horizontal space
                if (curRight > 20)
                {
                    TextRenderer.DrawText(g, GpuName, font, new Rectangle(0, 0, curRight - 4, h),
                        Color.FromArgb(165, 165, 180),
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                }
            }
        }

        private class SmoothPanel : Panel
        {
            public SmoothPanel()
            {
                this.DoubleBuffered = true;
                this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
                this.SetStyle(ControlStyles.Selectable, true);
                this.UpdateStyles();
            }
        }

        private SmoothPanel CreateGraphPanel()
        {
            return new SmoothPanel
            {
                BackColor = Color.FromArgb(16, 16, 20)
            };
        }

        private class ModuleSeparator : Control
        {
            private Color _accentColor;

            public Color AccentColor
            {
                get => _accentColor;
                set
                {
                    if (_accentColor != value)
                    {
                        _accentColor = value;
                        Invalidate();
                    }
                }
            }

            public ModuleSeparator(Color accentColor)
            {
                _accentColor = accentColor;
                this.DoubleBuffered = true;
                this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
                this.Height = 3;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                int w = this.Width;
                if (w <= 0) return;

                // 1. Top dark line
                using (var darkPen = new Pen(Color.FromArgb(12, 12, 16), 1f))
                {
                    g.DrawLine(darkPen, 0, 0, w, 0);
                }

                // 2. Accent color line of the block
                using (var accentPen = new Pen(_accentColor, 1.5f))
                {
                    g.DrawLine(accentPen, 0, 1.5f, w, 1.5f);
                }
            }
        }

        private void OnHeaderMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (!_isDetached)
                {
                    _isDetached = true;
                    _settings.IsDetached = true;
                    _settings.Save();
                    _btnDetach.Text = "⤡";
                    _btnDetach.ForeColor = Color.FromArgb(56, 189, 248);
                }
                ReleaseCapture();
                SendMessage(this.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }

        private void OnResizeGripMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(this.Handle, WM_NCLBUTTONDOWN, HTBOTTOMRIGHT, 0);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if ((int)m.Result == HTCLIENT)
                {
                    int screenX = (short)(m.LParam.ToInt64() & 0xFFFF);
                    int screenY = (short)((m.LParam.ToInt64() >> 16) & 0xFFFF);
                    Point pt = this.PointToClient(new Point(screenX, screenY));
                    int border = 8;

                    bool left = pt.X <= border;
                    bool right = pt.X >= this.ClientSize.Width - border;
                    bool top = pt.Y <= border;
                    bool bottom = pt.Y >= this.ClientSize.Height - border;

                    if (top && left) { m.Result = (IntPtr)HTTOPLEFT; return; }
                    if (top && right) { m.Result = (IntPtr)HTTOPRIGHT; return; }
                    if (bottom && left) { m.Result = (IntPtr)HTBOTTOMLEFT; return; }
                    if (bottom && right) { m.Result = (IntPtr)HTBOTTOMRIGHT; return; }
                    if (left) { m.Result = (IntPtr)HTLEFT; return; }
                    if (right) { m.Result = (IntPtr)HTRIGHT; return; }
                    if (top) { m.Result = (IntPtr)HTTOP; return; }
                    if (bottom) { m.Result = (IntPtr)HTBOTTOM; return; }
                }
                return;
            }

            base.WndProc(ref m);
        }

        private void OnWindowLocationChanged(object? sender, EventArgs e)
        {
            if (_isDetached && this.Visible && this.WindowState == FormWindowState.Normal)
            {
                _settings.WindowX = this.Location.X;
                _settings.WindowY = this.Location.Y;
                _settings.Save();
            }
        }

        private void OnWindowDeactivated(object? sender, EventArgs e)
        {
            if (!_isDetached && this.Visible)
            {
                this.Hide();
            }
        }

        public void ToggleDetachedMode()
        {
            _isDetached = !_isDetached;
            _settings.IsDetached = _isDetached;
            _settings.Save();

            _btnDetach.Text = _isDetached ? "⤡" : "⤢";
            _btnDetach.ForeColor = _isDetached ? Color.FromArgb(56, 189, 248) : Color.FromArgb(160, 160, 170);
            UpdateTooltips();

            if (!_isDetached)
            {
                PositionAtTray();
            }
        }

        public void ToggleAlwaysOnTop()
        {
            _alwaysOnTop = !_alwaysOnTop;
            this.TopMost = _alwaysOnTop;
            _settings.AlwaysOnTop = _alwaysOnTop;
            _settings.Save();

            _btnPin.Text = _alwaysOnTop ? "📌" : "📍";
            _btnPin.ForeColor = _alwaysOnTop ? Color.FromArgb(80, 220, 140) : Color.FromArgb(130, 130, 140);
            UpdateTooltips();
        }

        public void SetModeState(bool isQuietMode)
        {
            _isQuietMode = isQuietMode;
            this.Icon = _isQuietMode ? AppIcons.QuietIcon : AppIcons.NormalIcon;
            if (_picAppIcon != null)
            {
                _picAppIcon.Image = _isQuietMode ? AppIcons.QuietBitmap : AppIcons.NormalBitmap;
            }
            if (_sepCpu != null)
            {
                _sepCpu.AccentColor = _isQuietMode ? Color.FromArgb(80, 220, 140) : Color.FromArgb(250, 180, 80);
            }
            UpdateModeUI();
        }

        public void SetStartupState(bool enabled)
        {
            _isStartupEnabled = enabled;
        }

        public void SyncTopProcessesSetting()
        {
            _hardware.Processes.IsEnabled = _settings.ShowTopProcesses;
            LayoutComponents();
            this.Invalidate();
        }

        public void SyncShowAllGpusSetting()
        {
            if (_chkShowAllGpus != null && _chkShowAllGpus.Checked != _settings.ShowAllGpus)
            {
                _chkShowAllGpus.Checked = _settings.ShowAllGpus;
            }
            UpdateMetricsUI();
        }

        private void UpdateModeUI()
        {
            bool separate = _settings.SeparateCpuGpuControl;

            // 1. CPU Mode UI
            if (_isQuietMode)
            {
                _btnSilent.Text = separate ? Loc.Get("CpuSilentFull") : Loc.Get("SilentFull");
                _btnSilent.BackColor = Color.FromArgb(34, 150, 85);
                _btnSilent.ForeColor = Color.White;

                _btnBoost.Text = separate ? Loc.Get("CpuBoost") : Loc.Get("Boost");
                _btnBoost.BackColor = Color.FromArgb(28, 28, 34);
                _btnBoost.ForeColor = Color.FromArgb(140, 140, 150);
            }
            else
            {
                _btnSilent.Text = separate ? Loc.Get("CpuSilent") : Loc.Get("Silent");
                _btnSilent.BackColor = Color.FromArgb(28, 28, 34);
                _btnSilent.ForeColor = Color.FromArgb(140, 140, 150);

                _btnBoost.Text = separate ? Loc.Get("CpuBoostActive") : Loc.Get("BoostActive");
                _btnBoost.BackColor = Color.FromArgb(215, 135, 25);
                _btnBoost.ForeColor = Color.White;
            }

            // 2. GPU Mode UI (when separate controls are enabled)
            if (_btnGpuSilent != null && _btnGpuBoost != null)
            {
                bool gpuQuiet = _hardware.GpuTuning.IsGpuQuietMode;
                int quietW = _hardware.GpuTuning.CurrentAppliedWatts > 0 ? _hardware.GpuTuning.CurrentAppliedWatts : _settings.GpuSilentPowerWatts;
                int stockW = _hardware.GpuTuning.StockWatts > 0 ? _hardware.GpuTuning.StockWatts : 336;

                if (gpuQuiet)
                {
                    _btnGpuSilent.Text = string.Format(Loc.Get("GpuSilentFull"), quietW);
                    _btnGpuSilent.BackColor = Color.FromArgb(34, 150, 85);
                    _btnGpuSilent.ForeColor = Color.White;

                    _btnGpuBoost.Text = string.Format(Loc.Get("GpuBoost"), stockW);
                    _btnGpuBoost.BackColor = Color.FromArgb(28, 28, 34);
                    _btnGpuBoost.ForeColor = Color.FromArgb(140, 140, 150);
                }
                else
                {
                    _btnGpuSilent.Text = Loc.Get("GpuSilent");
                    _btnGpuSilent.BackColor = Color.FromArgb(28, 28, 34);
                    _btnGpuSilent.ForeColor = Color.FromArgb(140, 140, 150);

                    _btnGpuBoost.Text = string.Format(Loc.Get("GpuBoostActive"), stockW);
                    _btnGpuBoost.BackColor = Color.FromArgb(215, 135, 25);
                    _btnGpuBoost.ForeColor = Color.White;
                }
            }
        }

        private void UpdateTooltips()
        {
            if (_toolTip == null) return;
            _toolTip.SetToolTip(_btnInfo, Loc.Get("TipInfo"));
            _toolTip.SetToolTip(_btnPin, _alwaysOnTop ? Loc.Get("TipPinOn") : Loc.Get("TipPinOff"));
            _toolTip.SetToolTip(_btnDetach, _isDetached ? Loc.Get("TipDock") : Loc.Get("TipDetach"));
            _toolTip.SetToolTip(_btnClose, Loc.Get("TipClose"));
            if (_btnGear != null) _toolTip.SetToolTip(_btnGear, Loc.Get("MenuSettings"));
        }

        private void OnLanguageChanged()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(OnLanguageChanged));
                return;
            }
            UpdateTooltips();
            UpdateModeUI();
            if (_chkShowAllGpus != null) _chkShowAllGpus.Text = Loc.Get("ShowAllGpus");
            UpdateMetricsUI();
            LayoutComponents();
        }

        public void ShowDashboard()
        {
            if (_isDetached && _settings.WindowX >= 0 && _settings.WindowY >= 0)
            {
                var scr = Screen.FromPoint(new Point(_settings.WindowX, _settings.WindowY));
                int x = Math.Clamp(_settings.WindowX, scr.WorkingArea.Left, scr.WorkingArea.Right - this.Width);
                int y = Math.Clamp(_settings.WindowY, scr.WorkingArea.Top, scr.WorkingArea.Bottom - this.Height);
                this.Location = new Point(x, y);
            }
            else
            {
                PositionAtTray();
            }

            this.Show();
            this.BringToFront();
            this.Activate();
            UpdateMetricsUI();
        }

        private void PositionAtTray()
        {
            var workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
            int posX = workingArea.Right - this.Width; // 0px margin from right edge
            int posY = workingArea.Bottom - this.Height - 6; // 6px gap from taskbar
            this.Location = new Point(posX, posY);
        }

        private void UpdateMetricsUI()
        {
            // 1. CPU
            float cpuUsage = _hardware.Cpu.TotalCpuUsage;
            float freqGhz = _hardware.Cpu.CurrentFrequencyMHz / 1000f;
            _lblCpu.Text = $"{Loc.Get("Cpu")}: {cpuUsage:F0}%";
            _lblCpu.ForeColor = _isQuietMode ? Color.FromArgb(80, 220, 140) : Color.FromArgb(250, 180, 80);
            string cpuName = _hardware.Cpu.CpuShortName;
            string cpuCoreInfo = (freqGhz > 0.5f)
                ? $"{freqGhz:F2} GHz | {_hardware.Cpu.CoreCount} {Loc.Get("Threads")}"
                : $"{_hardware.Cpu.CoreCount} {Loc.Get("ThreadsActive")}";
            string cpuText = !string.IsNullOrEmpty(cpuName) ? $"{cpuName} | {cpuCoreInfo}" : cpuCoreInfo;
            if (_hardware.Fans.IsConnected)
            {
                var cpuFan = _hardware.Fans.Fans.FirstOrDefault(f => f.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase));
                if (cpuFan != null)
                {
                    cpuText += (cpuFan.CurrentRpm > 0) ? $" | 🌀 {cpuFan.CurrentRpm:N0} RPM" : " | 🌀 0 RPM";
                }
            }
            _lblCpuSub.Text = cpuText;

            // 2. RAM
            float ramUsage = _hardware.Ram.MemoryLoadPercent;
            _lblRam.Text = $"{Loc.Get("Ram")}: {ramUsage:F0}%";
            string ramSpecs = !string.IsNullOrEmpty(_hardware.Ram.MemorySpecs) ? $"{_hardware.Ram.MemorySpecs} | " : "";
            _lblRamSub.Text = $"{ramSpecs}{_hardware.Ram.UsedGb:F1} / {_hardware.Ram.TotalGb:F1} GB";

            // 3. GPU
            bool multiGpuActive = _hardware.Gpu.GpuCount > 1 && _settings.ShowAllGpus;
            if (multiGpuActive)
            {
                var devs = _hardware.Gpu.Devices;
                float avgGpu = 0f;
                string gpuDetail = "";
                for (int i = 0; i < devs.Count; i++)
                {
                    avgGpu += devs[i].GpuLoadPercent;
                    if (i > 0) gpuDetail += "  ";
                    gpuDetail += $"#{i}: {devs[i].GpuLoadPercent:F0}%";
                    if (devs[i].GpuPowerWatts > 0)
                    {
                        gpuDetail += $" {devs[i].GpuPowerWatts:F0}W";
                    }
                    if (_settings.ShowGpuFanSpeed)
                    {
                        if (devs[i].FanSpeedPercent > 0)
                            gpuDetail += $" {devs[i].FanSpeedPercent}%💨";
                        else
                            gpuDetail += " 0dB💨";
                    }
                    if (devs[i].GpuTemperatureC > 0)
                    {
                        gpuDetail += $" ({devs[i].GpuTemperatureC:F0}°)";
                    }
                }
                avgGpu /= devs.Count;
                _lblGpu.Text = $"{Loc.Get("Gpu")}: {avgGpu:F0}%";
                _pnlGpuSub.IsMultiGpu = true;
                _pnlGpuSub.MultiGpuText = gpuDetail;
                _pnlGpuSub.Invalidate();

                // 4. VRAM
                ulong totalVramBytes = 0;
                ulong usedVramBytes = 0;
                string vramDetail = "";
                for (int i = 0; i < devs.Count; i++)
                {
                    totalVramBytes += devs[i].VramTotalBytes;
                    usedVramBytes += devs[i].VramUsedBytes;
                    if (i > 0) vramDetail += "  ";
                    vramDetail += $"#{i}: {devs[i].VramUsedGb:F1}/{devs[i].VramTotalGb:F0}G";
                }
                float totalVramPercent = totalVramBytes > 0 ? (float)(usedVramBytes * 100.0 / totalVramBytes) : 0f;
                double totalUsedGb = usedVramBytes / (1024.0 * 1024 * 1024);
                double totalTotalGb = totalVramBytes / (1024.0 * 1024 * 1024);
                string vramType = _hardware.Gpu.VramType;
                _lblVram.Text = $"{Loc.Get("Vram")}: {totalVramPercent:F0}%";
                _lblVramSub.Text = $"{vramType} | {totalUsedGb:F1}/{totalTotalGb:F0} GB  ({vramDetail})";
            }
            else
            {
                float gpuUsage = _hardware.Gpu.GpuLoadPercent;
                _lblGpu.Text = $"{Loc.Get("Gpu")}: {gpuUsage:F0}%";

                int gpuFanRpm = -1;
                if (_hardware.Fans.IsConnected)
                {
                    var gpuFans = _hardware.Fans.Fans.Where(f => f.Hardware.Contains("GPU", StringComparison.OrdinalIgnoreCase) || f.Hardware.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || f.Hardware.Contains("AMD", StringComparison.OrdinalIgnoreCase)).ToList();
                    if (gpuFans.Count > 0)
                    {
                        gpuFanRpm = gpuFans.Max(f => f.CurrentRpm);
                    }
                }

                _pnlGpuSub.IsMultiGpu = false;
                _pnlGpuSub.GpuName = _hardware.Gpu.GpuName;
                _pnlGpuSub.Temperature = _hardware.Gpu.GpuTemperatureC;
                _pnlGpuSub.PowerWatts = _hardware.Gpu.GpuPowerWatts;
                _pnlGpuSub.FanSpeedPercent = _hardware.Gpu.FanSpeedPercent;
                _pnlGpuSub.FanRpm = gpuFanRpm;
                _pnlGpuSub.ShowFanSpeed = _settings.ShowGpuFanSpeed;
                _pnlGpuSub.ShowTempSquare = _settings.ShowGpuTempLine;
                _pnlGpuSub.Invalidate();

                // 4. VRAM
                float vramUsage = _hardware.Gpu.VramLoadPercent;
                string vramType = _hardware.Gpu.VramType;
                _lblVram.Text = $"{Loc.Get("Vram")}: {vramUsage:F0}%";
                _lblVramSub.Text = $"{vramType} | {_hardware.Gpu.VramUsedGb:F1} / {_hardware.Gpu.VramTotalGb:F1} GB";
            }

            // 5. DISK
            float diskUsage = _hardware.Disk.DiskLoadPercent;
            _lblDisk.Text = $"{Loc.Get("Disk")}: {diskUsage:F0}%";
            string diskIdleText = (_hardware.Disk.PeakActiveDisk == "Idle") ? Loc.Get("Idle") : _hardware.Disk.PeakActiveDisk;
            _lblDiskSub.Text = $"R:{_hardware.Disk.ReadMbPerSec:F1} W:{_hardware.Disk.WriteMbPerSec:F1} MB/s [{diskIdleText}]";

            // 6. FANS
            if (_settings.ShowFanGraph)
            {
                var fans = _hardware.Fans.Fans;
                if (_hardware.Fans.IsConnected && fans.Count > 0)
                {
                    _lblFans.Text = Loc.Get("Fans");
                    _lblFansSub.Text = "";
                }
                else
                {
                    _lblFans.Text = $"{Loc.Get("Fans")}: --";
                    string msg = _hardware.Fans.StatusMessage;
                    if (_hardware.Fans.IsConnected && fans.Count == 0)
                    {
                        msg = Loc.Get("FanPluginStatus_NoFans");
                    }
                    else if (string.IsNullOrEmpty(msg))
                    {
                        msg = !_settings.EnableFanAddon ? Loc.Get("FanPluginStatus_Disabled") : Loc.Get("FanPluginStatus_NotInstalled");
                    }
                    _lblFansSub.Text = msg;
                }
            }

            if (_pnlCpuGraph.Visible) _pnlCpuGraph.Invalidate();
            if (_pnlCoresMatrix.Visible) _pnlCoresMatrix.Invalidate();
            if (_pnlTopProcesses.Visible) _pnlTopProcesses.Invalidate();
            if (_pnlRamGraph.Visible) _pnlRamGraph.Invalidate();
            if (_pnlGpuGraph.Visible) _pnlGpuGraph.Invalidate();
            if (_pnlVramGraph.Visible) _pnlVramGraph.Invalidate();
            if (_pnlDiskGraph.Visible) _pnlDiskGraph.Invalidate();
            if (_pnlFansGraph.Visible) _pnlFansGraph.Invalidate();
        }

        private void DrawGraphInternal(Graphics g, Panel pnl, IReadOnlyList<float> history, Color strokeColor, Color topFill)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = pnl.Width;
            int h = pnl.Height;
            if (w <= 1 || h <= 1) return;

            // Grid lines (50%)
            using var gridPen = new Pen(Color.FromArgb(32, 32, 38), 1f);
            int midY = h / 2;
            g.DrawLine(gridPen, 0, midY, w, midY);

            if (history.Count < 2) return;

            var points = new PointF[history.Count];
            float stepX = (float)w / (history.Count - 1);

            for (int i = 0; i < history.Count; i++)
            {
                float val = Math.Clamp(history[i], 0f, 100f);
                float x = i * stepX;
                float y = h - (h * (val / 100f));
                points[i] = new PointF(x, y);
            }

            // Fill area
            using (var path = new GraphicsPath())
            {
                path.AddLines(points);
                path.AddLine(w, h, 0, h);
                path.CloseFigure();

                Color botFill = Color.FromArgb(8, 16, 16, 20);
                using var brush = new LinearGradientBrush(new Point(0, 0), new Point(0, h), topFill, botFill);
                g.FillPath(brush, path);
            }

            // Draw Curve Line
            using var linePen = new Pen(strokeColor, 1.8f);
            g.DrawLines(linePen, points);
        }

        private void DrawCpuGraph(object? sender, PaintEventArgs e)
        {
            Color stroke = _isQuietMode ? Color.FromArgb(80, 220, 140) : Color.FromArgb(250, 180, 80);
            Color fill = _isQuietMode ? Color.FromArgb(80, 40, 180, 100) : Color.FromArgb(80, 240, 160, 40);
            DrawGraphInternal(e.Graphics, _pnlCpuGraph, _hardware.Cpu.History, stroke, fill);
        }

        private void DrawRamGraph(object? sender, PaintEventArgs e)
        {
            Color stroke = Color.FromArgb(56, 189, 248);
            Color fill = Color.FromArgb(70, 30, 140, 220);
            DrawGraphInternal(e.Graphics, _pnlRamGraph, _hardware.Ram.History, stroke, fill);
        }

        private void DrawGpuGraph(object? sender, PaintEventArgs e)
        {
            if (_settings.ShowAllGpus && _hardware.Gpu.GpuCount > 1)
            {
                DrawMultiGraphInternal(e.Graphics, _pnlGpuGraph, _hardware.Gpu.Devices, false, MultiGpuColors);
            }
            else
            {
                Color stroke = Color.FromArgb(168, 85, 247);
                Color fill = Color.FromArgb(70, 140, 60, 220);
                DrawGraphInternal(e.Graphics, _pnlGpuGraph, _hardware.Gpu.GpuHistory, stroke, fill);
                if (_settings.ShowGpuTempLine)
                {
                    DrawTemperatureCurve(e.Graphics, _pnlGpuGraph, _hardware.Gpu.TempHistory);
                }
            }

            DrawGpuTuningOverlay(e.Graphics, _pnlGpuGraph);
        }

        private void DrawGpuTuningOverlay(Graphics g, Panel pnl)
        {
            int w = pnl.Width;
            int h = pnl.Height;
            if (w <= 80 || h <= 20) return;

            using var font = new Font("Segoe UI", 8.0f);

            // 1. Current Power Consumption (Yellow)
            float pwr = _hardware.Gpu.GpuPowerWatts;
            string pwrStr = pwr > 0 ? $"⚡ {pwr:F0} W" : "";

            // 2. Power Limit (Green if quiet/active, Gray if stock/inactive)
            bool tuningEnabled = _settings.GpuTuningEnabled;
            bool quietMode = _hardware.GpuTuning.IsGpuQuietMode;
            int currentWatts = _hardware.GpuTuning.CurrentAppliedWatts > 0 
                ? _hardware.GpuTuning.CurrentAppliedWatts 
                : _settings.GpuSilentPowerWatts;
            int stockWatts = _hardware.GpuTuning.StockWatts > 0 
                ? _hardware.GpuTuning.StockWatts 
                : 336;

            string plStr;
            Color plColor;

            if (tuningEnabled && quietMode)
            {
                plStr = $"PL: {currentWatts}W";
                plColor = Color.FromArgb(74, 222, 128); // Vibrant Green
            }
            else if (tuningEnabled)
            {
                plStr = $"PL: {stockWatts}W";
                plColor = Color.FromArgb(148, 163, 184); // Slate Gray
            }
            else
            {
                plStr = $"PL: {stockWatts}W";
                plColor = Color.FromArgb(130, 130, 140); // Muted Gray
            }

            // 3. Fan Cap (Green if active, Gray if Auto, Red if FailSafe)
            string fanCapStr;
            Color fanCapColor;

            if (tuningEnabled && _settings.GpuFanCapEnabled && quietMode)
            {
                if (_hardware.GpuTuning.IsFailSafeActive)
                {
                    fanCapStr = "Fan: ⚠️ Auto (83°C)";
                    fanCapColor = Color.FromArgb(248, 113, 113); // Danger Red
                }
                else
                {
                    fanCapStr = $"Fan: {_settings.GpuFanMaxPercent}%";
                    fanCapColor = Color.FromArgb(74, 222, 128); // Vibrant Green
                }
            }
            else
            {
                fanCapStr = "Fan: Auto";
                fanCapColor = Color.FromArgb(148, 163, 184); // Slate Gray
            }

            // Measure components
            Size szPl = TextRenderer.MeasureText(g, plStr, font, Size.Empty, TextFormatFlags.NoPadding);
            Size szSepSlash = TextRenderer.MeasureText(g, "/", font, Size.Empty, TextFormatFlags.NoPadding);
            Size szFan = TextRenderer.MeasureText(g, fanCapStr, font, Size.Empty, TextFormatFlags.NoPadding);
            Size szSepPipe = !string.IsNullOrEmpty(pwrStr)
                ? TextRenderer.MeasureText(g, "|", font, Size.Empty, TextFormatFlags.NoPadding)
                : Size.Empty;
            Size szPwr = !string.IsNullOrEmpty(pwrStr) 
                ? TextRenderer.MeasureText(g, pwrStr, font, Size.Empty, TextFormatFlags.NoPadding) 
                : Size.Empty;

            int gap = 5;
            int totalW = szPl.Width + gap + szSepSlash.Width + gap + szFan.Width;
            if (szPwr.Width > 0)
            {
                totalW += gap + szSepPipe.Width + gap + szPwr.Width;
            }

            int topY = 2;
            int textH = 18;
            int startX = w - 6 - totalW;
            if (startX < 6) startX = 6;

            // Draw translucent dark background pill for high contrast over graph lines
            using (var bgBrush = new SolidBrush(Color.FromArgb(140, 12, 12, 16)))
            {
                g.FillRectangle(bgBrush, startX - 4, topY - 1, totalW + 8, textH + 2);
            }

            int curX = startX;
            Color sepColor = Color.FromArgb(100, 100, 115);

            // 1. Power Limit
            TextRenderer.DrawText(g, plStr, font, new Rectangle(curX, topY, szPl.Width + 2, textH),
                plColor,
                TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
            curX += szPl.Width + gap;

            // 2. Separator /
            TextRenderer.DrawText(g, "/", font, new Rectangle(curX, topY, szSepSlash.Width + 2, textH),
                sepColor,
                TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
            curX += szSepSlash.Width + gap;

            // 3. Fan Cap
            TextRenderer.DrawText(g, fanCapStr, font, new Rectangle(curX, topY, szFan.Width + 2, textH),
                fanCapColor,
                TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
            curX += szFan.Width + gap;

            // 4. Power Consumption on the right (Yellow)
            if (!string.IsNullOrEmpty(pwrStr))
            {
                TextRenderer.DrawText(g, "|", font, new Rectangle(curX, topY, szSepPipe.Width + 2, textH),
                    sepColor,
                    TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
                curX += szSepPipe.Width + gap;

                TextRenderer.DrawText(g, pwrStr, font, new Rectangle(curX, topY, szPwr.Width + 4, textH),
                    Color.FromArgb(250, 204, 21),
                    TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
            }
        }

        private void DrawVramGraph(object? sender, PaintEventArgs e)
        {
            if (_settings.ShowAllGpus && _hardware.Gpu.GpuCount > 1)
            {
                DrawMultiGraphInternal(e.Graphics, _pnlVramGraph, _hardware.Gpu.Devices, true, MultiVramColors);
            }
            else
            {
                Color stroke = Color.FromArgb(236, 72, 153);
                Color fill = Color.FromArgb(70, 210, 50, 130);
                DrawGraphInternal(e.Graphics, _pnlVramGraph, _hardware.Gpu.VramHistory, stroke, fill);
            }
        }

        private void DrawDiskGraph(object? sender, PaintEventArgs e)
        {
            Color stroke = Color.FromArgb(6, 182, 212);
            Color fill = Color.FromArgb(60, 6, 182, 212);
            DrawGraphInternal(e.Graphics, _pnlDiskGraph, _hardware.Disk.History, stroke, fill);
        }

        private void DrawFansGraph(object? sender, PaintEventArgs e)
        {
            if (_settings.FanVisualMode == 1)
            {
                DrawFansIconsView(e.Graphics);
            }
            else if (_settings.FanVisualMode == 2)
            {
                DrawFansGridView(e.Graphics);
            }
            else
            {
                DrawFansLineGraph(e.Graphics);
            }
        }

        private void DrawFansLineGraph(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = _pnlFansGraph.Width;
            int h = _pnlFansGraph.Height;
            if (w <= 1 || h <= 1) return;

            // Grid lines (50%)
            using var gridPen = new Pen(Color.FromArgb(32, 32, 38), 1f);
            int midY = h / 2;
            g.DrawLine(gridPen, 0, midY, w, midY);

            var allFans = _hardware.Fans.Fans;
            var fans = allFans.Where(f => _settings.IsFanVisible(f.Id, f.Name)).ToList();
            if (fans.Count == 0 || !_hardware.Fans.IsConnected)
            {
                using var font = new Font("Segoe UI", 8.25f);
                using var brush = new SolidBrush(Color.FromArgb(120, 130, 145));
                string msg = _hardware.Fans.StatusMessage;
                if (_hardware.Fans.IsConnected && fans.Count == 0)
                {
                    msg = Loc.Get("FanPluginStatus_NoFans");
                }
                else if (string.IsNullOrEmpty(msg))
                {
                    msg = !_settings.EnableFanAddon ? Loc.Get("FanPluginStatus_Disabled") : Loc.Get("FanPluginStatus_NotInstalled");
                }
                var sz = g.MeasureString(msg, font);
                g.DrawString(msg, font, brush, (w - sz.Width) / 2, (h - sz.Height) / 2);
                return;
            }

            // Determine scale: max RPM among all fans, rounded up to next 500 RPM, minimum 1500
            int maxRpm = 1500;
            foreach (var f in fans)
            {
                if (f.MaxRpm > maxRpm) maxRpm = f.MaxRpm;
                if (f.CurrentRpm > maxRpm) maxRpm = ((f.CurrentRpm / 500) + 1) * 500;
            }

            // Draw lines for each fan (reverse order so fan #0 CPU fan is drawn on top)
            for (int d = fans.Count - 1; d >= 0; d--)
            {
                var f = fans[d];
                var history = f.History;
                if (history.Count < 2) continue;

                Color color = f.Color;
                var points = new PointF[history.Count];
                float stepX = (float)w / (history.Count - 1);

                for (int i = 0; i < history.Count; i++)
                {
                    float val = Math.Clamp(history[i], 0f, (float)maxRpm);
                    float x = i * stepX;
                    float y = h - (h * (val / maxRpm));
                    points[i] = new PointF(x, y);
                }

                if (d == 0)
                {
                    using var path = new GraphicsPath();
                    path.AddLines(points);
                    path.AddLine(w, h, 0, h);
                    path.CloseFigure();

                    Color topFill = Color.FromArgb(40, color.R, color.G, color.B);
                    Color botFill = Color.FromArgb(6, 16, 16, 20);
                    using var brush = new LinearGradientBrush(new Point(0, 0), new Point(0, h), topFill, botFill);
                    g.FillPath(brush, path);
                }

                using var linePen = new Pen(color, d == 0 ? 2.0f : 1.5f);
                if (d > 0)
                {
                    linePen.DashStyle = DashStyle.Dash;
                }
                g.DrawLines(linePen, points);
            }

            // Legend badges on top-right of graph
            using var legendFont = new Font("Segoe UI", 7f, FontStyle.Bold);
            int legX = w - 10;
            for (int d = fans.Count - 1; d >= 0; d--)
            {
                var f = fans[d];
                string shortName = f.Name.Replace(" Fan", "").Trim();
                if (shortName.Length > 8) shortName = shortName.Substring(0, 8);
                string label = $"{shortName}: {f.CurrentRpm}";
                var sz = g.MeasureString(label, legendFont);
                legX -= (int)sz.Width + 14;
                if (legX < 10) break; // Don't overflow into left margin

                using var brush = new SolidBrush(f.Color);
                g.FillRectangle(brush, legX, 4, 8, 8);
                using var textBrush = new SolidBrush(Color.FromArgb(200, 200, 215));
                g.DrawString(label, legendFont, textBrush, legX + 10, 2);
                legX -= 4;
            }
        }

        private void DrawFansIconsView(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int w = _pnlFansGraph.Width;
            int h = _pnlFansGraph.Height;
            if (w <= 1 || h <= 1) return;

            var allFans = _hardware.Fans.Fans;
            var fans = allFans.Where(f => _settings.IsFanVisible(f.Id, f.Name)).ToList();
            if (fans.Count == 0 || !_hardware.Fans.IsConnected)
            {
                using var font = new Font("Segoe UI", 8.25f);
                using var brush = new SolidBrush(Color.FromArgb(120, 130, 145));
                string msg = _hardware.Fans.StatusMessage;
                if (_hardware.Fans.IsConnected && fans.Count == 0)
                {
                    msg = Loc.Get("FanPluginStatus_NoFans");
                }
                else if (string.IsNullOrEmpty(msg))
                {
                    msg = !_settings.EnableFanAddon ? Loc.Get("FanPluginStatus_Disabled") : Loc.Get("FanPluginStatus_NotInstalled");
                }
                var sz = g.MeasureString(msg, font);
                g.DrawString(msg, font, brush, (w - sz.Width) / 2, (h - sz.Height) / 2);
                return;
            }

            float fanScale = _settings.FanScale;
            int cardW = (int)Math.Round(76 + 32 * (fanScale - 1f));
            int cardH = Math.Min(h - 4, (int)Math.Round(72 * fanScale));
            int gap = Math.Max(5, (int)Math.Round(6 * fanScale));
            int padX = 6;
            int totalW = padX + (fans.Count * cardW) + (Math.Max(0, fans.Count - 1) * gap) + padX;

            if (totalW > w)
            {
                _maxFanScroll = totalW - w;
            }
            else
            {
                _maxFanScroll = 0;
                _fanScrollX = 0;
            }
            _fanScrollX = Math.Clamp(_fanScrollX, 0, _maxFanScroll);

            var origClip = g.Clip;
            g.SetClip(new Rectangle(0, 0, w, h));

            int startX = padX - _fanScrollX;
            float fontSizeName = Math.Clamp(7.5f * (float)Math.Sqrt(fanScale), 7.5f, 11f);
            float fontSizeRpm = Math.Clamp(7f * (float)Math.Sqrt(fanScale), 7f, 10f);
            using var fontName = new Font("Segoe UI", fontSizeName, FontStyle.Bold);
            using var fontRpm = new Font("Segoe UI", fontSizeRpm, FontStyle.Regular);

            for (int i = 0; i < fans.Count; i++)
            {
                var f = fans[i];
                int cardX = startX + i * (cardW + gap);
                int cardY = (h - cardH) / 2;

                if (cardX + cardW < 0 || cardX > w) continue;

                int rpm = f.CurrentRpm;
                int bladeCount;
                Color tierColor;

                if (rpm <= 0)
                {
                    bladeCount = 3;
                    tierColor = Color.FromArgb(100, 110, 125); // muted slate
                }
                else if (rpm < 1200)
                {
                    bladeCount = 4; // мало лопастей - тихий режим
                    tierColor = Color.FromArgb(56, 189, 248); // Sky Blue
                }
                else if (rpm < 2200)
                {
                    bladeCount = 7; // много - крутится быстрее
                    tierColor = Color.FromArgb(249, 115, 22); // Amber / Orange
                }
                else
                {
                    bladeCount = 11; // очень много - максимальная скорость
                    tierColor = Color.FromArgb(239, 68, 68); // Coral Red / Turbo
                }

                // Card background & subtle border
                var cardRect = new Rectangle(cardX, cardY, cardW, cardH);
                using (var cardBrush = new SolidBrush(Color.FromArgb(24, 25, 32)))
                {
                    g.FillRectangle(cardBrush, cardRect);
                }
                using (var borderPen = new Pen(Color.FromArgb(42, 44, 56), 1f))
                {
                    g.DrawRectangle(borderPen, cardRect);
                }

                // Accent top line if running
                if (rpm > 0)
                {
                    using var accentPen = new Pen(Color.FromArgb(180, tierColor.R, tierColor.G, tierColor.B), Math.Max(2f, 2f * fanScale));
                    g.DrawLine(accentPen, cardX + 4, cardY + 1, cardX + cardW - 4, cardY + 1);
                }

                // Fan geometry
                float cx = cardX + (cardW / 2f);
                float fanRadius = Math.Clamp((cardH - 30f) * 0.46f, 16f, 36f);
                float cy = cardY + 4f + fanRadius;

                // Outer shroud
                using (var shroudBg = new SolidBrush(Color.FromArgb(18, 19, 24)))
                {
                    g.FillEllipse(shroudBg, cx - fanRadius, cy - fanRadius, fanRadius * 2, fanRadius * 2);
                }
                using (var shroudPen = new Pen(Color.FromArgb(46, 48, 60), Math.Max(1f, 1f * fanScale)))
                {
                    g.DrawEllipse(shroudPen, cx - fanRadius, cy - fanRadius, fanRadius * 2, fanRadius * 2);
                }

                // Rotating blades
                _fanAngles.TryGetValue(f.Id, out float curAngle);
                DrawFanBlades(g, cx, cy, fanRadius, bladeCount, curAngle, tierColor, rpm > 0, fanScale);

                // Central hub cap
                float hubR = 5f * fanScale;
                using (var hubBrush = new SolidBrush(Color.FromArgb(32, 34, 44)))
                {
                    g.FillEllipse(hubBrush, cx - hubR, cy - hubR, hubR * 2, hubR * 2);
                }
                using (var hubBorder = new Pen(Color.FromArgb(60, 64, 80), Math.Max(1f, 1f * fanScale)))
                {
                    g.DrawEllipse(hubBorder, cx - hubR, cy - hubR, hubR * 2, hubR * 2);
                }
                float dotR = 1.5f * fanScale;
                using (var dotBrush = new SolidBrush(tierColor))
                {
                    g.FillEllipse(dotBrush, cx - dotR, cy - dotR, dotR * 2, dotR * 2);
                }

                // Legend: Fan Name
                string dispName = FormatFanName(f.Name);
                var szName = g.MeasureString(dispName, fontName);
                float textNameY = cy + fanRadius + 3f * fanScale;
                using (var nameBrush = new SolidBrush(Color.FromArgb(235, 235, 245)))
                {
                    g.DrawString(dispName, fontName, nameBrush, cx - (szName.Width / 2f), textNameY);
                }

                // Legend: RPM
                string rpmText = rpm > 0 ? $"{rpm:N0} RPM" : "0 RPM (0dB)";
                var szRpm = g.MeasureString(rpmText, fontRpm);
                float textRpmY = textNameY + szName.Height + 1f;
                using (var rpmBrush = new SolidBrush(tierColor))
                {
                    g.DrawString(rpmText, fontRpm, rpmBrush, cx - (szRpm.Width / 2f), textRpmY);
                }
            }

            g.Clip = origClip;

            // Horizontal scroll navigation arrows (overlaid on edges)
            if (_maxFanScroll > 0)
            {
                using var arrowFont = new Font("Segoe UI", 9f, FontStyle.Bold);

                // Left arrow
                if (_fanScrollX > 0)
                {
                    using var leftBg = new LinearGradientBrush(
                        new Rectangle(0, 0, 26, h),
                        Color.FromArgb(220, 16, 16, 20),
                        Color.FromArgb(0, 16, 16, 20),
                        LinearGradientMode.Horizontal);
                    g.FillRectangle(leftBg, 0, 0, 26, h);

                    using var arrowBrush = new SolidBrush(Color.FromArgb(56, 189, 248));
                    g.DrawString("◀", arrowFont, arrowBrush, 2, (h - 18) / 2);
                }

                // Right arrow
                if (_fanScrollX < _maxFanScroll)
                {
                    using var rightBg = new LinearGradientBrush(
                        new Rectangle(w - 26, 0, 26, h),
                        Color.FromArgb(0, 16, 16, 20),
                        Color.FromArgb(220, 16, 16, 20),
                        LinearGradientMode.Horizontal);
                    g.FillRectangle(rightBg, w - 26, 0, 26, h);

                    using var arrowBrush = new SolidBrush(Color.FromArgb(56, 189, 248));
                    g.DrawString("▶", arrowFont, arrowBrush, w - 16, (h - 18) / 2);
                }
            }
        }

        private void DrawFansGridView(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int w = _pnlFansGraph.Width;
            int h = _pnlFansGraph.Height;
            if (w <= 1 || h <= 1) return;

            var allFans = _hardware.Fans.Fans;
            var fans = allFans.Where(f => _settings.IsFanVisible(f.Id, f.Name)).ToList();
            if (fans.Count == 0 || !_hardware.Fans.IsConnected)
            {
                using var font = new Font("Segoe UI", 8.25f);
                using var brush = new SolidBrush(Color.FromArgb(120, 130, 145));
                string msg = _hardware.Fans.StatusMessage;
                if (_hardware.Fans.IsConnected && fans.Count == 0)
                {
                    msg = Loc.Get("FanPluginStatus_NoFans");
                }
                else if (string.IsNullOrEmpty(msg))
                {
                    msg = !_settings.EnableFanAddon ? Loc.Get("FanPluginStatus_Disabled") : Loc.Get("FanPluginStatus_NotInstalled");
                }
                var sz = g.MeasureString(msg, font);
                g.DrawString(msg, font, brush, (w - sz.Width) / 2, (h - sz.Height) / 2);
                return;
            }

            float fanScale = _settings.FanScale;
            int cols;
            if (fanScale >= 1.40f)
            {
                cols = Math.Max(2, (w - 4) / (int)(95 * fanScale));
                if (w < 460) cols = 2;
            }
            else
            {
                cols = Math.Max(2, (w - 4) / 115);
                if (w < 460) cols = 3;
                if (w < 320) cols = 2;
            }

            int rows = (int)Math.Ceiling((double)fans.Count / cols);
            if (rows < 1) rows = 1;

            int gapX = 5;
            int gapY = 5;
            int padX = 2;
            int cardW = (w - (padX * 2) - (gapX * (cols - 1))) / cols;
            bool isWideCard = (cardW >= 140);
            int cardH = isWideCard 
                ? (int)Math.Round(48 + 20 * fanScale) 
                : (int)Math.Round(42 + 10 * fanScale);

            float fontSizeName = isWideCard 
                ? Math.Clamp(8.25f + 1.25f * (fanScale - 1f), 8.0f, 9.5f)
                : 7.25f;
            float fontSizeRpm = isWideCard
                ? Math.Clamp(7.75f + 1.25f * (fanScale - 1f), 7.5f, 9.0f)
                : 6.75f;
            using var fontName = new Font("Segoe UI", fontSizeName, FontStyle.Bold);
            using var fontRpm = new Font("Segoe UI", fontSizeRpm, FontStyle.Bold);

            for (int i = 0; i < fans.Count; i++)
            {
                var f = fans[i];
                int col = i % cols;
                int row = i / cols;

                int cardX = padX + col * (cardW + gapX);
                int cardY = 4 + row * (cardH + gapY);

                if (cardY + cardH > h + 20) break;

                int rpm = f.CurrentRpm;
                int bladeCount;
                Color tierColor;

                if (rpm <= 0)
                {
                    bladeCount = 3;
                    tierColor = Color.FromArgb(100, 110, 125); // Muted slate
                }
                else if (rpm < 1200)
                {
                    bladeCount = 4; // Sky blue
                    tierColor = Color.FromArgb(56, 189, 248);
                }
                else if (rpm < 2200)
                {
                    bladeCount = 7; // Amber orange
                    tierColor = Color.FromArgb(249, 115, 22);
                }
                else
                {
                    bladeCount = 11; // Coral red
                    tierColor = Color.FromArgb(239, 68, 68);
                }

                // Card background & border
                var cardRect = new Rectangle(cardX, cardY, cardW, cardH);
                using (var cardBrush = new SolidBrush(Color.FromArgb(24, 25, 32)))
                {
                    g.FillRectangle(cardBrush, cardRect);
                }
                using (var borderPen = new Pen(Color.FromArgb(42, 44, 56), 1f))
                {
                    g.DrawRectangle(borderPen, cardRect);
                }

                // Accent top stripe when spinning
                if (rpm > 0)
                {
                    using var accentPen = new Pen(Color.FromArgb(180, tierColor.R, tierColor.G, tierColor.B), Math.Max(2f, 2f * fanScale));
                    g.DrawLine(accentPen, cardX + 3, cardY + 1, cardX + cardW - 3, cardY + 1);
                }

                _fanAngles.TryGetValue(f.Id, out float curAngle);
                string dispName = FormatFanName(f.Name);
                string rpmText = rpm > 0 ? $"{rpm:N0} RPM" : "0 RPM";

                string subInfo = f.Name?.Trim() ?? "";
                if (string.Equals(subInfo, dispName, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(f.Hardware))
                {
                    subInfo = f.Hardware.Trim();
                }

                // SIDE-BY-SIDE LAYOUT FOR ALL SCALES:
                // Animated spinner on the left, fan name + RPM text on the right
                float fanRadius = isWideCard 
                    ? Math.Clamp((cardH - 18f) / 2f, 20f, 34f)
                    : Math.Clamp((cardH - 14f) / 2f, 15f, 20f);

                float padLeft = isWideCard ? Math.Clamp(8f * fanScale, 8f, 14f) : 6f;
                float cx = cardX + padLeft + fanRadius;
                float cy = cardY + (cardH / 2f);

                // Outer shroud
                using (var shroudBg = new SolidBrush(Color.FromArgb(18, 19, 24)))
                {
                    g.FillEllipse(shroudBg, cx - fanRadius, cy - fanRadius, fanRadius * 2, fanRadius * 2);
                }
                using (var shroudPen = new Pen(Color.FromArgb(46, 48, 60), Math.Max(1.1f, 1.1f * fanScale)))
                {
                    g.DrawEllipse(shroudPen, cx - fanRadius, cy - fanRadius, fanRadius * 2, fanRadius * 2);
                }

                // Rotating blades
                DrawFanBlades(g, cx, cy, fanRadius, bladeCount, curAngle, tierColor, rpm > 0, fanScale);

                // Central hub cap
                float hubR = fanRadius * 0.30f;
                using (var hubBrush = new SolidBrush(Color.FromArgb(32, 34, 44)))
                {
                    g.FillEllipse(hubBrush, cx - hubR, cy - hubR, hubR * 2, hubR * 2);
                }
                using (var hubBorder = new Pen(Color.FromArgb(60, 64, 80), Math.Max(1f, 1f * fanScale)))
                {
                    g.DrawEllipse(hubBorder, cx - hubR, cy - hubR, hubR * 2, hubR * 2);
                }
                float dotR = Math.Max(1.5f, hubR * 0.35f);
                using (var dotBrush = new SolidBrush(tierColor))
                {
                    g.FillEllipse(dotBrush, cx - dotR, cy - dotR, dotR * 2, dotR * 2);
                }

                // Text block on the right
                float gapSpinnerToText = isWideCard ? Math.Clamp(8f * fanScale, 8f, 12f) : 6f;
                float textLeft = cx + fanRadius + gapSpinnerToText;
                float availTextW = Math.Max(30f, (cardX + cardW - 3f) - textLeft);

                using var sf = new StringFormat
                {
                    Trimming = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.NoWrap
                };

                bool showSubInfo = isWideCard && fanScale >= 1.35f && !string.IsNullOrWhiteSpace(subInfo);

                if (showSubInfo)
                {
                    // 3-line layout for large magnification: Name (bold) + Sensor breakdown (small) + RPM (bold)
                    float fontSizeSub = Math.Clamp(fontSizeName - 1.75f, 6.5f, 7.5f);
                    using var fontSub = new Font("Segoe UI", fontSizeSub, FontStyle.Regular);

                    var szName = g.MeasureString(dispName, fontName);
                    var szSub = g.MeasureString(subInfo, fontSub);
                    var szRpm = g.MeasureString(rpmText, fontRpm);

                    float spacing1 = Math.Clamp(2f * fanScale, 2f, 3.5f);
                    float spacing2 = Math.Clamp(3.5f * fanScale, 3f, 5.5f);

                    float totalTextH = szName.Height + spacing1 + szSub.Height + spacing2 + szRpm.Height;
                    float textTopY = cy - (totalTextH / 2f);

                    var rectName = new RectangleF(textLeft, textTopY, availTextW, szName.Height + 2);
                    using (var nameBrush = new SolidBrush(Color.FromArgb(235, 235, 245)))
                    {
                        g.DrawString(dispName, fontName, nameBrush, rectName, sf);
                    }

                    float subY = textTopY + szName.Height + spacing1;
                    var rectSub = new RectangleF(textLeft, subY, availTextW, szSub.Height + 2);
                    using (var subBrush = new SolidBrush(Color.FromArgb(140, 148, 168)))
                    {
                        g.DrawString(subInfo, fontSub, subBrush, rectSub, sf);
                    }

                    float rpmY = subY + szSub.Height + spacing2;
                    var rectRpm = new RectangleF(textLeft, rpmY, availTextW, szRpm.Height + 2);
                    using (var rpmBrush = new SolidBrush(tierColor))
                    {
                        g.DrawString(rpmText, fontRpm, rpmBrush, rectRpm, sf);
                    }
                }
                else
                {
                    // 2-line layout for compact scale (3 columns or small magnification)
                    float lineSpacing = isWideCard
                        ? Math.Clamp(5f * fanScale, 4f, 9f)
                        : 3f;

                    var szName = g.MeasureString(dispName, fontName);
                    var szRpm = g.MeasureString(rpmText, fontRpm);

                    float totalTextH = szName.Height + lineSpacing + szRpm.Height;
                    float textTopY = cy - (totalTextH / 2f);

                    var rectName = new RectangleF(textLeft, textTopY, availTextW, szName.Height + 2);
                    using (var nameBrush = new SolidBrush(Color.FromArgb(235, 235, 245)))
                    {
                        g.DrawString(dispName, fontName, nameBrush, rectName, sf);
                    }

                    var rectRpm = new RectangleF(textLeft, textTopY + szName.Height + lineSpacing, availTextW, szRpm.Height + 2);
                    using (var rpmBrush = new SolidBrush(tierColor))
                    {
                        g.DrawString(rpmText, fontRpm, rpmBrush, rectRpm, sf);
                    }
                }
            }
        }

        private static void DrawFanBlades(Graphics g, float cx, float cy, float fanRadius, int bladeCount, float angle, Color tierColor, bool isRunning, float fanScale = 1f)
        {
            float r0 = Math.Max(2.5f * fanScale, fanRadius * 0.28f);
            float r1 = fanRadius - (1.5f * fanScale);
            float wHub = Math.Clamp(180f / bladeCount, 12f, 24f);
            float wTip = Math.Clamp(240f / bladeCount, 16f, 32f);
            float curve = 14f;

            Color fillCol = isRunning
                ? Color.FromArgb(195, tierColor.R, tierColor.G, tierColor.B)
                : Color.FromArgb(100, tierColor.R, tierColor.G, tierColor.B);
            using var bladeBrush = new SolidBrush(fillCol);
            using var bladePen = new Pen(Color.FromArgb(isRunning ? 230 : 120, tierColor.R, tierColor.G, tierColor.B), Math.Max(0.8f, 0.8f * fanScale));

            for (int b = 0; b < bladeCount; b++)
            {
                float baseAngle = angle + (b * 360f / bladeCount);

                float rad0 = (baseAngle - wHub / 2f) * (MathF.PI / 180f);
                float rad1 = (baseAngle + curve - wTip / 2f) * (MathF.PI / 180f);
                float rad2 = (baseAngle + curve + wTip / 2f) * (MathF.PI / 180f);
                float rad3 = (baseAngle + wHub / 2f) * (MathF.PI / 180f);

                var pts = new PointF[]
                {
                    new(cx + r0 * MathF.Cos(rad0), cy + r0 * MathF.Sin(rad0)),
                    new(cx + r1 * MathF.Cos(rad1), cy + r1 * MathF.Sin(rad1)),
                    new(cx + r1 * MathF.Cos(rad2), cy + r1 * MathF.Sin(rad2)),
                    new(cx + r0 * MathF.Cos(rad3), cy + r0 * MathF.Sin(rad3))
                };

                g.FillPolygon(bladeBrush, pts);
                g.DrawPolygon(bladePen, pts);
            }
        }

        public static string FormatFanName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return "FAN";
            string s = rawName.Trim();

            // Match GPU Fan 1, GPU 1, GPU #1, GPU Fan #2
            if (s.StartsWith("GPU", StringComparison.OrdinalIgnoreCase))
            {
                var match = System.Text.RegularExpressions.Regex.Match(s, @"GPU\s*(?:Fan)?\s*#?(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success) return $"GPU {match.Groups[1].Value}";
                return "GPU";
            }

            // Match CPU Fan 1, CPU Fan, CPU 2, CPU OPT
            if (s.StartsWith("CPU", StringComparison.OrdinalIgnoreCase))
            {
                var match = System.Text.RegularExpressions.Regex.Match(s, @"CPU\s*(?:Fan)?\s*#?(\d+|OPT)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success) return $"CPU {match.Groups[1].Value}";
                return "CPU";
            }

            if (s.StartsWith("Pump", StringComparison.OrdinalIgnoreCase))
            {
                var match = System.Text.RegularExpressions.Regex.Match(s, @"Pump\s*(?:Fan)?\s*#?(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success) return $"PUMP {match.Groups[1].Value}";
                return "PUMP";
            }

            if (s.StartsWith("System", StringComparison.OrdinalIgnoreCase) || s.StartsWith("SYS", StringComparison.OrdinalIgnoreCase))
            {
                var match = System.Text.RegularExpressions.Regex.Match(s, @"(?:System|SYS)\s*(?:Fan)?\s*#?(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success) return $"SYS {match.Groups[1].Value}";
                return "SYS";
            }

            if (s.Contains("Chassis #", StringComparison.OrdinalIgnoreCase))
            {
                int idx = s.IndexOf("Chassis #", StringComparison.OrdinalIgnoreCase);
                return "FAN " + s.Substring(idx + 9).Trim();
            }
            if (s.Contains("Fan #", StringComparison.OrdinalIgnoreCase))
            {
                int idx = s.IndexOf("Fan #", StringComparison.OrdinalIgnoreCase);
                return "FAN " + s.Substring(idx + 5).Trim();
            }
            if (s.EndsWith(" Fan", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(0, s.Length - 4).Trim();
            }
            return s.Length > 9 ? s.Substring(0, 9) : s;
        }

        private void UpdateFanAnimation()
        {
            var allFans = _hardware.Fans.Fans;
            var fans = allFans.Where(f => _settings.IsFanVisible(f.Id, f.Name)).ToList();
            if (fans.Count == 0) return;

            bool anySpinning = false;
            foreach (var fan in fans)
            {
                if (fan.CurrentRpm > 0)
                {
                    anySpinning = true;
                    // Rotation speed scales with RPM:
                    // ~3 deg per frame for quiet mode, up to ~22 deg per frame for turbo
                    float speed = Math.Clamp(fan.CurrentRpm / 140f, 2.5f, 22f);
                    if (!_fanAngles.TryGetValue(fan.Id, out float curAngle))
                    {
                        curAngle = 0;
                    }
                    _fanAngles[fan.Id] = (curAngle + speed) % 360f;
                }
            }

            if (anySpinning)
            {
                _pnlFansGraph.Invalidate();
            }
        }

        private void OnFansPanelMouseWheel(object? sender, MouseEventArgs e)
        {
            if (_settings.FanVisualMode != 1 || _maxFanScroll <= 0) return;
            int step = (int)Math.Round(45 * _settings.FanScale);
            _fanScrollX = Math.Clamp(_fanScrollX - Math.Sign(e.Delta) * step, 0, _maxFanScroll);
            _pnlFansGraph.Invalidate();
        }

        private void OnFansPanelMouseDown(object? sender, MouseEventArgs e)
        {
            if (_settings.FanVisualMode != 1 || _maxFanScroll <= 0) return;
            if (e.Button == MouseButtons.Left)
            {
                _isFanDragging = true;
                _fanDragStartX = e.X;
                _fanScrollStartX = _fanScrollX;
            }
        }

        private void OnFansPanelMouseMove(object? sender, MouseEventArgs e)
        {
            if (_settings.FanVisualMode != 1) return;
            if (_isFanDragging)
            {
                int delta = e.X - _fanDragStartX;
                _fanScrollX = Math.Clamp(_fanScrollStartX - delta, 0, _maxFanScroll);
                _pnlFansGraph.Invalidate();
            }
        }

        private void OnFansPanelMouseUp(object? sender, MouseEventArgs e)
        {
            _isFanDragging = false;
        }

        private void OnFansPanelMouseClick(object? sender, MouseEventArgs e)
        {
            if (_settings.FanVisualMode != 1 || _maxFanScroll <= 0) return;

            int w = _pnlFansGraph.Width;
            int step = (int)Math.Round(92 * _settings.FanScale);
            if (_fanScrollX > 0 && e.X <= 26)
            {
                _fanScrollX = Math.Max(0, _fanScrollX - step);
                _pnlFansGraph.Invalidate();
            }
            else if (_fanScrollX < _maxFanScroll && e.X >= w - 26)
            {
                _fanScrollX = Math.Min(_maxFanScroll, _fanScrollX + step);
                _pnlFansGraph.Invalidate();
            }
        }

        private void DrawMultiGraphInternal(Graphics g, Panel pnl, IReadOnlyList<GpuDeviceInfo> devices, bool isVram, Color[] colors)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = pnl.Width;
            int h = pnl.Height;
            if (w <= 1 || h <= 1) return;

            // Grid lines (50%)
            using var gridPen = new Pen(Color.FromArgb(32, 32, 38), 1f);
            int midY = h / 2;
            g.DrawLine(gridPen, 0, midY, w, midY);

            if (devices.Count == 0) return;

            // Draw lines for each device in reverse so GPU #0 is rendered on top
            for (int d = devices.Count - 1; d >= 0; d--)
            {
                var dev = devices[d];
                var history = isVram ? dev.VramHistory : dev.GpuHistory;
                if (history.Count < 2) continue;

                Color color = colors[d % colors.Length];
                var points = new PointF[history.Count];
                float stepX = (float)w / (history.Count - 1);

                for (int i = 0; i < history.Count; i++)
                {
                    float val = Math.Clamp(history[i], 0f, 100f);
                    float x = i * stepX;
                    float y = h - (h * (val / 100f));
                    points[i] = new PointF(x, y);
                }

                if (d == 0)
                {
                    using var path = new GraphicsPath();
                    path.AddLines(points);
                    path.AddLine(w, h, 0, h);
                    path.CloseFigure();

                    Color topFill = Color.FromArgb(50, color.R, color.G, color.B);
                    Color botFill = Color.FromArgb(6, 16, 16, 20);
                    using var brush = new LinearGradientBrush(new Point(0, 0), new Point(0, h), topFill, botFill);
                    g.FillPath(brush, path);
                }

                using var linePen = new Pen(color, d == 0 ? 2.0f : 1.6f);
                if (d > 0)
                {
                    linePen.DashStyle = DashStyle.Dash;
                }
                g.DrawLines(linePen, points);
            }

            // Legend badges on top-right of graph
            if (devices.Count > 1)
            {
                using var font = new Font("Segoe UI", 7f, FontStyle.Bold);
                int legX = w - 10;
                for (int d = devices.Count - 1; d >= 0; d--)
                {
                    string label = $"#{d}";
                    var sz = g.MeasureString(label, font);
                    legX -= (int)sz.Width + 14;
                    Color color = colors[d % colors.Length];
                    using var brush = new SolidBrush(color);
                    g.FillRectangle(brush, legX, 4, 8, 8);
                    using var textBrush = new SolidBrush(Color.FromArgb(200, 200, 210));
                    g.DrawString(label, font, textBrush, legX + 10, 2);
                    legX -= 4;
                }
            }

            if (!isVram && devices.Count > 0 && _settings.ShowGpuTempLine)
            {
                DrawTemperatureCurve(g, pnl, devices[0].TempHistory);
            }
        }

        private void DrawTemperatureCurve(Graphics g, Panel pnl, IReadOnlyList<float> tempHistory)
        {
            if (tempHistory.Count < 2) return;

            bool hasData = false;
            for (int i = 0; i < tempHistory.Count; i++)
            {
                if (tempHistory[i] > 0f) { hasData = true; break; }
            }
            if (!hasData) return;

            int w = pnl.Width;
            int h = pnl.Height;
            if (w <= 1 || h <= 1) return;

            var points = new PointF[tempHistory.Count];
            float stepX = (float)w / (tempHistory.Count - 1);

            for (int i = 0; i < tempHistory.Count; i++)
            {
                float val = Math.Clamp(tempHistory[i], 0f, 100f);
                float x = i * stepX;
                float y = h - (h * (val / 100f));
                points[i] = new PointF(x, y);
            }

            using var tempPen = new Pen(GpuTempColor, 1.4f);
            tempPen.DashPattern = new float[] { 3.5f, 2.5f };
            g.DrawLines(tempPen, points);
        }

        private void DrawCoresMatrix(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            int count = _hardware.Cpu.CoreCount;
            if (count <= 0) return;

            int cols = count <= 16 ? 8 : 16;
            int rows = (int)Math.Ceiling((double)count / cols);

            int pW = _pnlCoresMatrix.Width;
            int pH = _pnlCoresMatrix.Height;

            int cellW = (pW - (cols + 1) * 2) / cols;
            int cellH = (pH - (rows + 1) * 2) / rows;

            for (int i = 0; i < count; i++)
            {
                int r = i / cols;
                int c = i % cols;

                int x = 2 + c * (cellW + 2);
                int y = 2 + r * (cellH + 2);

                float load = (i < _hardware.Cpu.CoreUsages.Length) ? _hardware.Cpu.CoreUsages[i] : 0f;
                float norm = Math.Clamp(load / 100f, 0f, 1f);

                Color cellColor;
                if (_isQuietMode)
                {
                    int green = (int)(50 + norm * 180);
                    int blue = (int)(40 + norm * 100);
                    cellColor = Color.FromArgb(30, green, blue);
                }
                else
                {
                    int red = (int)(60 + norm * 190);
                    int green = (int)(40 + norm * 140);
                    cellColor = Color.FromArgb(red, green, 30);
                }

                using var brush = new SolidBrush(cellColor);
                g.FillRectangle(brush, x, y, cellW, cellH);

                using var borderPen = new Pen(Color.FromArgb(40, 40, 48), 1f);
                g.DrawRectangle(borderPen, x, y, cellW, cellH);
            }
        }

        private void DrawTopProcesses(object? sender, PaintEventArgs e)
        {
            try
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                int w = _pnlTopProcesses.Width;
                int h = _pnlTopProcesses.Height;
                if (w <= 10 || h <= 10) return;

                // Draw border
                using (var borderPen = new Pen(Color.FromArgb(32, 32, 38), 1f))
                {
                    g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);
                }

                var list = _hardware.Processes.TopProcesses;
                if (list.Count == 0)
                {
                    using var brush = new SolidBrush(Color.FromArgb(140, 140, 150));
                    using var font = new Font("Segoe UI", 8.25f, FontStyle.Italic);
                    g.DrawString("Monitoring top CPU processes...", font, brush, 8, (h - 16) / 2);
                    return;
                }

                int rowH = 18;
                Color cpuCol = _isQuietMode ? Color.FromArgb(80, 220, 140) : Color.FromArgb(250, 180, 80);
                using var fontName = new Font("Segoe UI", 8.25f);
                using var fontCpu = new Font("Segoe UI", 8.25f, FontStyle.Bold);
                using var fontRam = new Font("Segoe UI", 8.25f);

                using var brushName = new SolidBrush(Color.FromArgb(220, 220, 230));
                using var brushCpu = new SolidBrush(cpuCol);
                using var brushRam = new SolidBrush(Color.FromArgb(56, 189, 248)); // RAM Cyan
                using var rowDividerPen = new Pen(Color.FromArgb(26, 26, 32), 1f);

                int count = Math.Min(5, list.Count);
                for (int i = 0; i < count; i++)
                {
                    var proc = list[i];
                    int y = i * rowH;

                    // Subtle CPU load bar in background
                    if (proc.CpuPercent > 0.1)
                    {
                        float barW = (float)(Math.Min(100.0, proc.CpuPercent) / 100.0) * (w - 2);
                        using var barBrush = new SolidBrush(Color.FromArgb(28, cpuCol.R, cpuCol.G, cpuCol.B));
                        g.FillRectangle(barBrush, 1, y, barW, rowH - 1);
                    }

                    // Icon
                    if (proc.Icon != null)
                    {
                        g.DrawImage(proc.Icon, 4, y + 1, 16, 16);
                    }
                    else
                    {
                        using var dotBrush = new SolidBrush(Color.FromArgb(70, 70, 85));
                        g.FillEllipse(dotBrush, 9, y + 6, 5, 5);
                    }

                    // RAM value right-aligned at right edge
                    int ramW = 62;
                    int ramX = w - ramW - 6;
                    var ramRect = new Rectangle(ramX, y, ramW, rowH);
                    var ramFormat = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
                    g.DrawString(proc.RamFormatted, fontRam, brushRam, ramRect, ramFormat);

                    // CPU % right-aligned before RAM
                    int cpuW = 52;
                    int cpuX = ramX - cpuW - 4;
                    var cpuRect = new Rectangle(cpuX, y, cpuW, rowH);
                    var cpuFormat = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
                    g.DrawString($"{proc.CpuPercent:F1}%", fontCpu, brushCpu, cpuRect, cpuFormat);

                    // Process Name with ellipsis
                    int nameX = 24;
                    int nameW = Math.Max(40, cpuX - nameX - 4);
                    var nameRect = new RectangleF(nameX, y, nameW, rowH);
                    var nameFormat = new StringFormat
                    {
                        Alignment = StringAlignment.Near,
                        LineAlignment = StringAlignment.Center,
                        Trimming = StringTrimming.EllipsisCharacter,
                        FormatFlags = StringFormatFlags.NoWrap
                    };
                    g.DrawString(proc.FriendlyName, fontName, brushName, nameRect, nameFormat);

                    // Divider line between rows
                    if (i < count - 1)
                    {
                        g.DrawLine(rowDividerPen, 4, y + rowH - 1, w - 4, y + rowH - 1);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"DrawTopProcesses error: {ex.Message}");
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var borderPen = new Pen(Color.FromArgb(50, 50, 60), 1f);
            e.Graphics.DrawRectangle(borderPen, 0, 0, this.Width - 1, this.Height - 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Loc.LanguageChanged -= OnLanguageChanged;
                _uiRefreshTimer?.Dispose();
                _fanAnimTimer?.Dispose();
                _infoForm?.Dispose();
                _settingsForm?.Dispose();
                _toolTip?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
