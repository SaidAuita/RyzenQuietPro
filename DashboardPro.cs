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

        private float _currentUiScale = 1.0f;
        public float CurrentUiScale => _currentUiScale;

        public float GetEffectiveUiScale(Screen? screen = null)
        {
            screen ??= (this.IsHandleCreated ? Screen.FromControl(this) : null) ?? Screen.PrimaryScreen;
            return _settings.GetEffectiveUiScale(screen);
        }

        // UI Header
        private Panel _headerPanel = null!;
        private PictureBox _picAppIcon = null!;
        private Label _lblTitle = null!;
        private Label _btnInfo = null!;
        private Label _btnPin = null!;
        private Label _btnDetach = null!;
        private Label _btnClose = null!;

        // Module Separators
        private static readonly Color StopwatchAccentColor = Color.FromArgb(245, 158, 11); // Amber/Gold
        private ModuleSeparator _sepStopwatch = null!;
        private Label _lblStopwatch = null!;
        private Label _lblStopwatchSub = null!;
        private SmoothPanel _pnlStopwatch = null!;
        private Button _btnSwDashboardAdd = null!;
        private class StopwatchRowControls
        {
            public StopwatchItem Item { get; set; } = null!;
            public Button BtnStart { get; set; } = null!;
            public Button BtnAuto { get; set; } = null!;
            public Button BtnStop { get; set; } = null!;
            public Button BtnReset { get; set; } = null!;
            public Button BtnScale { get; set; } = null!;
        }
        private readonly List<StopwatchRowControls> _stopwatchRowControls = new();
        private System.Windows.Forms.Timer? _stopwatchTimer;


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
        private Label _btnCpuProcessesExpand = null!;
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
        private Label _lblHddStatus = null!;
        private Label _btnDiskExpand = null!;
        private DiskLegendPanel _pnlDiskLegend = null!;
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

            _hardware.Processes.IsEnabled = _settings.ShowTopProcesses || (_settings.ShowDiskGraph && _settings.EnhancedDiskMode);
            _hardware.Fans.DemoMode = _settings.EnableFanDemo;
            _hardware.Fans.SetEnabled(_settings.EnableFanAddon);
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

            // Do not hide window on focus loss - remains visible until explicitly closed
            // this.Deactivate += OnWindowDeactivated;
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

            _hardware.Stopwatch.StateChanged += () => {
                if (this.IsHandleCreated && !this.IsDisposed)
                {
                    try { this.BeginInvoke((Action)UpdateStopwatchUI); } catch { }
                }
            };

            _stopwatchTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _stopwatchTimer.Tick += (s, e) => {
                if (this.Visible && _settings.ShowStopwatch)
                {
                    if (_hardware.Stopwatch.Items.Any(i => i.IsRunning))
                    {
                        _pnlStopwatch.Invalidate();
                    }
                    UpdateStopwatchSubHeader();
                }
            };
            _stopwatchTimer.Start();
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
            float scale = _currentUiScale;
            int cols;
            if (contentW < (int)Math.Round(380 * scale))
            {
                cols = 2;
            }
            else if (contentW < (int)Math.Round(560 * scale))
            {
                cols = 3;
            }
            else
            {
                cols = Math.Max(2, (contentW - 4) / (int)Math.Round(120 * scale));
            }

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

        public int CalculateDefaultHeight(float scale)
        {
            bool separate = _settings.SeparateCpuGpuControl;
            int headerH = (int)Math.Round(36 * scale);
            int footerH = (int)Math.Round((separate ? 78 : 44) * scale);
            int baseGraphH = (int)Math.Round(50 * scale);

            int totalH = headerH + footerH + (int)Math.Round(8 * scale);

            var visibleSw = _hardware.Stopwatch.Items.Where(i => i.Profile.IsVisible).ToList();
            int swCount = visibleSw.Count;
            if (_settings.ShowStopwatch)
            {
                if (!_settings.SectionStopwatchCollapsed && swCount > 0)
                {
                    int swRowH = (int)Math.Round(50 * scale);
                    int swGap = (int)Math.Round(4 * scale);
                    int swCardH = (swCount * swRowH) + ((swCount - 1) * swGap);
                    totalH += (int)Math.Round(28 * scale) + swCardH + (int)Math.Round(6 * scale);
                }
                else
                {
                    totalH += (int)Math.Round(28 * scale);
                }
            }

            bool showCpu = _settings.ShowCpuGraph || _settings.ShowCpuCores || _settings.ShowTopProcesses;
            if (showCpu)
            {
                totalH += (int)Math.Round(28 * scale);
                if (!_settings.SectionCpuCollapsed)
                {
                    if (_settings.ShowCpuGraph) totalH += baseGraphH + (int)Math.Round(2 * scale);
                    if (_settings.ShowCpuCores) totalH += (int)Math.Round(26 * scale);
                    if (_settings.ShowTopProcesses) totalH += (int)Math.Round(96 * scale);
                }
            }

            if (_settings.ShowRamGraph)
            {
                totalH += (int)Math.Round(28 * scale);
                if (!_settings.SectionRamCollapsed) totalH += baseGraphH + (int)Math.Round(6 * scale);
            }

            if (_settings.ShowGpuGraph)
            {
                totalH += (int)Math.Round(28 * scale);
                if (!_settings.SectionGpuCollapsed) totalH += baseGraphH + (int)Math.Round(6 * scale);
            }

            if (_settings.ShowVramGraph)
            {
                totalH += (int)Math.Round(28 * scale);
                if (!_settings.SectionVramCollapsed) totalH += baseGraphH + (int)Math.Round(6 * scale);
            }

            if (_settings.ShowDiskGraph)
            {
                totalH += (int)Math.Round(28 * scale);
                if (!_settings.SectionDiskCollapsed)
                {
                    totalH += baseGraphH + (int)Math.Round(6 * scale);
                    if (_settings.EnhancedDiskMode && _settings.DiskLegendExpanded)
                    {
                        int driveCount = Math.Max(1, _hardware.Disk.Drives.Count);
                        int extraH = _pnlDiskLegend != null ? _pnlDiskLegend.ExpandedExtraHeight : 0;
                        totalH += (int)Math.Round((driveCount * 22 + 4 + extraH) * scale);
                    }
                }
            }

            if (_settings.ShowFanGraph)
            {
                totalH += (int)Math.Round(33 * scale);
                if (!_settings.SectionFansCollapsed)
                {
                    float fanScale = _settings.FanScale * scale;
                    if (_settings.FanVisualMode == 1)
                    {
                        totalH += (int)Math.Round(72 * fanScale) + (int)Math.Round(16 * scale);
                    }
                    else if (_settings.FanVisualMode == 2)
                    {
                        int fanCount = _settings.LastKnownFanCount > 0 ? _settings.LastKnownFanCount : 4;
                        int cW = Math.Max(180, (int)Math.Round(420 * scale) - (int)Math.Round(28 * scale));
                        int cols = (cW < (int)Math.Round(380 * scale)) ? 2 : ((cW < (int)Math.Round(560 * scale)) ? 3 : Math.Max(2, (cW - 4) / (int)Math.Round(120 * scale)));
                        int rows = (int)Math.Ceiling((double)fanCount / cols);
                        int cardH = (int)Math.Round((42 + 10 * _settings.FanScale) * scale);
                        int gapY = (int)Math.Max(2, Math.Round(5 * scale));
                        totalH += (rows * cardH) + ((rows - 1) * gapY) + (int)Math.Round(14 * scale);
                    }
                    else
                    {
                        totalH += baseGraphH;
                    }
                }
            }

            return Math.Max((int)Math.Round(160 * scale), totalH);
        }

        public void ApplyUiScale(bool adjustWindowDimensions = false)
        {
            _currentUiScale = GetEffectiveUiScale();
            float scale = _currentUiScale;

            this.MinimumSize = new Size((int)Math.Round(280 * scale), (int)Math.Round(160 * scale));

            if (adjustWindowDimensions)
            {
                int targetW = (int)Math.Round(420 * scale);
                int targetH = CalculateDefaultHeight(scale);

                var screen = Screen.FromControl(this) ?? Screen.PrimaryScreen;
                if (screen != null)
                {
                    targetW = Math.Min(targetW, screen.WorkingArea.Width);
                    targetH = Math.Min(targetH, screen.WorkingArea.Height);
                }

                this.Size = new Size(targetW, targetH);
                _settings.WindowWidth = targetW;
                _settings.WindowHeight = targetH;
                _settings.Save();
            }

            UpdateControlFontsAndSizes(scale);
            LayoutComponents();
            this.Invalidate(true);
        }

        private void UpdateControlFontsAndSizes(float scale)
        {
            if (_lblTitle != null) _lblTitle.Font = new Font("Segoe UI", (float)Math.Max(5.5, Math.Round(10f * scale, 1)), FontStyle.Bold);
            if (_btnInfo != null) _btnInfo.Font = new Font("Segoe UI", (float)Math.Max(5.5, Math.Round(11f * scale, 1)), FontStyle.Bold);
            if (_btnPin != null) _btnPin.Font = new Font("Segoe UI", (float)Math.Max(5.5, Math.Round(11f * scale, 1)));
            if (_btnDetach != null) _btnDetach.Font = new Font("Segoe UI", (float)Math.Max(5.5, Math.Round(12f * scale, 1)), FontStyle.Bold);
            if (_btnClose != null) _btnClose.Font = new Font("Segoe UI", (float)Math.Max(5.5, Math.Round(10.5f * scale, 1)), FontStyle.Bold);

            if (_lblStopwatch != null) _lblStopwatch.Font = new Font("Segoe UI", (float)Math.Max(5.5, Math.Round(9.5f * scale, 1)), FontStyle.Bold);
            if (_lblStopwatchSub != null) _lblStopwatchSub.Font = new Font("Segoe UI", (float)Math.Max(4.8, Math.Round(8.0f * scale, 1)));
            if (_btnSwDashboardAdd != null) _btnSwDashboardAdd.Font = new Font("Segoe UI", (float)Math.Max(5.5, Math.Round(9.0f * scale, 1)), FontStyle.Bold);

            if (_lblCpu != null) _lblCpu.Font = new Font("Segoe UI", (float)Math.Max(5.5, Math.Round(9.5f * scale, 1)), FontStyle.Bold);
            if (_lblCpuSub != null) _lblCpuSub.Font = new Font("Segoe UI", (float)Math.Max(4.8, Math.Round(8.0f * scale, 1)));
            if (_btnCpuProcessesExpand != null) _btnCpuProcessesExpand.Font = new Font("Segoe UI", (float)Math.Max(5.0, Math.Round(7.0f * scale, 1)), FontStyle.Bold);

            if (_lblRam != null) _lblRam.Font = new Font("Segoe UI", (float)Math.Max(5.5, Math.Round(9.5f * scale, 1)), FontStyle.Bold);
            if (_lblRamSub != null) _lblRamSub.Font = new Font("Segoe UI", (float)Math.Max(4.8, Math.Round(8.0f * scale, 1)));

            if (_lblGpu != null) _lblGpu.Font = new Font("Segoe UI", (float)Math.Max(5.5, Math.Round(9.5f * scale, 1)), FontStyle.Bold);
            if (_chkShowAllGpus != null) _chkShowAllGpus.Font = new Font("Segoe UI", (float)Math.Max(5.0, Math.Round(7.5f * scale, 1)));

            if (_lblVram != null) _lblVram.Font = new Font("Segoe UI", (float)Math.Max(5.5, Math.Round(9.5f * scale, 1)), FontStyle.Bold);
            if (_lblVramSub != null) _lblVramSub.Font = new Font("Segoe UI", (float)Math.Max(4.8, Math.Round(8.0f * scale, 1)));

            if (_lblDisk != null) _lblDisk.Font = new Font("Segoe UI", (float)Math.Max(5.5, Math.Round(9.5f * scale, 1)), FontStyle.Bold);
            if (_lblDiskSub != null) _lblDiskSub.Font = new Font("Segoe UI", (float)Math.Max(4.8, Math.Round(8.0f * scale, 1)));
            if (_btnDiskExpand != null) _btnDiskExpand.Font = new Font("Segoe UI", (float)Math.Max(5.0, Math.Round(7.0f * scale, 1)), FontStyle.Bold);
            if (_lblHddStatus != null) _lblHddStatus.Font = new Font("Segoe UI", (float)Math.Max(5.0, Math.Round(8.5f * scale, 1)), FontStyle.Bold);

            if (_lblFans != null) _lblFans.Font = new Font("Segoe UI", (float)Math.Max(5.5, Math.Round(9.5f * scale, 1)), FontStyle.Bold);
            if (_lblFansSub != null) _lblFansSub.Font = new Font("Segoe UI", (float)Math.Max(4.8, Math.Round(8.0f * scale, 1)));

            if (_btnSilent != null) _btnSilent.Font = new Font("Segoe UI", (float)Math.Max(5.2, Math.Round(8.5f * scale, 1)), FontStyle.Bold);
            if (_btnBoost != null) _btnBoost.Font = new Font("Segoe UI", (float)Math.Max(5.2, Math.Round(8.5f * scale, 1)), FontStyle.Bold);
            if (_btnGpuSilent != null) _btnGpuSilent.Font = new Font("Segoe UI", (float)Math.Max(5.2, Math.Round(8.5f * scale, 1)), FontStyle.Bold);
            if (_btnGpuBoost != null) _btnGpuBoost.Font = new Font("Segoe UI", (float)Math.Max(5.2, Math.Round(8.5f * scale, 1)), FontStyle.Bold);

            foreach (var rc in _stopwatchRowControls)
            {
                var f = new Font("Segoe UI", (float)Math.Max(4.8, Math.Round(7.0f * scale, 1)), FontStyle.Bold);
                rc.BtnStart.Font = f;
                rc.BtnAuto.Font = f;
                rc.BtnStop.Font = f;
                rc.BtnReset.Font = f;
                rc.BtnScale.Font = f;
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

            _currentUiScale = GetEffectiveUiScale();
            float scale = _currentUiScale;

            int initW = Math.Max((int)Math.Round(300 * scale), _settings.WindowWidth > 0 ? _settings.WindowWidth : (int)Math.Round(420 * scale));
            int defaultH = CalculateDefaultHeight(scale);
            int initH = Math.Max((int)Math.Round(340 * scale), _settings.WindowHeight > 0 ? _settings.WindowHeight : defaultH);
            this.ClientSize = new Size(initW, initH);
            this.MinimumSize = new Size((int)Math.Round(280 * scale), (int)Math.Round(320 * scale));

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
                Text = Loc.Get("AppTitle"),
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

            // Stopwatch Module
            _sepStopwatch = new ModuleSeparator(StopwatchAccentColor);
            _sepStopwatch.Click += (s, e) => ToggleSectionStopwatch();
            this.Controls.Add(_sepStopwatch);

            _lblStopwatch = CreateMetricHeader("⏱ " + Loc.Get("Stopwatch"), StopwatchAccentColor);
            _lblStopwatch.Click += (s, e) => ToggleSectionStopwatch();
            _lblStopwatchSub = CreateMetricSubHeader(Loc.Get("StopwatchStopped"));
            this.Controls.Add(_lblStopwatch);
            this.Controls.Add(_lblStopwatchSub);

            _pnlStopwatch = new SmoothPanel
            {
                BackColor = Color.FromArgb(16, 16, 20)
            };
            _pnlStopwatch.Paint += DrawStopwatchCard;
            this.Controls.Add(_pnlStopwatch);

            _btnSwDashboardAdd = new Button
            {
                Text = "+",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 211, 153),
                BackColor = Color.FromArgb(28, 28, 36),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _btnSwDashboardAdd.FlatAppearance.BorderColor = Color.FromArgb(44, 44, 56);
            _toolTip.SetToolTip(_btnSwDashboardAdd, Loc.Get("StopwatchAdd"));
            _btnSwDashboardAdd.Click += (s, e) => {
                using var dlg = new StopwatchEditDialog(null, _hardware.Processes);
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _settings.Stopwatches.Add(dlg.Profile);
                    _settings.Save();
                    _hardware.Stopwatch.SyncWithSettings(_settings);
                    RebuildStopwatchControls();
                    LayoutComponents();
                    _pnlStopwatch.Invalidate();
                }
            };
            this.Controls.Add(_btnSwDashboardAdd);

            RebuildStopwatchControls();


            // CPU Module
            _sepCpu = new ModuleSeparator(cpuColor);
            _sepCpu.Click += (s, e) => ToggleSectionCpu();
            this.Controls.Add(_sepCpu);

            _lblCpu = CreateMetricHeader("CPU: 0%", cpuColor);
            _lblCpu.Click += (s, e) => ToggleSectionCpu();
            _lblCpuSub = CreateMetricSubHeader("0.0 GHz | Cores");
            this.Controls.Add(_lblCpu);
            this.Controls.Add(_lblCpuSub);

            _pnlCpuGraph = CreateGraphPanel();
            _pnlCpuGraph.Paint += DrawCpuGraph;
            _pnlCpuGraph.Cursor = Cursors.Hand;
            _pnlCpuGraph.MouseClick += (s, e) => ToggleCpuGraphScale();
            this.Controls.Add(_pnlCpuGraph);

            _pnlCoresMatrix = new SmoothPanel
            {
                BackColor = Color.FromArgb(16, 16, 20)
            };
            _pnlCoresMatrix.Paint += DrawCoresMatrix;
            this.Controls.Add(_pnlCoresMatrix);

            _btnCpuProcessesExpand = new Label
            {
                Text = _settings.ShowTopProcesses ? "▲" : "▼",
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                ForeColor = cpuColor,
                BackColor = Color.FromArgb(20, 24, 32),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand,
                Size = new Size(20, 22)
            };
            _btnCpuProcessesExpand.MouseEnter += (s, e) => _btnCpuProcessesExpand.BackColor = Color.FromArgb(32, 42, 56);
            _btnCpuProcessesExpand.MouseLeave += (s, e) => _btnCpuProcessesExpand.BackColor = Color.FromArgb(20, 24, 32);
            _btnCpuProcessesExpand.Click += (s, e) => ToggleCpuProcesses();
            this.Controls.Add(_btnCpuProcessesExpand);

            _pnlTopProcesses = new SmoothPanel
            {
                BackColor = Color.FromArgb(16, 16, 20),
                Visible = _settings.ShowTopProcesses
            };
            _pnlTopProcesses.Paint += DrawTopProcesses;
            this.Controls.Add(_pnlTopProcesses);

            // RAM Module
            _sepRam = new ModuleSeparator(Color.FromArgb(56, 189, 248));
            _sepRam.Click += (s, e) => ToggleSectionRam();
            this.Controls.Add(_sepRam);

            _lblRam = CreateMetricHeader("RAM: 0%", Color.FromArgb(56, 189, 248));
            _lblRam.Click += (s, e) => ToggleSectionRam();
            _lblRamSub = CreateMetricSubHeader("0.0 / 0.0 GB");
            this.Controls.Add(_lblRam);
            this.Controls.Add(_lblRamSub);

            _pnlRamGraph = CreateGraphPanel();
            _pnlRamGraph.Paint += DrawRamGraph;
            this.Controls.Add(_pnlRamGraph);

            // GPU Module
            _sepGpu = new ModuleSeparator(Color.FromArgb(168, 85, 247));
            _sepGpu.Click += (s, e) => ToggleSectionGpu();
            this.Controls.Add(_sepGpu);

            _lblGpu = CreateMetricHeader("GPU: 0%", Color.FromArgb(168, 85, 247));
            _lblGpu.Click += (s, e) => ToggleSectionGpu();
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
            _sepVram.Click += (s, e) => ToggleSectionVram();
            this.Controls.Add(_sepVram);

            _lblVram = CreateMetricHeader("VRAM: 0%", Color.FromArgb(236, 72, 153));
            _lblVram.Click += (s, e) => ToggleSectionVram();
            _lblVramSub = CreateMetricSubHeader("0.0 / 0.0 GB");
            this.Controls.Add(_lblVram);
            this.Controls.Add(_lblVramSub);

            _pnlVramGraph = CreateGraphPanel();
            _pnlVramGraph.Paint += DrawVramGraph;
            this.Controls.Add(_pnlVramGraph);

            // DISK Module
            _sepDisk = new ModuleSeparator(Color.FromArgb(6, 182, 212));
            _sepDisk.Click += (s, e) => ToggleSectionDisk();
            this.Controls.Add(_sepDisk);

            _lblDisk = CreateMetricHeader("DISK: 0%", Color.FromArgb(6, 182, 212));
            _lblDisk.Click += (s, e) => ToggleSectionDisk();
            _lblDiskSub = CreateMetricSubHeader("R:0.0 W:0.0 MB/s [Idle]");
            this.Controls.Add(_lblDisk);
            this.Controls.Add(_lblDiskSub);

            _lblHddStatus = new Label
            {
                Text = "⚡",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 211, 153),
                BackColor = Color.FromArgb(20, 24, 32),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand,
                Size = new Size(18, 18),
                Visible = false
            };
            _lblHddStatus.MouseEnter += (s, e) => _lblHddStatus.BackColor = Color.FromArgb(32, 42, 56);
            _lblHddStatus.MouseLeave += (s, e) => _lblHddStatus.BackColor = Color.FromArgb(20, 24, 32);
            _lblHddStatus.Click += (s, e) => ShowSettingsDialog();
            this.Controls.Add(_lblHddStatus);

            _btnDiskExpand = new Label
            {
                Text = _settings.DiskLegendExpanded ? "▲" : "▼",
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(6, 182, 212),
                BackColor = Color.FromArgb(20, 24, 32),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand,
                Size = new Size(20, 18)
            };
            _btnDiskExpand.MouseEnter += (s, e) => _btnDiskExpand.BackColor = Color.FromArgb(32, 42, 56);
            _btnDiskExpand.MouseLeave += (s, e) => _btnDiskExpand.BackColor = Color.FromArgb(20, 24, 32);
            _btnDiskExpand.Click += (s, e) => ToggleDiskLegend();
            this.Controls.Add(_btnDiskExpand);

            _pnlDiskLegend = new DiskLegendPanel(_hardware, _settings, () => {
                _pnlDiskGraph.Invalidate();
            }, (expanded) => {
                OnDiskSelectionLayoutChanged(expanded);
            });
            this.Controls.Add(_pnlDiskLegend);

            _pnlDiskGraph = CreateGraphPanel();
            _pnlDiskGraph.Paint += DrawDiskGraph;
            this.Controls.Add(_pnlDiskGraph);

            // FANS Module
            _sepFans = new ModuleSeparator(Color.FromArgb(14, 165, 233));
            _sepFans.Click += (s, e) => ToggleSectionFans();
            this.Controls.Add(_sepFans);

            _lblFans = CreateMetricHeader("FANS: 0 RPM", Color.FromArgb(14, 165, 233));
            _lblFans.Click += (s, e) => ToggleSectionFans();
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
            UpdateControlFontsAndSizes(_currentUiScale);
            LayoutComponents();
        }

        private void LayoutComponents()
        {
            _currentUiScale = GetEffectiveUiScale();
            float scale = _currentUiScale;
            UpdateControlFontsAndSizes(scale);

            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;

            int marginX = (int)Math.Round(14 * scale);
            int contentW = Math.Max(180, w - (marginX * 2));

            // 1. Header Layout
            int headerH = (int)Math.Round(36 * scale);
            _headerPanel.Location = new Point(0, 0);
            _headerPanel.Size = new Size(w, headerH);

            int appIconSize = (int)Math.Round(22 * scale);
            int appIconPadX = (int)Math.Round(10 * scale);
            _picAppIcon.Location = new Point(appIconPadX, (headerH - appIconSize) / 2);
            _picAppIcon.Size = new Size(appIconSize, appIconSize);

            _lblTitle.Location = new Point(_picAppIcon.Right + (int)Math.Round(6 * scale), (headerH - _lblTitle.PreferredHeight) / 2);

            int btnSize = (int)Math.Round(24 * scale);
            int btnY = (headerH - btnSize) / 2;
            int btnGap = (int)Math.Round(4 * scale);
            int curBtnX = w - (int)Math.Round(8 * scale) - btnSize;

            _btnClose.Location = new Point(curBtnX, btnY);
            _btnClose.Size = new Size(btnSize, btnSize);
            curBtnX -= btnSize + btnGap;

            _btnDetach.Location = new Point(curBtnX, btnY);
            _btnDetach.Size = new Size(btnSize, btnSize);
            curBtnX -= btnSize + btnGap;

            _btnPin.Location = new Point(curBtnX, btnY);
            _btnPin.Size = new Size(btnSize, btnSize);
            curBtnX -= btnSize + btnGap;

            _btnInfo.Location = new Point(curBtnX, btnY);
            _btnInfo.Size = new Size(btnSize, btnSize);

            // 2. Footer Layout (Single row unified, or two rows when separate CPU/GPU control is enabled)
            bool separate = _settings.SeparateCpuGpuControl;
            int btnBaseH = (int)Math.Round(32 * scale);
            int footerH = separate ? (btnBaseH * 2 + (int)Math.Round(14 * scale)) : (btnBaseH + (int)Math.Round(12 * scale));
            int footerY = h - footerH;
            _footerPanel.Location = new Point(0, footerY);
            _footerPanel.Size = new Size(w, footerH);
            _footerPanel.BringToFront();

            int gearW = (int)Math.Round(34 * scale);
            int fButtonGap = (int)Math.Round(4 * scale);
            int availableBtnW = contentW - gearW - fButtonGap;
            int halfW = (availableBtnW - fButtonGap) / 2;
            int fBtnY = (int)Math.Round(6 * scale);

            // Row 1: CPU Controls
            _btnSilent.Location = new Point(marginX, fBtnY);
            _btnSilent.Size = new Size(halfW, btnBaseH);

            _btnBoost.Location = new Point(marginX + halfW + fButtonGap, fBtnY);
            _btnBoost.Size = new Size(availableBtnW - halfW - fButtonGap, btnBaseH);

            // Single unified Gear button on the right (centered vertically across 1 or 2 rows)
            int gearH = separate ? (btnBaseH * 2 + (int)Math.Round(4 * scale)) : btnBaseH;
            _btnGear.Location = new Point(marginX + contentW - gearW, fBtnY);
            _btnGear.Size = new Size(gearW, gearH);

            // Row 2: GPU Controls (visible only when separate controls are enabled)
            _btnGpuSilent.Visible = separate;
            _btnGpuBoost.Visible = separate;

            if (separate)
            {
                int row2Y = fBtnY + btnBaseH + (int)Math.Round(4 * scale);
                _btnGpuSilent.Location = new Point(marginX, row2Y);
                _btnGpuSilent.Size = new Size(halfW, btnBaseH);

                _btnGpuBoost.Location = new Point(marginX + halfW + fButtonGap, row2Y);
                _btnGpuBoost.Size = new Size(availableBtnW - halfW - fButtonGap, btnBaseH);
            }

            int gripSize = (int)Math.Round(16 * scale);
            _lblResizeGrip.Size = new Size(gripSize, gripSize);
            _lblResizeGrip.Location = new Point(w - gripSize - 2, footerH - gripSize - 2);

            // 3. Middle Area: Dynamic Graph Sections
            int topY = headerH + (int)Math.Round(2 * scale);
            int availableH = Math.Max((int)Math.Round(150 * scale), (footerY - (int)Math.Round(8 * scale)) - topY);

            bool showCpu = _settings.ShowCpuGraph || _settings.ShowCpuCores || _settings.ShowTopProcesses;
            bool showRam = _settings.ShowRamGraph;
            bool showGpu = _settings.ShowGpuGraph;
            bool showVram = _settings.ShowVramGraph;
            bool showDisk = _settings.ShowDiskGraph;
            bool showFans = _settings.ShowFanGraph;

            int activeGraphCount = 0;
            if (_settings.ShowCpuGraph && !_settings.SectionCpuCollapsed) activeGraphCount++;
            if (_settings.ShowRamGraph && !_settings.SectionRamCollapsed) activeGraphCount++;
            if (_settings.ShowGpuGraph && !_settings.SectionGpuCollapsed) activeGraphCount++;
            if (_settings.ShowVramGraph && !_settings.SectionVramCollapsed) activeGraphCount++;
            if (_settings.ShowDiskGraph && !_settings.SectionDiskCollapsed) activeGraphCount++;
            if (_settings.ShowFanGraph && !_settings.SectionFansCollapsed) activeGraphCount++;
            if (activeGraphCount == 0) activeGraphCount = 1;

            int sepH = Math.Max(2, (int)Math.Round(3 * scale));
            int sepGap = (int)Math.Round(5 * scale);
            int headerRowH = (int)Math.Round(20 * scale);

            int fixedH = 0;
            var visibleSw = _hardware.Stopwatch.Items.Where(i => i.Profile.IsVisible).ToList();
            int swCount = visibleSw.Count;
            int swRowH = (int)Math.Round(50 * scale);
            int swGap = (int)Math.Round(4 * scale);
            int swCardH = swCount > 0 ? (swCount * swRowH) + ((swCount - 1) * swGap) : 0;
            if (_settings.ShowStopwatch)
            {
                if (swCount > 0 && !_settings.SectionStopwatchCollapsed)
                {
                    fixedH += sepH + sepGap + headerRowH + swCardH + (int)Math.Round(6 * scale);
                }
                else
                {
                    fixedH += sepH + sepGap + headerRowH + (int)Math.Round(4 * scale);
                }
            }
            if (showCpu)
            {
                fixedH += sepH + sepGap + headerRowH;
                if (!_settings.SectionCpuCollapsed)
                {
                    if (_settings.ShowCpuCores) fixedH += (int)Math.Round(22 * scale) + (int)Math.Round(4 * scale);
                    if (_settings.ShowTopProcesses) fixedH += (int)Math.Round(90 * scale) + (int)Math.Round(6 * scale);
                }
            }
            if (showRam) fixedH += sepH + sepGap + headerRowH + (!_settings.SectionRamCollapsed ? (int)Math.Round(6 * scale) : 0);
            if (showGpu) fixedH += sepH + sepGap + headerRowH + (!_settings.SectionGpuCollapsed ? (int)Math.Round(6 * scale) : 0);
            if (showVram) fixedH += sepH + sepGap + headerRowH + (!_settings.SectionVramCollapsed && (showDisk || showFans) ? (int)Math.Round(6 * scale) : 0);

            int diskLegendH = 0;
            if (showDisk && !_settings.SectionDiskCollapsed && _settings.EnhancedDiskMode && _settings.DiskLegendExpanded)
            {
                int driveCount = Math.Max(1, _hardware.Disk.Drives.Count);
                int extraH = _pnlDiskLegend != null ? _pnlDiskLegend.ExpandedExtraHeight : 0;
                diskLegendH = (int)Math.Round((driveCount * 22 + 4 + extraH) * scale);
                fixedH += diskLegendH + (int)Math.Round(4 * scale);
            }
            if (showDisk) fixedH += sepH + sepGap + headerRowH + (!_settings.SectionDiskCollapsed && showFans ? (int)Math.Round(6 * scale) : 0);
            if (showFans) fixedH += sepH + (int)Math.Round(6 * scale) + (int)Math.Round(24 * scale);

            int fanH;
            int graphH;
            int minGraphH = (int)Math.Round(22 * scale);

            if (showFans && !_settings.SectionFansCollapsed && _settings.FanVisualMode == 1)
            {
                float fanScale = _settings.FanScale * scale;
                fanH = (int)Math.Round(72 * fanScale) + (int)Math.Round(4 * scale);
                fixedH += fanH;
                int nonFanGraphs = activeGraphCount - 1;
                if (nonFanGraphs <= 0) nonFanGraphs = 1;
                int remainingForGraphs = Math.Max((int)Math.Round(30 * scale), availableH - fixedH);
                graphH = Math.Max(minGraphH, remainingForGraphs / nonFanGraphs);
            }
            else if (showFans && !_settings.SectionFansCollapsed && _settings.FanVisualMode == 2)
            {
                var allFans = _hardware.Fans.Fans;
                int visibleFanCount = allFans.Count(f => _settings.IsFanVisible(f.Id, f.Name));
                if (visibleFanCount == 0) visibleFanCount = 1;
                float fanScale = _settings.FanScale * scale;
                int cols;
                if (contentW < (int)Math.Round(380 * scale))
                {
                    cols = 2;
                }
                else if (contentW < (int)Math.Round(560 * scale))
                {
                    cols = 3;
                }
                else
                {
                    cols = Math.Max(2, (contentW - 4) / (int)Math.Round(120 * scale));
                }

                int rows = (int)Math.Ceiling((double)visibleFanCount / cols);
                if (rows < 1) rows = 1;
                int gapX = (int)Math.Max(2, Math.Round(5 * scale));
                int padX = (int)Math.Max(2, Math.Round(2 * scale));
                int cardW = (contentW - (padX * 2) - (gapX * (cols - 1))) / cols;
                bool isWideCard = (cardW >= 135 * scale);
                int cardH = (int)Math.Round((isWideCard 
                    ? (46 + 18 * _settings.FanScale) 
                    : (40 + 10 * _settings.FanScale)) * scale);
                int gapY = (int)Math.Max(2, Math.Round(5 * scale));
                fanH = (rows * cardH) + ((rows - 1) * gapY) + (int)Math.Round(8 * scale);
                fixedH += fanH;
                int nonFanGraphs = activeGraphCount - 1;
                if (nonFanGraphs <= 0) nonFanGraphs = 1;
                int remainingForGraphs = Math.Max((int)Math.Round(30 * scale), availableH - fixedH);
                graphH = Math.Max(minGraphH, remainingForGraphs / nonFanGraphs);
            }
            else if (showFans && !_settings.SectionFansCollapsed)
            {
                int remainingForGraphs = Math.Max((int)Math.Round(40 * scale), availableH - fixedH);
                graphH = Math.Max(minGraphH, remainingForGraphs / activeGraphCount);
                fanH = graphH;
            }
            else
            {
                fanH = 0;
                int remainingForGraphs = Math.Max((int)Math.Round(40 * scale), availableH - fixedH);
                graphH = Math.Max(minGraphH, remainingForGraphs / activeGraphCount);
            }

            int curY = topY;

            // --- STOPWATCH ---
            bool showStopwatch = _settings.ShowStopwatch;
            bool isSwCollapsed = _settings.SectionStopwatchCollapsed;
            bool hasVisibleSw = visibleSw.Count > 0;
            _sepStopwatch.Visible = showStopwatch;
            _lblStopwatch.Visible = showStopwatch;
            _lblStopwatchSub.Visible = showStopwatch;
            _btnSwDashboardAdd.Visible = showStopwatch && !isSwCollapsed;
            _pnlStopwatch.Visible = showStopwatch && hasVisibleSw && !isSwCollapsed;

            if (showStopwatch)
            {
                _sepStopwatch.Location = new Point(0, curY);
                _sepStopwatch.Size = new Size(w, sepH);
                curY += sepGap;

                int swAddW = (int)Math.Round(24 * scale);
                int swAddH = (int)Math.Round(20 * scale);
                if (!isSwCollapsed)
                {
                    _btnSwDashboardAdd.Location = new Point(marginX + contentW - swAddW, curY);
                    _btnSwDashboardAdd.Size = new Size(swAddW, swAddH);
                }

                _lblStopwatch.Location = new Point(marginX, curY);
                int swLblW = Math.Max((int)(70 * scale), _lblStopwatch.PreferredWidth);
                int swRightPad = !isSwCollapsed ? (swAddW + (int)Math.Round(6 * scale)) : 0;
                _lblStopwatchSub.Location = new Point(marginX + swLblW + (int)Math.Round(6 * scale), curY);
                _lblStopwatchSub.Size = new Size(Math.Max(50, contentW - swLblW - (int)Math.Round(6 * scale) - swRightPad), headerRowH);
                curY += headerRowH;

                if (!isSwCollapsed && hasVisibleSw)
                {
                    _pnlStopwatch.Location = new Point(marginX, curY);
                    _pnlStopwatch.Size = new Size(contentW, swCardH);
                    LayoutStopwatchButtons(contentW, swRowH, swGap);
                    curY += swCardH + (int)Math.Round(6 * scale);
                }
                else
                {
                    curY += (int)Math.Round(4 * scale);
                }
            }

            // --- CPU ---
            bool isCpuCollapsed = _settings.SectionCpuCollapsed;
            _sepCpu.Visible = showCpu;
            _lblCpu.Visible = showCpu;
            _lblCpuSub.Visible = showCpu;
            _pnlCpuGraph.Visible = _settings.ShowCpuGraph && !isCpuCollapsed;
            _pnlCoresMatrix.Visible = _settings.ShowCpuCores && !isCpuCollapsed;
            _btnCpuProcessesExpand.Visible = showCpu && !isCpuCollapsed;

            if (showCpu)
            {
                _sepCpu.Location = new Point(0, curY);
                _sepCpu.Size = new Size(w, sepH);
                curY += sepGap;

                _lblCpu.Location = new Point(marginX, curY);
                int cpuLabelW = Math.Max((int)(65 * scale), _lblCpu.PreferredWidth);
                int cpuSubRightOffset = isCpuCollapsed ? 0 : (_settings.ShowCpuCores ? 0 : (int)Math.Round(24 * scale));
                _lblCpuSub.Location = new Point(marginX + cpuLabelW + (int)Math.Round(6 * scale), curY);
                _lblCpuSub.Size = new Size(Math.Max(50, contentW - cpuLabelW - (int)Math.Round(6 * scale) - cpuSubRightOffset), headerRowH);
                curY += headerRowH;

                if (!isCpuCollapsed)
                {
                    if (_settings.ShowCpuGraph)
                    {
                        _pnlCpuGraph.Location = new Point(marginX, curY);
                        _pnlCpuGraph.Size = new Size(contentW, graphH);
                        curY += graphH + (int)Math.Round(2 * scale);
                    }

                    int coresMatrixH = (int)Math.Round(22 * scale);
                    int expandBtnW = (int)Math.Round(20 * scale);
                    if (_settings.ShowCpuCores)
                    {
                        _pnlCoresMatrix.Location = new Point(marginX, curY);
                        _pnlCoresMatrix.Size = new Size(contentW - expandBtnW - (int)Math.Round(4 * scale), coresMatrixH);

                        _btnCpuProcessesExpand.Location = new Point(marginX + contentW - expandBtnW, curY);
                        _btnCpuProcessesExpand.Size = new Size(expandBtnW, coresMatrixH);
                        _btnCpuProcessesExpand.Text = _settings.ShowTopProcesses ? "▲" : "▼";
                        _toolTip.SetToolTip(_btnCpuProcessesExpand, _settings.ShowTopProcesses ? Loc.Get("ProcessesCollapse") : Loc.Get("ProcessesExpand"));

                        curY += coresMatrixH + (int)Math.Round(4 * scale);
                    }
                    else
                    {
                        _btnCpuProcessesExpand.Location = new Point(marginX + contentW - expandBtnW, curY - headerRowH + 1);
                        _btnCpuProcessesExpand.Size = new Size(expandBtnW, (int)Math.Round(18 * scale));
                        _btnCpuProcessesExpand.Text = _settings.ShowTopProcesses ? "▲" : "▼";
                        _toolTip.SetToolTip(_btnCpuProcessesExpand, _settings.ShowTopProcesses ? Loc.Get("ProcessesCollapse") : Loc.Get("ProcessesExpand"));
                    }

                    if (_settings.ShowTopProcesses)
                    {
                        int topProcH = (int)Math.Round(90 * scale);
                        _pnlTopProcesses.Visible = true;
                        _pnlTopProcesses.Location = new Point(marginX, curY);
                        _pnlTopProcesses.Size = new Size(contentW, topProcH);
                        curY += topProcH + (int)Math.Round(6 * scale);
                    }
                    else
                    {
                        _pnlTopProcesses.Visible = false;
                        curY += (int)Math.Round(2 * scale);
                    }
                }
                else
                {
                    _pnlTopProcesses.Visible = false;
                    curY += (int)Math.Round(4 * scale);
                }
            }
            else
            {
                _pnlTopProcesses.Visible = false;
                _btnCpuProcessesExpand.Visible = false;
            }

            // --- RAM ---
            bool isRamCollapsed = _settings.SectionRamCollapsed;
            _sepRam.Visible = showRam;
            _lblRam.Visible = showRam;
            _lblRamSub.Visible = showRam;
            _pnlRamGraph.Visible = showRam && !isRamCollapsed;

            if (showRam)
            {
                _sepRam.Location = new Point(0, curY);
                _sepRam.Size = new Size(w, sepH);
                curY += sepGap;

                _lblRam.Location = new Point(marginX, curY);
                int ramLabelW = Math.Max((int)(65 * scale), _lblRam.PreferredWidth);
                _lblRamSub.Location = new Point(marginX + ramLabelW + (int)Math.Round(6 * scale), curY);
                _lblRamSub.Size = new Size(Math.Max(50, contentW - ramLabelW - (int)Math.Round(6 * scale)), headerRowH);
                curY += headerRowH;

                if (!isRamCollapsed)
                {
                    _pnlRamGraph.Location = new Point(marginX, curY);
                    _pnlRamGraph.Size = new Size(contentW, graphH);
                    curY += graphH + (int)Math.Round(6 * scale);
                }
                else
                {
                    curY += (int)Math.Round(4 * scale);
                }
            }

            // --- GPU ---
            bool isGpuCollapsed = _settings.SectionGpuCollapsed;
            _sepGpu.Visible = showGpu;
            _lblGpu.Visible = showGpu;
            _pnlGpuSub.Visible = showGpu;
            _pnlGpuGraph.Visible = showGpu && !isGpuCollapsed;

            bool multiGpu = _hardware.Gpu.GpuCount > 1;
            _chkShowAllGpus.Visible = showGpu && multiGpu && !isGpuCollapsed;

            if (showGpu)
            {
                _sepGpu.Location = new Point(0, curY);
                _sepGpu.Size = new Size(w, sepH);
                curY += sepGap;

                _lblGpu.Location = new Point(marginX, curY);
                int gpuLabelW = Math.Max((int)(65 * scale), _lblGpu.PreferredWidth);
                _pnlGpuSub.UiScale = scale;

                if (multiGpu && !isGpuCollapsed)
                {
                    int chkW = (int)Math.Round(74 * scale);
                    _chkShowAllGpus.Location = new Point(w - marginX - chkW, curY + 2);
                    _pnlGpuSub.Location = new Point(marginX + gpuLabelW + (int)Math.Round(6 * scale), curY);
                    _pnlGpuSub.Size = new Size(Math.Max(50, w - marginX - chkW - (marginX + gpuLabelW + (int)Math.Round(6 * scale)) - 4), headerRowH);
                }
                else
                {
                    _pnlGpuSub.Location = new Point(marginX + gpuLabelW + (int)Math.Round(6 * scale), curY);
                    _pnlGpuSub.Size = new Size(Math.Max(50, contentW - gpuLabelW - (int)Math.Round(6 * scale)), headerRowH);
                }
                curY += headerRowH;

                if (!isGpuCollapsed)
                {
                    _pnlGpuGraph.Location = new Point(marginX, curY);
                    _pnlGpuGraph.Size = new Size(contentW, graphH);
                    curY += graphH + (int)Math.Round(6 * scale);
                }
                else
                {
                    curY += (int)Math.Round(4 * scale);
                }
            }

            // --- VRAM ---
            bool isVramCollapsed = _settings.SectionVramCollapsed;
            _sepVram.Visible = showVram;
            _lblVram.Visible = showVram;
            _lblVramSub.Visible = showVram;
            _pnlVramGraph.Visible = showVram && !isVramCollapsed;

            if (showVram)
            {
                _sepVram.Location = new Point(0, curY);
                _sepVram.Size = new Size(w, sepH);
                curY += sepGap;

                _lblVram.Location = new Point(marginX, curY);
                int vramLabelW = Math.Max((int)(65 * scale), _lblVram.PreferredWidth);
                _lblVramSub.Location = new Point(marginX + vramLabelW + (int)Math.Round(6 * scale), curY);
                _lblVramSub.Size = new Size(Math.Max(50, contentW - vramLabelW - (int)Math.Round(6 * scale)), headerRowH);
                curY += headerRowH;

                if (!isVramCollapsed)
                {
                    _pnlVramGraph.Location = new Point(marginX, curY);
                    _pnlVramGraph.Size = new Size(contentW, graphH);
                    curY += graphH + (showDisk || showFans ? (int)Math.Round(6 * scale) : 0);
                }
                else
                {
                    curY += (int)Math.Round(4 * scale);
                }
            }

            // --- DISK ---
            bool isDiskCollapsed = _settings.SectionDiskCollapsed;
            bool enhancedDisk = _settings.EnhancedDiskMode;
            bool diskLegendOpen = enhancedDisk && _settings.DiskLegendExpanded;
            bool showHddStatus = showDisk && _settings.HddScheduleEnabled;
            _sepDisk.Visible = showDisk;
            _lblDisk.Visible = showDisk;
            _lblDiskSub.Visible = showDisk;
            _btnDiskExpand.Visible = showDisk && enhancedDisk && !isDiskCollapsed;
            _lblHddStatus.Visible = showHddStatus && !isDiskCollapsed;
            if (_pnlDiskLegend != null) _pnlDiskLegend.Visible = showDisk && diskLegendOpen && !isDiskCollapsed;
            _pnlDiskGraph.Visible = showDisk && !isDiskCollapsed;

            if (showDisk)
            {
                _sepDisk.Location = new Point(0, curY);
                _sepDisk.Size = new Size(w, sepH);
                curY += sepGap;

                _lblDisk.Location = new Point(marginX, curY);
                int diskLabelW = Math.Max((int)(65 * scale), _lblDisk.PreferredWidth);

                int rightOffset = 0;
                int expandW = (int)Math.Round(20 * scale);
                if (!isDiskCollapsed && enhancedDisk)
                {
                    _btnDiskExpand.Location = new Point(marginX + contentW - expandW, curY + 1);
                    _btnDiskExpand.Size = new Size(expandW, (int)Math.Round(18 * scale));
                    _btnDiskExpand.Text = _settings.DiskLegendExpanded ? "▲" : "▼";
                    _toolTip.SetToolTip(_btnDiskExpand, _settings.DiskLegendExpanded ? Loc.Get("DiskLegendCollapse") : Loc.Get("DiskLegendExpand"));
                    rightOffset += expandW + (int)Math.Round(4 * scale);
                }

                if (!isDiskCollapsed && showHddStatus)
                {
                    int hddStatW = (int)Math.Round(18 * scale);
                    _lblHddStatus.Location = new Point(marginX + contentW - rightOffset - hddStatW, curY + 1);
                    _lblHddStatus.Size = new Size(hddStatW, (int)Math.Round(18 * scale));
                    rightOffset += hddStatW + (int)Math.Round(4 * scale);
                }

                _lblDiskSub.Location = new Point(marginX + diskLabelW + (int)Math.Round(6 * scale), curY);
                _lblDiskSub.Size = new Size(Math.Max(50, contentW - diskLabelW - (int)Math.Round(6 * scale) - rightOffset), headerRowH);
                curY += headerRowH;

                if (!isDiskCollapsed)
                {
                    if (diskLegendOpen && _pnlDiskLegend != null)
                    {
                        _pnlDiskLegend.UiScale = scale;
                        _pnlDiskLegend.Location = new Point(marginX, curY);
                        _pnlDiskLegend.Size = new Size(contentW, diskLegendH);
                        _pnlDiskLegend.UpdateLayout();
                        curY += diskLegendH + (int)Math.Round(4 * scale);
                    }

                    _pnlDiskGraph.Location = new Point(marginX, curY);
                    _pnlDiskGraph.Size = new Size(contentW, graphH);
                    curY += graphH + (showFans ? (int)Math.Round(6 * scale) : 0);
                }
                else
                {
                    curY += (int)Math.Round(4 * scale);
                }
            }

            // --- FANS ---
            bool isFansCollapsed = _settings.SectionFansCollapsed;
            _sepFans.Visible = showFans;
            _lblFans.Visible = showFans;
            _lblFansSub.Visible = showFans;
            _pnlFansGraph.Visible = showFans && !isFansCollapsed;

            if (showFans)
            {
                _sepFans.Location = new Point(0, curY);
                _sepFans.Size = new Size(w, sepH);
                curY += (int)Math.Round(6 * scale);

                _lblFans.Location = new Point(marginX, curY);
                int fansLabelW = Math.Max((int)(70 * scale), _lblFans.PreferredWidth);
                _lblFansSub.Location = new Point(marginX + fansLabelW + (int)Math.Round(8 * scale), curY);
                _lblFansSub.Size = new Size(Math.Max(10, contentW - (fansLabelW + (int)Math.Round(8 * scale))), (int)Math.Round(18 * scale));
                curY += (int)Math.Round(24 * scale);

                if (!isFansCollapsed)
                {
                    _pnlFansGraph.Location = new Point(marginX, curY);
                    _pnlFansGraph.Size = new Size(contentW, fanH);
                }
                else
                {
                    curY += (int)Math.Round(4 * scale);
                }
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

        private void ShowSettingsDialog()
        {
            if (_settingsForm == null || _settingsForm.IsDisposed)
            {
                _settingsForm = new SettingsForm(_hardware, _settings, () => {
                    ApplyUiScale(true);
                    _hardware.Fans.DemoMode = _settings.EnableFanDemo;
                    _hardware.Fans.SetEnabled(_settings.EnableFanAddon);
                    _hardware.Stopwatch.SyncWithSettings(_settings);
                    RebuildStopwatchControls();
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
            int minX = screen.WorkingArea.Left + 10;
            int maxX = Math.Max(minX, screen.WorkingArea.Right - _settingsForm.Width - 10);
            x = Math.Clamp(x, minX, maxX);

            int minY = screen.WorkingArea.Top + 10;
            int maxY = Math.Max(minY, screen.WorkingArea.Bottom - _settingsForm.Height - 10);
            y = Math.Clamp(y, minY, maxY);
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

        private string? _lastScreenDeviceName;

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            if (!_isInitialized) return;

            if (_settings.AutoUiScale)
            {
                var curScreen = Screen.FromControl(this);
                if (_lastScreenDeviceName != null && !string.Equals(_lastScreenDeviceName, curScreen.DeviceName, StringComparison.OrdinalIgnoreCase))
                {
                    _lastScreenDeviceName = curScreen.DeviceName;
                    float newScale = GetEffectiveUiScale(curScreen);
                    if (Math.Abs(newScale - _currentUiScale) > 0.01f)
                    {
                        ApplyUiScale(true);
                    }
                }
                else
                {
                    _lastScreenDeviceName = curScreen.DeviceName;
                }
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
                Font = new Font("Segoe UI", (float)Math.Max(5.5, Math.Round(9.5f * _currentUiScale, 1)), FontStyle.Bold),
                ForeColor = color,
                AutoSize = true,
                Cursor = Cursors.Hand
            };
        }

        private Label CreateMetricSubHeader(string text)
        {
            return new Label
            {
                Text = text,
                Font = new Font("Segoe UI", (float)Math.Max(4.8, Math.Round(8.0f * _currentUiScale, 1))),
                ForeColor = Color.FromArgb(165, 165, 180),
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true
            };
        }

        private Button CreateStopwatchButton(string text, Color foreColor)
        {
            var btn = new Button
            {
                Text = text,
                Font = new Font("Segoe UI", (float)Math.Max(4.8, Math.Round(7.0f * _currentUiScale, 1)), FontStyle.Bold),
                ForeColor = foreColor,
                BackColor = Color.FromArgb(28, 28, 36),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = Padding.Empty
            };
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = Color.FromArgb(44, 44, 56);
            return btn;
        }

        private void RebuildStopwatchControls()
        {
            _pnlStopwatch.SuspendLayout();
            _pnlStopwatch.Controls.Clear();
            _stopwatchRowControls.Clear();

            var visibleItems = _hardware.Stopwatch.Items.Where(i => i.Profile.IsVisible).ToList();
            foreach (var item in visibleItems)
            {
                var curItem = item;
                var rc = new StopwatchRowControls
                {
                    Item = curItem,
                    BtnStart = CreateStopwatchButton(Loc.Get("StopwatchStart"), curItem.AccentColor),
                    BtnAuto = CreateStopwatchButton(Loc.Get("StopwatchAuto"), Color.FromArgb(245, 158, 11)),
                    BtnStop = CreateStopwatchButton(Loc.Get("StopwatchStop"), Color.FromArgb(244, 63, 94)),
                    BtnReset = CreateStopwatchButton(Loc.Get("StopwatchReset"), Color.FromArgb(160, 160, 175)),
                    BtnScale = CreateStopwatchButton("📈 Auto", curItem.AccentColor)
                };

                rc.BtnStart.Click += (s, e) => {
                    curItem.Start();
                    UpdateStopwatchUI();
                };
                rc.BtnAuto.Click += (s, e) => {
                    curItem.ToggleAuto();
                    _settings.Save();
                    UpdateStopwatchUI();
                };
                rc.BtnStop.Click += (s, e) => {
                    curItem.Stop();
                    UpdateStopwatchUI();
                };
                rc.BtnReset.Click += (s, e) => {
                    curItem.Reset();
                    UpdateStopwatchUI();
                };
                rc.BtnScale.Click += (s, e) => {
                    CycleStopwatchScale(curItem);
                };

                _pnlStopwatch.Controls.Add(rc.BtnStart);
                _pnlStopwatch.Controls.Add(rc.BtnAuto);
                _pnlStopwatch.Controls.Add(rc.BtnStop);
                _pnlStopwatch.Controls.Add(rc.BtnReset);
                _pnlStopwatch.Controls.Add(rc.BtnScale);

                _stopwatchRowControls.Add(rc);
            }

            _pnlStopwatch.ResumeLayout(true);
            UpdateStopwatchUI();
        }

        private void LayoutStopwatchButtons(int panelW, int rowH, int gap)
        {
            if (_stopwatchRowControls.Count == 0) return;

            float scale = _currentUiScale;
            int actionBtnCount = 4;
            int btnGap = (int)Math.Max(2, Math.Round(3 * scale));
            int scaleBtnW = (int)Math.Max(26, Math.Round(44 * scale));
            int actionBtnW = (int)Math.Max(18, Math.Round(30 * scale));
            int btnH = (int)Math.Max(16, Math.Round(22 * scale));

            int totalButtonsW = (actionBtnW * actionBtnCount) + (btnGap * actionBtnCount) + scaleBtnW;
            int startX = Math.Max((int)Math.Round(95 * scale), panelW - totalButtonsW - (int)Math.Round(4 * scale));

            for (int i = 0; i < _stopwatchRowControls.Count; i++)
            {
                int rowY = i * (rowH + gap);
                int btnY = rowY + (rowH - btnH) / 2;
                var rc = _stopwatchRowControls[i];

                rc.BtnStart.Location = new Point(startX, btnY);
                rc.BtnStart.Size = new Size(actionBtnW, btnH);

                rc.BtnAuto.Location = new Point(startX + (actionBtnW + btnGap), btnY);
                rc.BtnAuto.Size = new Size(actionBtnW, btnH);

                rc.BtnStop.Location = new Point(startX + (actionBtnW + btnGap) * 2, btnY);
                rc.BtnStop.Size = new Size(actionBtnW, btnH);

                rc.BtnReset.Location = new Point(startX + (actionBtnW + btnGap) * 3, btnY);
                rc.BtnReset.Size = new Size(actionBtnW, btnH);

                rc.BtnScale.Location = new Point(startX + (actionBtnW + btnGap) * 4, btnY);
                rc.BtnScale.Size = new Size(scaleBtnW, btnH);
            }
        }

        private void UpdateStopwatchUI()
        {
            if (_stopwatchRowControls.Count == 0) return;

            foreach (var rc in _stopwatchRowControls)
            {
                var item = rc.Item;
                bool running = item.IsRunning;
                bool auto = item.IsAutoArmed;

                if (running)
                {
                    rc.BtnStart.BackColor = item.AccentColor;
                    rc.BtnStart.ForeColor = Color.FromArgb(18, 18, 22);
                    rc.BtnStart.FlatAppearance.BorderColor = item.AccentColor;
                }
                else
                {
                    rc.BtnStart.BackColor = Color.FromArgb(28, 28, 36);
                    rc.BtnStart.ForeColor = item.AccentColor;
                    rc.BtnStart.FlatAppearance.BorderColor = Color.FromArgb(44, 44, 56);
                }

                if (auto)
                {
                    rc.BtnAuto.BackColor = Color.FromArgb(245, 158, 11);
                    rc.BtnAuto.ForeColor = Color.FromArgb(18, 18, 22);
                    rc.BtnAuto.FlatAppearance.BorderColor = Color.FromArgb(245, 158, 11);
                }
                else
                {
                    rc.BtnAuto.BackColor = Color.FromArgb(28, 28, 36);
                    rc.BtnAuto.ForeColor = Color.FromArgb(245, 158, 11);
                    rc.BtnAuto.FlatAppearance.BorderColor = Color.FromArgb(44, 44, 56);
                }

                // Stopwatch Curve Scale button
                int scale = item.Profile.ShowOnCpuGraph ? item.Profile.GraphScale : -1;
                bool isGraphActive = (scale != -1);

                string scaleText = scale switch
                {
                    0 => "📈 " + Loc.Get("StopwatchScale_Auto"),
                    1 => "📈 1x",
                    2 => "📈 x2",
                    4 => "📈 x4",
                    8 => "📈 x8",
                    16 => "📈 x16",
                    _ => "📈 " + Loc.Get("StopwatchScale_Off")
                };
                rc.BtnScale.Text = scaleText;

                if (isGraphActive)
                {
                    rc.BtnScale.BackColor = Color.FromArgb(36, item.AccentColor.R, item.AccentColor.G, item.AccentColor.B);
                    rc.BtnScale.ForeColor = item.AccentColor;
                    rc.BtnScale.FlatAppearance.BorderColor = item.AccentColor;
                }
                else
                {
                    rc.BtnScale.BackColor = Color.FromArgb(24, 24, 32);
                    rc.BtnScale.ForeColor = Color.FromArgb(125, 125, 140);
                    rc.BtnScale.FlatAppearance.BorderColor = Color.FromArgb(46, 46, 58);
                }

                if (_toolTip != null) _toolTip.SetToolTip(rc.BtnScale, Loc.Get("StopwatchScaleTip"));
            }

            UpdateStopwatchSubHeader();
            _pnlStopwatch?.Invalidate();
        }

        private void CycleStopwatchScale(StopwatchItem item)
        {
            int current = item.Profile.ShowOnCpuGraph ? item.Profile.GraphScale : -1;
            int next = current switch
            {
                -1 => 0,  // Off -> Auto
                0 => 2,   // Auto -> 2x
                2 => 4,   // 2x -> 4x
                4 => 8,   // 4x -> 8x
                8 => 16,  // 8x -> 16x
                16 => 1,  // 16x -> 1x
                1 => -1,  // 1x -> Off
                _ => 0
            };

            if (next == -1)
            {
                item.Profile.ShowOnCpuGraph = false;
                item.Profile.GraphScale = -1;
            }
            else
            {
                item.Profile.ShowOnCpuGraph = true;
                item.Profile.GraphScale = next;
            }

            _settings.Save();
            UpdateStopwatchUI();
            _pnlCpuGraph?.Invalidate();
        }

        private void UpdateStopwatchSubHeader()
        {
            if (!_settings.ShowStopwatch || _lblStopwatchSub == null) return;

            float pwr = _hardware.GetTriggerPowerWatts(_settings.StopwatchTriggerSource);
            string pwrText = $"{pwr:F0}W";
            string sourceName = _settings.StopwatchTriggerSource switch
            {
                1 => "CPU",
                2 => "GPU",
                3 => "Total",
                _ => "Max"
            };

            var visibleItems = _hardware.Stopwatch.Items.Where(i => i.Profile.IsVisible).ToList();
            if (visibleItems.Count == 0)
            {
                _lblStopwatchSub.Text = Loc.Get("StopwatchAllHidden");
                _lblStopwatchSub.ForeColor = Color.FromArgb(135, 135, 150);
                return;
            }

            int runningCount = visibleItems.Count(i => i.IsRunning);
            int armedCount = visibleItems.Count(i => i.IsAutoArmed);

            if (runningCount > 0)
            {
                _lblStopwatchSub.Text = $"⚡ {Loc.Get("StopwatchRunning")} ({runningCount}) | {sourceName}: {pwrText}";
                _lblStopwatchSub.ForeColor = Color.FromArgb(52, 211, 153);
            }
            else if (armedCount > 0)
            {
                _lblStopwatchSub.Text = $"⚡ {Loc.Get("StopwatchArmed")} (> {_settings.StopwatchAutoStartWatts}W) | {sourceName}: {pwrText}";
                _lblStopwatchSub.ForeColor = Color.FromArgb(245, 158, 11);
            }
            else
            {
                _lblStopwatchSub.Text = $"{Loc.Get("StopwatchStopped")} | {sourceName}: {pwrText}";
                _lblStopwatchSub.ForeColor = Color.FromArgb(165, 165, 180);
            }
        }

        private void DrawStopwatchCard(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int w = _pnlStopwatch.Width;
            if (w <= 10) return;

            var items = _hardware.Stopwatch.Items.Where(i => i.Profile.IsVisible).ToList();
            if (items.Count == 0) return;

            int swRowH = (int)Math.Round(50 * _currentUiScale);
            int swGap = (int)Math.Round(4 * _currentUiScale);

            using var fontTime = new Font("Consolas", (float)Math.Max(7.5, Math.Round(13.0f * _currentUiScale, 1)), FontStyle.Bold);
            using var fontSub = new Font("Segoe UI", (float)Math.Max(4.8, Math.Round(7.5f * _currentUiScale, 1)));
            using var borderPen = new Pen(Color.FromArgb(36, 36, 46), 1f);

            int actionBtnW = (int)Math.Max(18, Math.Round(30 * _currentUiScale));
            int scaleBtnW = (int)Math.Max(26, Math.Round(44 * _currentUiScale));
            int btnGap = (int)Math.Max(2, Math.Round(3 * _currentUiScale));
            int totalButtonsW = (actionBtnW * 4) + (btnGap * 4) + scaleBtnW;
            int startX = Math.Max((int)Math.Round(95 * _currentUiScale), w - totalButtonsW - (int)Math.Round(4 * _currentUiScale));
            int maxTextW = Math.Max(30, startX - (int)Math.Round(14 * _currentUiScale));

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                int rowY = i * (swRowH + swGap);

                // Row background border
                g.DrawRectangle(borderPen, 0, rowY, w - 1, swRowH - 1);

                // Left color accent bar
                using (var colorBrush = new SolidBrush(item.AccentColor))
                {
                    g.FillRectangle(colorBrush, 2, rowY + (int)Math.Round(6 * _currentUiScale), 3, swRowH - (int)Math.Round(12 * _currentUiScale));
                }

                // Time String
                string timeStr = item.FormattedTime;

                Color digitColor;
                if (item.IsRunning)
                {
                    digitColor = item.AccentColor;
                }
                else if (item.IsAutoArmed)
                {
                    digitColor = Color.FromArgb(245, 158, 11); // Amber
                }
                else if (item.Elapsed > TimeSpan.Zero)
                {
                    digitColor = Color.FromArgb(240, 240, 248); // Crisp white
                }
                else
                {
                    digitColor = Color.FromArgb(145, 145, 160); // Muted silver
                }

                if (item.IsRunning)
                {
                    using var glowBrush = new SolidBrush(Color.FromArgb(45, digitColor.R, digitColor.G, digitColor.B));
                    g.DrawString(timeStr, fontTime, glowBrush, (int)Math.Round(11 * _currentUiScale), rowY + (int)Math.Round(5 * _currentUiScale));
                }

                using (var brush = new SolidBrush(digitColor))
                {
                    g.DrawString(timeStr, fontTime, brush, (int)Math.Round(10 * _currentUiScale), rowY + (int)Math.Round(4 * _currentUiScale));
                }

                // Subtitle: Target Program / Process Name (clean text, no broken glyphs)
                string targetDisplay;
                if (!string.IsNullOrWhiteSpace(item.Profile.TargetFriendlyName))
                {
                    targetDisplay = item.Profile.TargetFriendlyName;
                }
                else if (!string.IsNullOrWhiteSpace(item.Profile.TargetExe))
                {
                    targetDisplay = item.Profile.TargetExe;
                }
                else
                {
                    targetDisplay = Loc.Get("StopwatchAnyProgram");
                }

                string appDesc;
                if (!string.IsNullOrWhiteSpace(item.Profile.Name) &&
                    !item.Profile.Name.StartsWith("Stopwatch", StringComparison.OrdinalIgnoreCase) &&
                    !item.Profile.Name.StartsWith("Секундомер", StringComparison.OrdinalIgnoreCase) &&
                    !item.Profile.Name.Equals(targetDisplay, StringComparison.OrdinalIgnoreCase) &&
                    !item.Profile.Name.Equals(item.Profile.TargetExe, StringComparison.OrdinalIgnoreCase))
                {
                    appDesc = $"{item.Profile.Name} • {targetDisplay}";
                }
                else
                {
                    appDesc = targetDisplay;
                }

                // Clip subtitle before buttons
                var subRect = new RectangleF((int)Math.Round(10 * _currentUiScale), rowY + (int)Math.Round(26 * _currentUiScale), maxTextW, (int)Math.Round(18 * _currentUiScale));
                using (var subBrush = new SolidBrush(Color.FromArgb(56, 189, 248)))
                using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                {
                    g.DrawString(appDesc, fontSub, subBrush, subRect, sf);
                }
            }
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
            public float UiScale { get; set; } = 1.0f;

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

                using var font = new Font("Segoe UI", (float)Math.Max(4.8, Math.Round(8.0f * UiScale, 1)));

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
                        int sqSize = (int)Math.Max(5, Math.Round(8 * UiScale));
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

        private class DiskLegendPanel : Panel
        {
            private readonly HardwareMonitor _hardware;
            private readonly AppSettings _settings;
            private readonly Action _onDiskToggled;
            private readonly Action<bool> _onSelectionChanged;
            private int _hoveredIndex = -1;
            private int _selectedDriveIndex = -1;

            public int ExpandedExtraHeight => (_selectedDriveIndex >= 0 && _selectedDriveIndex < _hardware.Disk.Drives.Count) ? 84 : 0;
            public float UiScale { get; set; } = 1.0f;

            public DiskLegendPanel(HardwareMonitor hardware, AppSettings settings, Action onDiskToggled, Action<bool> onSelectionChanged)
            {
                _hardware = hardware;
                _settings = settings;
                _onDiskToggled = onDiskToggled;
                _onSelectionChanged = onSelectionChanged;
                this.DoubleBuffered = true;
                this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
                this.AutoScroll = true;
                this.BackColor = Color.FromArgb(14, 16, 22);
            }

            public void ResetSelection()
            {
                if (_selectedDriveIndex != -1)
                {
                    _selectedDriveIndex = -1;
                    _onSelectionChanged(false);
                }
            }

            public int GetRowY(int index)
            {
                int baseRow = index * 22 + 2;
                if (_selectedDriveIndex >= 0 && index > _selectedDriveIndex)
                {
                    return baseRow + 84;
                }
                return baseRow;
            }

            public Rectangle GetExpandedCardRect()
            {
                if (_selectedDriveIndex < 0 || _selectedDriveIndex >= _hardware.Disk.Drives.Count)
                    return Rectangle.Empty;
                int y = GetRowY(_selectedDriveIndex) + 22;
                int w = this.ClientSize.Width;
                return new Rectangle(4, y, Math.Max(10, w - 8), 80);
            }

            public Rectangle GetResmonButtonRect(Rectangle cardRect)
            {
                return new Rectangle(cardRect.Right - 74, cardRect.Y + 2, 70, 16);
            }

            public int GetRowIndexAt(int y)
            {
                var drives = _hardware.Disk.Drives;
                for (int i = 0; i < drives.Count; i++)
                {
                    int rY = GetRowY(i);
                    if (y >= rY && y < rY + 22)
                        return i;
                }
                return -1;
            }

            public void UpdateLayout()
            {
                int count = _hardware.Disk.Drives.Count;
                int totalH = count * 22 + 4 + ExpandedExtraHeight;
                if (this.Height >= totalH)
                {
                    this.AutoScroll = false;
                    this.AutoScrollMinSize = Size.Empty;
                }
                else
                {
                    this.AutoScroll = true;
                    if (this.AutoScrollMinSize.Height != totalH)
                    {
                        this.AutoScrollMinSize = new Size(0, totalH);
                    }
                }
                this.Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                g.TranslateTransform(this.AutoScrollPosition.X, this.AutoScrollPosition.Y);

                var drives = _hardware.Disk.Drives;
                int rowH = 22;
                int w = this.ClientSize.Width;

                using var fontBold = new Font("Segoe UI", (float)Math.Max(4.8, Math.Round(7.5f * UiScale, 1)), FontStyle.Bold);
                using var fontRegular = new Font("Segoe UI", (float)Math.Max(4.8, Math.Round(7.5f * UiScale, 1)));

                for (int i = 0; i < drives.Count; i++)
                {
                    var drive = drives[i];
                    int rowY = GetRowY(i);
                    bool isVisible = _settings.IsDiskVisible(drive.Id, drive.InstanceName);
                    bool isHover = (i == _hoveredIndex);
                    bool isSelected = (i == _selectedDriveIndex);

                    if (isSelected)
                    {
                        using var sBrush = new SolidBrush(Color.FromArgb(24, 28, 40));
                        g.FillRectangle(sBrush, 0, rowY, w, rowH - 1);
                    }
                    else if (isHover)
                    {
                        using var hBrush = new SolidBrush(Color.FromArgb(28, 32, 44));
                        g.FillRectangle(hBrush, 0, rowY, w, rowH - 1);
                    }

                    // 1. Toggle checkbox indicator [✓] / [ ]
                    int chkX = 4;
                    int chkY = rowY + 3;
                    var chkRect = new Rectangle(chkX, chkY, 14, 14);

                    if (isVisible)
                    {
                        using var chkBg = new SolidBrush(Color.FromArgb(50, drive.Color.R, drive.Color.G, drive.Color.B));
                        using var chkPen = new Pen(drive.Color, 1.2f);
                        g.FillRectangle(chkBg, chkRect);
                        g.DrawRectangle(chkPen, chkRect);

                        TextRenderer.DrawText(g, "✓", fontBold, new Rectangle(chkX - 1, chkY - 2, 16, 16),
                            drive.Color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    }
                    else
                    {
                        using var chkPen = new Pen(Color.FromArgb(70, 75, 90), 1f);
                        g.DrawRectangle(chkPen, chkRect);
                    }

                    // 2. Color dot
                    int dotX = chkX + 18;
                    int dotY = rowY + 6;
                    using (var dotBrush = new SolidBrush(isVisible ? drive.Color : Color.FromArgb(70, 75, 90)))
                    {
                        g.FillEllipse(dotBrush, dotX, dotY, 8, 8);
                    }

                    // 3. Letters badge: [C:] or [C:, D:]
                    int ltrX = dotX + 13;
                    string ltrText = !string.IsNullOrEmpty(drive.DriveLetters) ? drive.DriveLetters : $"#{drive.PhysicalIndex}";
                    Size ltrSz = TextRenderer.MeasureText(g, ltrText, fontBold, Size.Empty, TextFormatFlags.NoPadding);

                    using (var ltrBg = new SolidBrush(isSelected ? Color.FromArgb(46, 52, 70) : Color.FromArgb(36, 40, 54)))
                    {
                        g.FillRectangle(ltrBg, ltrX - 2, rowY + 2, ltrSz.Width + 4, 16);
                    }
                    TextRenderer.DrawText(g, ltrText, fontBold, new Point(ltrX, rowY + 3),
                        isVisible ? Color.FromArgb(56, 189, 248) : Color.FromArgb(120, 130, 145), TextFormatFlags.NoPadding);

                    // 4. Metrics on right: "12% 45.2M"
                    string metricsText = isVisible ? $"{drive.LoadPercent:F0}%  {drive.CurrentMbPerSec:F1}M" : "OFF";
                    Size metSz = TextRenderer.MeasureText(g, metricsText, fontBold, Size.Empty, TextFormatFlags.NoPadding);
                    int metX = w - metSz.Width - 6;
                    TextRenderer.DrawText(g, metricsText, fontBold, new Point(metX, rowY + 3),
                        isVisible && drive.LoadPercent > 0.5f ? drive.Color : Color.FromArgb(110, 115, 130), TextFormatFlags.NoPadding);

                    // 5. Model / Manufacturer in middle
                    int modelX = ltrX + ltrSz.Width + 8;
                    int modelMaxW = Math.Max(10, metX - modelX - 6);

                    string brand = !string.IsNullOrEmpty(drive.Manufacturer) && !drive.Model.StartsWith(drive.Manufacturer, StringComparison.OrdinalIgnoreCase)
                        ? $"{drive.Manufacturer} " : "";
                    string fullModel = $"{brand}{drive.Model}".Trim();
                    if (string.IsNullOrEmpty(fullModel)) fullModel = drive.InstanceName;

                    Color textColor = isVisible ? Color.FromArgb(220, 225, 235) : Color.FromArgb(100, 105, 120);
                    TextRenderer.DrawText(g, fullModel, fontRegular, new Rectangle(modelX, rowY + 3, modelMaxW, 16),
                        textColor, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
                }

                // Draw expanded top processes card if a drive is selected
                if (_selectedDriveIndex >= 0 && _selectedDriveIndex < drives.Count)
                {
                    var selectedDrive = drives[_selectedDriveIndex];
                    var cardRect = GetExpandedCardRect();
                    if (cardRect.Width > 20 && cardRect.Height > 20)
                    {
                        using var cardBg = new SolidBrush(Color.FromArgb(16, 18, 25));
                        using var cardBorder = new Pen(Color.FromArgb(45, 52, 68), 1f);
                        using var accentBrush = new SolidBrush(selectedDrive.Color);

                        g.FillRectangle(cardBg, cardRect);
                        g.DrawRectangle(cardBorder, cardRect);
                        g.FillRectangle(accentBrush, cardRect.X, cardRect.Y, 3, cardRect.Height);

                        string ltr = !string.IsNullOrEmpty(selectedDrive.DriveLetters) ? selectedDrive.DriveLetters : $"#{selectedDrive.PhysicalIndex}";
                        string headerTitle = $"{Loc.Get("DiskProcessesTitle")} [{ltr}]";
                        using var fontHeader = new Font("Segoe UI", (float)Math.Max(4.8, Math.Round(7.5f * UiScale, 1)), FontStyle.Bold);
                        TextRenderer.DrawText(g, headerTitle, fontHeader, new Point(cardRect.X + 8, cardRect.Y + 3),
                            Color.FromArgb(56, 189, 248), TextFormatFlags.NoPadding);

                        var resmonRect = GetResmonButtonRect(cardRect);
                        using var resmonBg = new SolidBrush(Color.FromArgb(26, 30, 42));
                        using var resmonPen = new Pen(Color.FromArgb(60, 70, 90), 1f);
                        g.FillRectangle(resmonBg, resmonRect);
                        g.DrawRectangle(resmonPen, resmonRect);
                        using var fontResmon = new Font("Segoe UI", (float)Math.Max(4.8, Math.Round(7.0f * UiScale, 1)));
                        TextRenderer.DrawText(g, Loc.Get("ResmonBtn"), fontResmon, resmonRect,
                            Color.FromArgb(180, 190, 210), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                        var topIo = _hardware.Processes.TopDiskProcesses;
                        if (topIo.Count > 0)
                        {
                            int maxP = Math.Min(3, topIo.Count);
                            using var fontProc = new Font("Segoe UI", (float)Math.Max(4.8, Math.Round(7.2f * UiScale, 1)));
                            using var fontBoldMetrics = new Font("Segoe UI", (float)Math.Max(4.8, Math.Round(7.2f * UiScale, 1)), FontStyle.Bold);

                            for (int p = 0; p < maxP; p++)
                            {
                                var proc = topIo[p];
                                int py = cardRect.Y + 22 + (p * 18);

                                int px = cardRect.X + 8;
                                if (proc.Icon != null)
                                {
                                    g.DrawImage(proc.Icon, new Rectangle(px, py + 1, 14, 14));
                                    px += 18;
                                }

                                string pName = !string.IsNullOrEmpty(proc.FriendlyName) ? proc.FriendlyName : proc.ExeName;
                                bool matchesDrive = !string.IsNullOrEmpty(selectedDrive.DriveLetters) &&
                                                    !string.IsNullOrEmpty(proc.ExePath) &&
                                                    proc.ExePath.StartsWith(selectedDrive.DriveLetters, StringComparison.OrdinalIgnoreCase);

                                string totalStr = $"{proc.TotalDiskMbPerSec:F1} MB/s";
                                string rwStr = $"R:{proc.ReadMbPerSec:F1}M W:{proc.WriteMbPerSec:F1}M";

                                Size totalSz = TextRenderer.MeasureText(g, totalStr, fontBoldMetrics, Size.Empty, TextFormatFlags.NoPadding);
                                Size rwSz = TextRenderer.MeasureText(g, rwStr, fontProc, Size.Empty, TextFormatFlags.NoPadding);

                                int totX = cardRect.Right - totalSz.Width - 6;
                                int rwX = totX - rwSz.Width - 6;
                                int nameMaxW = Math.Max(10, rwX - px - 4);

                                TextRenderer.DrawText(g, pName, fontProc, new Rectangle(px, py, nameMaxW, 16),
                                    matchesDrive ? selectedDrive.Color : Color.FromArgb(220, 225, 235),
                                    TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);

                                TextRenderer.DrawText(g, rwStr, fontProc, new Point(rwX, py + 2),
                                    Color.FromArgb(140, 150, 170), TextFormatFlags.NoPadding);

                                TextRenderer.DrawText(g, totalStr, fontBoldMetrics, new Point(totX, py + 2),
                                    proc.TotalDiskMbPerSec > 1.0 ? selectedDrive.Color : Color.FromArgb(180, 185, 200), TextFormatFlags.NoPadding);
                            }
                        }
                        else
                        {
                            using var fontIdle = new Font("Segoe UI", 7.2f, FontStyle.Italic);
                            TextRenderer.DrawText(g, Loc.Get("DiskProcessesIdle"), fontIdle,
                                new Rectangle(cardRect.X + 8, cardRect.Y + 30, cardRect.Width - 16, 24),
                                Color.FromArgb(120, 125, 140), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                        }
                    }
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int virtY = e.Y - this.AutoScrollPosition.Y;
                if (_selectedDriveIndex >= 0)
                {
                    var cardRect = GetExpandedCardRect();
                    if (cardRect.Contains(e.X, virtY))
                    {
                        var resmonRect = GetResmonButtonRect(cardRect);
                        this.Cursor = resmonRect.Contains(e.X, virtY) ? Cursors.Hand : Cursors.Default;
                        if (_hoveredIndex != -1)
                        {
                            _hoveredIndex = -1;
                            this.Invalidate();
                        }
                        return;
                    }
                }

                int newIndex = GetRowIndexAt(virtY);
                var drives = _hardware.Disk.Drives;
                if (newIndex >= 0 && newIndex < drives.Count)
                {
                    this.Cursor = Cursors.Hand;
                    if (newIndex != _hoveredIndex)
                    {
                        _hoveredIndex = newIndex;
                        this.Invalidate();
                    }
                }
                else
                {
                    this.Cursor = Cursors.Default;
                    if (_hoveredIndex != -1)
                    {
                        _hoveredIndex = -1;
                        this.Invalidate();
                    }
                }
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                this.Cursor = Cursors.Default;
                _hoveredIndex = -1;
                this.Invalidate();
            }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                base.OnMouseClick(e);
                int virtY = e.Y - this.AutoScrollPosition.Y;
                if (_selectedDriveIndex >= 0)
                {
                    var cardRect = GetExpandedCardRect();
                    if (cardRect.Contains(e.X, virtY))
                    {
                        var resmonRect = GetResmonButtonRect(cardRect);
                        if (resmonRect.Contains(e.X, virtY))
                        {
                            try
                            {
                                Process.Start(new ProcessStartInfo("resmon.exe", "/res disk") { UseShellExecute = true });
                            }
                            catch { }
                        }
                        return;
                    }
                }

                int index = GetRowIndexAt(virtY);
                var drives = _hardware.Disk.Drives;
                if (index >= 0 && index < drives.Count)
                {
                    var d = drives[index];
                    if (e.X <= 22)
                    {
                        // Toggle disk line on graph
                        bool cur = _settings.IsDiskVisible(d.Id, d.InstanceName);
                        _settings.SetDiskVisibility(d.Id, !cur);
                        _settings.Save();
                        _onDiskToggled();
                        this.Invalidate();
                    }
                    else
                    {
                        // Toggle top disk processes for this drive
                        if (_selectedDriveIndex == index)
                        {
                            _selectedDriveIndex = -1;
                            _onSelectionChanged(false);
                        }
                        else
                        {
                            bool wasExpanded = (_selectedDriveIndex >= 0);
                            _selectedDriveIndex = index;
                            if (!wasExpanded)
                            {
                                _onSelectionChanged(true);
                            }
                            else
                            {
                                this.Invalidate();
                            }
                        }
                    }
                }
            }
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

            private bool _isHovered = false;

            public ModuleSeparator(Color accentColor)
            {
                _accentColor = accentColor;
                this.DoubleBuffered = true;
                this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
                this.Height = 3;
                this.Cursor = Cursors.Hand;
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                base.OnMouseEnter(e);
                _isHovered = true;
                this.Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                _isHovered = false;
                this.Invalidate();
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

                // 2. Accent color line of the block (glow on hover)
                Color lineColor = _isHovered ? ControlPaint.Light(_accentColor, 0.35f) : _accentColor;
                using (var accentPen = new Pen(lineColor, _isHovered ? 2.0f : 1.5f))
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
            if (!_isDetached && !_alwaysOnTop && this.Visible)
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
            if (_btnCpuProcessesExpand != null)
            {
                _btnCpuProcessesExpand.ForeColor = _isQuietMode ? Color.FromArgb(80, 220, 140) : Color.FromArgb(250, 180, 80);
            }
            UpdateModeUI();
        }

        public void SetStartupState(bool enabled)
        {
            _isStartupEnabled = enabled;
        }

        public void SyncTopProcessesSetting()
        {
            _hardware.Processes.IsEnabled = _settings.ShowTopProcesses || (_settings.ShowDiskGraph && _settings.EnhancedDiskMode);
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
            if (_pnlCpuGraph != null) _toolTip.SetToolTip(_pnlCpuGraph, Loc.Get("CpuGraphScaleTip"));
            if (_btnGear != null) _toolTip.SetToolTip(_btnGear, Loc.Get("MenuSettings"));
            if (_btnSwDashboardAdd != null) _toolTip.SetToolTip(_btnSwDashboardAdd, Loc.Get("StopwatchAdd"));
            foreach (var rc in _stopwatchRowControls)
            {
                _toolTip.SetToolTip(rc.BtnStart, Loc.Get("StopwatchStart"));
                _toolTip.SetToolTip(rc.BtnAuto, Loc.Get("StopwatchAutoStartEnable"));
                _toolTip.SetToolTip(rc.BtnStop, Loc.Get("StopwatchStop"));
                _toolTip.SetToolTip(rc.BtnReset, Loc.Get("StopwatchReset"));
                _toolTip.SetToolTip(rc.BtnScale, Loc.Get("StopwatchScaleTip"));
            }
            if (_btnCpuProcessesExpand != null)
            {
                _toolTip.SetToolTip(_btnCpuProcessesExpand, _settings.ShowTopProcesses ? Loc.Get("ProcessesCollapse") : Loc.Get("ProcessesExpand"));
            }
            if (_lblHddStatus != null && _settings.HddScheduleEnabled)
            {
                _toolTip.SetToolTip(_lblHddStatus, $"{Loc.Get("HddScheduleModule")}: {_hardware.HddSchedule.GetStatusString()}");
            }

            string sectionTip = Loc.Get("SectionToggleCollapse");
            if (_lblStopwatch != null) _toolTip.SetToolTip(_lblStopwatch, sectionTip);
            if (_sepStopwatch != null) _toolTip.SetToolTip(_sepStopwatch, sectionTip);
            if (_lblCpu != null) _toolTip.SetToolTip(_lblCpu, sectionTip);
            if (_sepCpu != null) _toolTip.SetToolTip(_sepCpu, sectionTip);
            if (_lblRam != null) _toolTip.SetToolTip(_lblRam, sectionTip);
            if (_sepRam != null) _toolTip.SetToolTip(_sepRam, sectionTip);
            if (_lblGpu != null) _toolTip.SetToolTip(_lblGpu, sectionTip);
            if (_sepGpu != null) _toolTip.SetToolTip(_sepGpu, sectionTip);
            if (_lblVram != null) _toolTip.SetToolTip(_lblVram, sectionTip);
            if (_sepVram != null) _toolTip.SetToolTip(_sepVram, sectionTip);
            if (_lblDisk != null) _toolTip.SetToolTip(_lblDisk, sectionTip);
            if (_sepDisk != null) _toolTip.SetToolTip(_sepDisk, sectionTip);
            if (_lblFans != null) _toolTip.SetToolTip(_lblFans, sectionTip);
            if (_sepFans != null) _toolTip.SetToolTip(_sepFans, sectionTip);
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
            if (_lblTitle != null) _lblTitle.Text = Loc.Get("AppTitle");
            if (_chkShowAllGpus != null) _chkShowAllGpus.Text = Loc.Get("ShowAllGpus");
            if (_lblStopwatch != null) _lblStopwatch.Text = (_settings.SectionStopwatchCollapsed ? "▸ " : "▾ ") + "⏱ " + Loc.Get("Stopwatch");
            foreach (var rc in _stopwatchRowControls)
            {
                rc.BtnStart.Text = Loc.Get("StopwatchStart");
                rc.BtnAuto.Text = Loc.Get("StopwatchAuto");
                rc.BtnStop.Text = Loc.Get("StopwatchStop");
                rc.BtnReset.Text = Loc.Get("StopwatchReset");
            }
            UpdateStopwatchUI();
            UpdateStopwatchSubHeader();
            UpdateMetricsUI();
            LayoutComponents();
        }

        public void ShowDashboard()
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.WindowState = FormWindowState.Normal;
            }

            if (_isDetached && (_settings.WindowX != -1 || _settings.WindowY != -1))
            {
                var targetRect = new Rectangle(_settings.WindowX, _settings.WindowY, this.Width, this.Height);
                Screen? targetScreen = null;
                foreach (var s in Screen.AllScreens)
                {
                    if (s.WorkingArea.IntersectsWith(targetRect))
                    {
                        targetScreen = s;
                        break;
                    }
                }

                if (targetScreen != null)
                {
                    if (_settings.AutoUiScale)
                    {
                        float targetScale = GetEffectiveUiScale(targetScreen);
                        if (Math.Abs(targetScale - _currentUiScale) > 0.01f)
                        {
                            _currentUiScale = targetScale;
                            ApplyUiScale(true);
                        }
                    }

                    var scr = targetScreen;
                    if (this.Width > scr.WorkingArea.Width)
                    {
                        this.Width = scr.WorkingArea.Width;
                    }
                    if (this.Height > scr.WorkingArea.Height)
                    {
                        this.Height = scr.WorkingArea.Height;
                    }

                    int minX = scr.WorkingArea.Left;
                    int maxX = Math.Max(minX, scr.WorkingArea.Right - this.Width);
                    int x = Math.Clamp(_settings.WindowX, minX, maxX);

                    int minY = scr.WorkingArea.Top;
                    int maxY = Math.Max(minY, scr.WorkingArea.Bottom - this.Height);
                    int y = Math.Clamp(_settings.WindowY, minY, maxY);
                    this.Location = new Point(x, y);
                }
                else
                {
                    PositionAtTray();
                }
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
            var curScreen = Screen.PrimaryScreen;
            if (_settings.AutoUiScale && curScreen != null)
            {
                float targetScale = GetEffectiveUiScale(curScreen);
                if (Math.Abs(targetScale - _currentUiScale) > 0.01f)
                {
                    _currentUiScale = targetScale;
                    ApplyUiScale(true);
                }
            }
            var workingArea = curScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
            if (this.Width > workingArea.Width)
            {
                this.Width = workingArea.Width;
            }
            if (this.Height > workingArea.Height)
            {
                this.Height = workingArea.Height;
            }
            int posX = Math.Max(workingArea.Left, workingArea.Right - this.Width); // 0px margin from right edge
            int posY = Math.Max(workingArea.Top, workingArea.Bottom - this.Height - 6); // 6px gap from taskbar
            this.Location = new Point(posX, posY);
        }

        private void UpdateMetricsUI()
        {
            // 0. Stopwatch
            if (_settings.ShowStopwatch)
            {
                if (_lblStopwatch != null)
                {
                    _lblStopwatch.Text = $"{(_settings.SectionStopwatchCollapsed ? "▸ " : "▾ ")}⏱ {Loc.Get("Stopwatch")}";
                }
                UpdateStopwatchSubHeader();
                _pnlStopwatch?.Invalidate();
            }

            // 1. CPU
            float cpuUsage = _hardware.Cpu.TotalCpuUsage;
            float freqGhz = _hardware.Cpu.CurrentFrequencyMHz / 1000f;
            string cpuArrow = _settings.SectionCpuCollapsed ? "▸ " : "▾ ";
            _lblCpu.Text = $"{cpuArrow}{Loc.Get("Cpu")}: {cpuUsage:F0}%";
            _lblCpu.ForeColor = _isQuietMode ? Color.FromArgb(80, 220, 140) : Color.FromArgb(250, 180, 80);
            string cpuName = _hardware.Cpu.CpuShortName;
            string cpuCoreInfo = (freqGhz > 0.5f)
                ? $"{freqGhz:F2} GHz | {_hardware.Cpu.CoreCount} {Loc.Get("Threads")}"
                : $"{_hardware.Cpu.CoreCount} {Loc.Get("ThreadsActive")}";
            string cpuText = !string.IsNullOrEmpty(cpuName) ? $"{cpuName} | {cpuCoreInfo}" : cpuCoreInfo;
            if (_hardware.Fans.IsConnected && !_hardware.Fans.DemoMode)
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
            string ramArrow = _settings.SectionRamCollapsed ? "▸ " : "▾ ";
            _lblRam.Text = $"{ramArrow}{Loc.Get("Ram")}: {ramUsage:F0}%";
            string ramSpecs = !string.IsNullOrEmpty(_hardware.Ram.MemorySpecs) ? $"{_hardware.Ram.MemorySpecs} | " : "";
            _lblRamSub.Text = $"{ramSpecs}{_hardware.Ram.UsedGb:F1} / {_hardware.Ram.TotalGb:F1} GB";

            // 3. GPU
            string gpuArrow = _settings.SectionGpuCollapsed ? "▸ " : "▾ ";
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
                _lblGpu.Text = $"{gpuArrow}{Loc.Get("Gpu")}: {avgGpu:F0}%";
                _pnlGpuSub.IsMultiGpu = true;
                _pnlGpuSub.MultiGpuText = gpuDetail;
                _pnlGpuSub.Invalidate();

                // 4. VRAM
                string vramArrow = _settings.SectionVramCollapsed ? "▸ " : "▾ ";
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
                _lblVram.Text = $"{vramArrow}{Loc.Get("Vram")}: {totalVramPercent:F0}%";
                _lblVramSub.Text = $"{vramType} | {totalUsedGb:F1}/{totalTotalGb:F0} GB  ({vramDetail})";
            }
            else
            {
                float gpuUsage = _hardware.Gpu.GpuLoadPercent;
                _lblGpu.Text = $"{gpuArrow}{Loc.Get("Gpu")}: {gpuUsage:F0}%";

                int gpuFanRpm = -1;
                if (_hardware.Fans.IsConnected && !_hardware.Fans.DemoMode)
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
                string vramArrow = _settings.SectionVramCollapsed ? "▸ " : "▾ ";
                float vramUsage = _hardware.Gpu.VramLoadPercent;
                string vramType = _hardware.Gpu.VramType;
                _lblVram.Text = $"{vramArrow}{Loc.Get("Vram")}: {vramUsage:F0}%";
                _lblVramSub.Text = $"{vramType} | {_hardware.Gpu.VramUsedGb:F1} / {_hardware.Gpu.VramTotalGb:F1} GB";
            }

            // 5. DISK
            string diskArrow = _settings.SectionDiskCollapsed ? "▸ " : "▾ ";
            _lblDisk.Text = $"{diskArrow}{Loc.Get("Disk")}";
            string diskIdleText = (_hardware.Disk.PeakActiveDisk == "Idle") ? Loc.Get("Idle") : _hardware.Disk.PeakActiveDisk;
            _lblDiskSub.Text = $"R:{_hardware.Disk.ReadMbPerSec:F1} W:{_hardware.Disk.WriteMbPerSec:F1} MB/s [{diskIdleText}]";
            if (_settings.HddScheduleEnabled)
            {
                bool isAwake = _hardware.HddSchedule.IsDisksAwake;
                _lblHddStatus.ForeColor = isAwake ? Color.FromArgb(52, 211, 153) : Color.FromArgb(120, 120, 135);
                string tip = $"{Loc.Get("HddScheduleModule")}: {_hardware.HddSchedule.GetStatusString()}";
                _toolTip.SetToolTip(_lblHddStatus, tip);
                _toolTip.SetToolTip(_lblDiskSub, tip);
                _toolTip.SetToolTip(_lblDisk, tip);
            }

            // 6. FANS
            if (_settings.ShowFanGraph)
            {
                string fansArrow = _settings.SectionFansCollapsed ? "▸ " : "▾ ";
                var fans = _hardware.Fans.Fans;
                if (_hardware.Fans.IsConnected && fans.Count > 0)
                {
                    if (_hardware.Fans.DemoMode)
                    {
                        _lblFans.Text = $"{fansArrow}{Loc.Get("Fans")} [DEMO]";
                        _lblFans.ForeColor = Color.FromArgb(245, 158, 11);
                        _lblFansSub.Text = Loc.Get("FanDemoModeHint");
                    }
                    else
                    {
                        _lblFans.Text = $"{fansArrow}{Loc.Get("Fans")}";
                        _lblFans.ForeColor = Color.FromArgb(14, 165, 233);
                        if (_settings.SectionFansCollapsed)
                        {
                            var visibleFans = fans.Where(f => _settings.IsFanVisible(f.Id, f.Name)).ToList();
                            if (visibleFans.Count > 0)
                            {
                                int maxRpm = visibleFans.Max(f => f.CurrentRpm);
                                _lblFansSub.Text = $"{visibleFans.Count} {Loc.Get("FansCountSuffix")} | Max: {maxRpm:N0} RPM";
                            }
                            else
                            {
                                _lblFansSub.Text = "";
                            }
                        }
                        else
                        {
                            _lblFansSub.Text = "";
                        }
                    }
                }
                else
                {
                    _lblFans.Text = $"{fansArrow}{Loc.Get("Fans")}: --";
                    _lblFans.ForeColor = Color.FromArgb(14, 165, 233);
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
            if (_pnlDiskLegend.Visible) _pnlDiskLegend.Invalidate();
            if (_pnlFansGraph.Visible) _pnlFansGraph.Invalidate();
        }

        private void DrawGraphInternal(Graphics g, Panel pnl, IReadOnlyList<float> history, Color strokeColor, Color topFill, float maxPercent = 100f)
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
                float val = Math.Clamp(history[i], 0f, maxPercent);
                float x = i * stepX;
                float norm = Math.Clamp(val / maxPercent, 0f, 1f);
                float y = h - (h * norm);
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

        private float GetCpuScaleMaxPercent(int scale)
        {
            if (scale == 2) return 50f;
            if (scale == 4) return 25f;
            if (scale == 6) return 16.67f;
            if (scale == 8) return 12.5f;
            if (scale == 0) // Auto scale
            {
                float peak = _hardware.Cpu.History.Count > 0 ? _hardware.Cpu.History.Max() : 10f;
                foreach (var sw in _hardware.Stopwatch.Items)
                {
                    if (sw.Profile.ShowOnCpuGraph && sw.HasRecordedHistory)
                    {
                        var snap = sw.GetCpuHistorySnapshot();
                        foreach (var v in snap)
                        {
                            if (v.HasValue && v.Value > peak) peak = v.Value;
                        }
                    }
                }
                float autoMax = Math.Max(10f, (float)Math.Ceiling((peak * 1.15f) / 5f) * 5f);
                return Math.Min(100f, autoMax);
            }
            return 100f; // 1x
        }

        private void ToggleCpuGraphScale()
        {
            int next = _settings.CpuGraphScale switch
            {
                1 => 2,
                2 => 4,
                4 => 6,
                6 => 8,
                8 => 0,
                0 => 1,
                _ => 1
            };
            _settings.CpuGraphScale = next;
            _settings.Save();
            _pnlCpuGraph.Invalidate();
        }

        private void DrawCpuScaleBadge(Graphics g, Panel pnl, float maxPercent)
        {
            int w = pnl.Width;
            int scale = _settings.CpuGraphScale;

            string badgeText = scale switch
            {
                2 => "x2 (50%)",
                4 => "x4 (25%)",
                6 => "x6 (17%)",
                8 => "x8 (12.5%)",
                0 => $"Auto ({maxPercent:F0}%)",
                _ => "1x (100%)"
            };

            float uiScale = _currentUiScale;
            using var font = new Font("Segoe UI", (float)Math.Max(5.0, Math.Round(7.0f * uiScale, 1)), FontStyle.Bold);
            var sz = g.MeasureString(badgeText, font);
            int badgeW = (int)Math.Ceiling(sz.Width) + (int)Math.Round(6 * uiScale);
            int badgeH = (int)Math.Max(10, Math.Round(15 * uiScale));
            int badgeX = w - badgeW - (int)Math.Round(6 * uiScale);
            int badgeY = (int)Math.Round(2 * uiScale);

            var badgeRect = new Rectangle(badgeX, badgeY, badgeW, badgeH);

            Color bgCol = (scale != 1) ? Color.FromArgb(200, 24, 30, 42) : Color.FromArgb(160, 20, 22, 28);
            Color borderCol = (scale != 1) ? Color.FromArgb(56, 189, 248) : Color.FromArgb(50, 55, 68);
            Color textCol = (scale != 1) ? Color.FromArgb(56, 189, 248) : Color.FromArgb(130, 135, 150);

            using (var bgBrush = new SolidBrush(bgCol))
            using (var borderPen = new Pen(borderCol, 1f))
            {
                g.FillRectangle(bgBrush, badgeRect);
                g.DrawRectangle(borderPen, badgeRect);
            }

            using var textBrush = new SolidBrush(textCol);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(badgeText, font, textBrush, badgeRect, sf);
        }

        private void DrawCpuGraph(object? sender, PaintEventArgs e)
        {
            Color stroke = _isQuietMode ? Color.FromArgb(80, 220, 140) : Color.FromArgb(250, 180, 80);
            Color fill = _isQuietMode ? Color.FromArgb(80, 40, 180, 100) : Color.FromArgb(80, 240, 160, 40);
            float maxPercent = GetCpuScaleMaxPercent(_settings.CpuGraphScale);

            DrawGraphInternal(e.Graphics, _pnlCpuGraph, _hardware.Cpu.History, stroke, fill, maxPercent);

            // Overlay curve for stopwatches that have ShowOnCpuGraph enabled and recorded history
            // Stays continuously visible even when stopped!
            foreach (var sw in _hardware.Stopwatch.Items)
            {
                if (sw.Profile.IsVisible && sw.Profile.ShowOnCpuGraph && sw.HasRecordedHistory)
                {
                    DrawStopwatchCpuCurve(e.Graphics, _pnlCpuGraph, sw, maxPercent);
                }
            }

            // Draw Scale Badge in top-right corner
            DrawCpuScaleBadge(e.Graphics, _pnlCpuGraph, maxPercent);
        }

        private void DrawStopwatchCpuCurve(Graphics g, Panel pnl, StopwatchItem sw, float maxPercent)
        {
            int w = pnl.Width;
            int h = pnl.Height;
            if (w <= 1 || h <= 1) return;

            if (!sw.Profile.ShowOnCpuGraph || sw.Profile.GraphScale == -1) return;

            var history = sw.GetCpuHistorySnapshot();
            int totalPoints = _hardware.Cpu.History.Count;
            if (totalPoints < 2 || history.Count == 0) return;

            float stepX = (float)w / (totalPoints - 1);

            // Determine independent scale for this stopwatch curve
            int scale = sw.Profile.GraphScale;
            float effectiveMax;

            if (scale == 0) // Auto: automatically adapt to peak workload of this app
            {
                float peak = 0f;
                foreach (var v in history)
                {
                    if (v.HasValue && v.Value > peak) peak = v.Value;
                }

                // If app is producing load (e.g. 3% in Illustrator),
                // scale so the peak reaches ~75% of the graph height!
                effectiveMax = Math.Max(1.0f, peak * 1.35f);
            }
            else if (scale > 1) // 2x, 4x, 8x, 16x magnification relative to CPU scale
            {
                effectiveMax = Math.Max(0.5f, maxPercent / scale);
            }
            else // 1x: 1:1 with main CPU scale
            {
                effectiveMax = maxPercent;
            }

            // Collect active points from history aligned to totalPoints at the right edge
            var pts = new List<PointF>();
            float? latestVal = null;

            for (int i = 0; i < history.Count; i++)
            {
                int gIdx = (totalPoints - 1) - (history.Count - 1 - i);
                if (gIdx < 0) continue;

                var val = history[i];
                if (val.HasValue)
                {
                    float x = gIdx * stepX;
                    float norm = Math.Clamp(val.Value / effectiveMax, 0f, 1f);
                    float y = h - (h * norm);
                    pts.Add(new PointF(x, y));
                    latestVal = val.Value;
                }
            }

            if (pts.Count < 2)
            {
                if (pts.Count == 1)
                {
                    using var dotBrush = new SolidBrush(sw.AccentColor);
                    g.FillEllipse(dotBrush, pts[0].X - 2, pts[0].Y - 2, 4, 4);
                }
                return;
            }

            // Translucent fill under stopwatch curve
            using (var path = new GraphicsPath())
            {
                path.AddLines(pts.ToArray());
                path.AddLine(pts[^1].X, h, pts[0].X, h);
                path.CloseFigure();

                Color stroke = sw.AccentColor;
                Color topFill = Color.FromArgb(45, stroke.R, stroke.G, stroke.B);
                Color botFill = Color.FromArgb(5, stroke.R, stroke.G, stroke.B);
                using var brush = new LinearGradientBrush(new Point(0, 0), new Point(0, h), topFill, botFill);
                g.FillPath(brush, path);
            }

            // Draw vibrant curve line
            using var pen = new Pen(sw.AccentColor, 2f);
            pen.LineJoin = LineJoin.Round;
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            g.DrawLines(pen, pts.ToArray());

            // Draw live end-point indicator with current % value
            if (latestVal.HasValue && pts.Count > 0)
            {
                var lastPt = pts[^1];
                using var dotBrush = new SolidBrush(sw.AccentColor);
                g.FillEllipse(dotBrush, lastPt.X - 3, lastPt.Y - 3, 6, 6);

                if (latestVal.Value >= 0.1f)
                {
                    string valText = $"{latestVal.Value:F1}%";
                    using var valFont = new Font("Segoe UI", 7.0f, FontStyle.Bold);
                    var valSz = g.MeasureString(valText, valFont);
                    float tagX = Math.Max(2, lastPt.X - valSz.Width - 6);
                    float tagY = Math.Clamp(lastPt.Y - valSz.Height / 2, 2, Math.Max(2, h - valSz.Height - 2));

                    using var tagBgBrush = new SolidBrush(Color.FromArgb(190, 20, 22, 28));
                    g.FillRectangle(tagBgBrush, tagX - 2, tagY - 1, valSz.Width + 4, valSz.Height + 2);

                    using var textBrush = new SolidBrush(sw.AccentColor);
                    g.DrawString(valText, valFont, textBrush, tagX, tagY);
                }
            }
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
            if (_settings.EnhancedDiskMode && _hardware.Disk.Drives.Count > 0)
            {
                DrawMultiDiskGraphInternal(e.Graphics, _pnlDiskGraph);
            }
            else
            {
                Color stroke = Color.FromArgb(6, 182, 212);
                Color fill = Color.FromArgb(60, 6, 182, 212);
                DrawGraphInternal(e.Graphics, _pnlDiskGraph, _hardware.Disk.History, stroke, fill);
            }
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

            float scale = _currentUiScale;
            float fanScale = _settings.FanScale * scale;
            int cardW = (int)Math.Round((76 + 32 * (_settings.FanScale - 1f)) * scale);
            int cardH = Math.Min(h - 4, (int)Math.Round(72 * fanScale));
            int gap = Math.Max(3, (int)Math.Round(6 * fanScale));
            int padX = (int)Math.Max(3, Math.Round(6 * scale));
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
            _fanScrollX = Math.Clamp(_fanScrollX, 0, Math.Max(0, _maxFanScroll));

            var origClip = g.Clip;
            g.SetClip(new Rectangle(0, 0, w, h));

            int startX = padX - _fanScrollX;
            float fontSizeName = (float)Math.Max(4.8, Math.Round(Math.Clamp(7.5f * (float)Math.Sqrt(_settings.FanScale), 7.5f, 11f) * scale, 1));
            float fontSizeRpm = (float)Math.Max(4.8, Math.Round(Math.Clamp(7f * (float)Math.Sqrt(_settings.FanScale), 7f, 10f) * scale, 1));
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
                float fanRadius = Math.Clamp((cardH - (int)Math.Round(26f * scale)) * 0.46f, 10f * scale, 34f * scale);
                float cy = cardY + (int)Math.Round(3f * scale) + fanRadius;

                // Outer shroud
                using (var shroudBg = new SolidBrush(Color.FromArgb(18, 19, 24)))
                {
                    g.FillEllipse(shroudBg, cx - fanRadius, cy - fanRadius, fanRadius * 2, fanRadius * 2);
                }
                using (var shroudPen = new Pen(Color.FromArgb(46, 48, 60), Math.Max(0.8f, 1f * fanScale)))
                {
                    g.DrawEllipse(shroudPen, cx - fanRadius, cy - fanRadius, fanRadius * 2, fanRadius * 2);
                }

                // Rotating blades
                _fanAngles.TryGetValue(f.Id, out float curAngle);
                DrawFanBlades(g, cx, cy, fanRadius, bladeCount, curAngle, tierColor, rpm > 0, fanScale);

                // Central hub cap
                float hubR = Math.Max(2.5f * scale, 4.5f * fanScale);
                using (var hubBrush = new SolidBrush(Color.FromArgb(32, 34, 44)))
                {
                    g.FillEllipse(hubBrush, cx - hubR, cy - hubR, hubR * 2, hubR * 2);
                }
                using (var hubBorder = new Pen(Color.FromArgb(60, 64, 80), Math.Max(0.8f, 1f * fanScale)))
                {
                    g.DrawEllipse(hubBorder, cx - hubR, cy - hubR, hubR * 2, hubR * 2);
                }
                float dotR = Math.Max(1.0f, 1.4f * fanScale);
                using (var dotBrush = new SolidBrush(tierColor))
                {
                    g.FillEllipse(dotBrush, cx - dotR, cy - dotR, dotR * 2, dotR * 2);
                }

                // Legend: Fan Name
                string dispName = FormatFanName(f.Name);
                var szName = g.MeasureString(dispName, fontName);
                float textNameY = cy + fanRadius + Math.Max(2f, 3f * fanScale);
                using (var nameBrush = new SolidBrush(Color.FromArgb(235, 235, 245)))
                {
                    g.DrawString(dispName, fontName, nameBrush, cx - (szName.Width / 2f), textNameY);
                }

                // Legend: RPM
                string rpmText = rpm > 0 ? $"{rpm:N0} RPM" : "0 RPM (0dB)";
                var szRpm = g.MeasureString(rpmText, fontRpm);
                float textRpmY = textNameY + szName.Height + Math.Max(1f, 1f * scale);
                using (var rpmBrush = new SolidBrush(tierColor))
                {
                    g.DrawString(rpmText, fontRpm, rpmBrush, cx - (szRpm.Width / 2f), textRpmY);
                }
            }

            g.Clip = origClip;

            // Horizontal scroll navigation arrows (overlaid on edges)
            if (_maxFanScroll > 0)
            {
                using var arrowFont = new Font("Segoe UI", (float)Math.Max(5.5, Math.Round(9f * scale, 1)), FontStyle.Bold);
                int arrowW = (int)Math.Round(22 * scale);

                // Left arrow
                if (_fanScrollX > 0)
                {
                    using var leftBg = new LinearGradientBrush(
                        new Rectangle(0, 0, arrowW, h),
                        Color.FromArgb(220, 16, 16, 20),
                        Color.FromArgb(0, 16, 16, 20),
                        LinearGradientMode.Horizontal);
                    g.FillRectangle(leftBg, 0, 0, arrowW, h);

                    using var arrowBrush = new SolidBrush(Color.FromArgb(56, 189, 248));
                    g.DrawString("◀", arrowFont, arrowBrush, 2, (h - (int)Math.Round(18 * scale)) / 2);
                }

                // Right arrow
                if (_fanScrollX < _maxFanScroll)
                {
                    using var rightBg = new LinearGradientBrush(
                        new Rectangle(w - arrowW, 0, arrowW, h),
                        Color.FromArgb(0, 16, 16, 20),
                        Color.FromArgb(220, 16, 16, 20),
                        LinearGradientMode.Horizontal);
                    g.FillRectangle(rightBg, w - arrowW, 0, arrowW, h);

                    using var arrowBrush = new SolidBrush(Color.FromArgb(56, 189, 248));
                    g.DrawString("▶", arrowFont, arrowBrush, w - arrowW + (int)Math.Round(8 * scale), (h - (int)Math.Round(18 * scale)) / 2);
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
                float emptyFontSz = (float)Math.Max(5.0, Math.Round(8.25f * _currentUiScale, 1));
                using var font = new Font("Segoe UI", emptyFontSz);
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

            float scale = _currentUiScale;
            float fanScale = _settings.FanScale * scale;

            int cols;
            if (w < (int)Math.Round(380 * scale))
            {
                cols = 2;
            }
            else if (w < (int)Math.Round(560 * scale))
            {
                cols = 3;
            }
            else
            {
                cols = Math.Max(2, (w - 4) / (int)Math.Round(120 * scale));
            }

            int rows = (int)Math.Ceiling((double)fans.Count / cols);
            if (rows < 1) rows = 1;

            int gapX = (int)Math.Max(2, Math.Round(5 * scale));
            int gapY = (int)Math.Max(2, Math.Round(5 * scale));
            int padX = (int)Math.Max(2, Math.Round(2 * scale));
            int cardW = (w - (padX * 2) - (gapX * (cols - 1))) / cols;
            bool isWideCard = (cardW >= 135 * scale);
            int cardH = (int)Math.Round((isWideCard 
                ? (46 + 18 * _settings.FanScale) 
                : (40 + 10 * _settings.FanScale)) * scale);

            float fontSizeName = (float)Math.Max(4.8, Math.Round((isWideCard 
                ? Math.Clamp(8.25f + 1.25f * (_settings.FanScale - 1f), 8.0f, 9.5f)
                : 7.25f) * scale, 1));
            float fontSizeRpm = (float)Math.Max(4.8, Math.Round((isWideCard
                ? Math.Clamp(7.75f + 1.25f * (_settings.FanScale - 1f), 7.5f, 9.0f)
                : 6.75f) * scale, 1));
            using var fontName = new Font("Segoe UI", fontSizeName, FontStyle.Bold);
            using var fontRpm = new Font("Segoe UI", fontSizeRpm, FontStyle.Bold);

            for (int i = 0; i < fans.Count; i++)
            {
                var f = fans[i];
                int col = i % cols;
                int row = i / cols;

                int cardX = padX + col * (cardW + gapX);
                int cardY = (int)Math.Round(4 * scale) + row * (cardH + gapY);

                if (cardY + cardH > h + (int)Math.Round(20 * scale)) break;

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
                    using var accentPen = new Pen(Color.FromArgb(180, tierColor.R, tierColor.G, tierColor.B), Math.Max(1.5f, 2f * fanScale));
                    g.DrawLine(accentPen, cardX + (int)Math.Round(3 * scale), cardY + 1, cardX + cardW - (int)Math.Round(3 * scale), cardY + 1);
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
                // Proportionally sized fan spinner
                float fanRadius = isWideCard 
                    ? Math.Clamp((cardH - (int)Math.Round(14f * scale)) / 2f, 10f * scale, 30f * scale)
                    : Math.Clamp((cardH - (int)Math.Round(10f * scale)) / 2f, 9f * scale, 20f * scale);

                float padLeft = isWideCard 
                    ? Math.Clamp(8f * fanScale, 6f * scale, 14f * scale) 
                    : (5f * scale);
                float cx = cardX + padLeft + fanRadius;
                float cy = cardY + (cardH / 2f);

                // Outer shroud
                using (var shroudBg = new SolidBrush(Color.FromArgb(18, 19, 24)))
                {
                    g.FillEllipse(shroudBg, cx - fanRadius, cy - fanRadius, fanRadius * 2, fanRadius * 2);
                }
                using (var shroudPen = new Pen(Color.FromArgb(46, 48, 60), Math.Max(0.8f, 1.1f * fanScale)))
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
                using (var hubBorder = new Pen(Color.FromArgb(60, 64, 80), Math.Max(0.8f, 1f * fanScale)))
                {
                    g.DrawEllipse(hubBorder, cx - hubR, cy - hubR, hubR * 2, hubR * 2);
                }
                float dotR = Math.Max(1.0f, hubR * 0.35f);
                using (var dotBrush = new SolidBrush(tierColor))
                {
                    g.FillEllipse(dotBrush, cx - dotR, cy - dotR, dotR * 2, dotR * 2);
                }

                // Text block on the right
                float gapSpinnerToText = isWideCard 
                    ? Math.Clamp(7f * fanScale, 5f * scale, 12f * scale) 
                    : (5f * scale);
                float textLeft = cx + fanRadius + gapSpinnerToText;
                float availTextW = Math.Max(25f * scale, (cardX + cardW - (int)Math.Round(3f * scale)) - textLeft);

                using var sf = new StringFormat
                {
                    Trimming = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.NoWrap
                };

                bool showSubInfo = isWideCard && _settings.FanScale >= 1.35f && !string.IsNullOrWhiteSpace(subInfo);

                if (showSubInfo)
                {
                    // 3-line layout for large magnification: Name (bold) + Sensor breakdown (small) + RPM (bold)
                    float fontSizeSub = (float)Math.Max(4.8, Math.Round(Math.Clamp(fontSizeName - 1.75f * scale, 6.0f * scale, 7.5f * scale), 1));
                    using var fontSub = new Font("Segoe UI", fontSizeSub, FontStyle.Regular);

                    var szName = g.MeasureString(dispName, fontName);
                    var szSub = g.MeasureString(subInfo, fontSub);
                    var szRpm = g.MeasureString(rpmText, fontRpm);

                    float spacing1 = Math.Clamp(2f * fanScale, 1.5f * scale, 3.5f * scale);
                    float spacing2 = Math.Clamp(3.5f * fanScale, 2.5f * scale, 5.5f * scale);

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
                        ? Math.Clamp(4f * fanScale, 3f * scale, 8f * scale)
                        : (2.5f * scale);

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
            _fanScrollX = Math.Clamp(_fanScrollX - Math.Sign(e.Delta) * step, 0, Math.Max(0, _maxFanScroll));
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
                _fanScrollX = Math.Clamp(_fanScrollStartX - delta, 0, Math.Max(0, _maxFanScroll));
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

        private void ToggleCpuProcesses()
        {
            _settings.ShowTopProcesses = !_settings.ShowTopProcesses;
            _btnCpuProcessesExpand.Text = _settings.ShowTopProcesses ? "▲" : "▼";
            _toolTip.SetToolTip(_btnCpuProcessesExpand, _settings.ShowTopProcesses ? Loc.Get("ProcessesCollapse") : Loc.Get("ProcessesExpand"));

            _hardware.Processes.IsEnabled = _settings.ShowTopProcesses || (_settings.ShowDiskGraph && _settings.EnhancedDiskMode);

            int diffH = (int)Math.Round(94 * _currentUiScale);
            int minH = (int)Math.Round(350 * _currentUiScale);
            if (_settings.ShowTopProcesses)
            {
                var screen = Screen.FromControl(this);
                if (this.Bottom + diffH > screen.WorkingArea.Bottom)
                {
                    int shiftUp = (this.Bottom + diffH) - screen.WorkingArea.Bottom;
                    this.Top = Math.Max(screen.WorkingArea.Top, this.Top - shiftUp);
                }
                this.Height += diffH;
            }
            else
            {
                if (this.Height - diffH >= minH)
                {
                    this.Height -= diffH;
                }
                else
                {
                    this.Height = minH;
                }
            }

            _settings.Save();
            _settingsForm?.UpdateTopProcessesCheckbox();
            LayoutComponents();
            this.Invalidate();
        }

        private void ToggleDiskLegend()
        {
            _settings.DiskLegendExpanded = !_settings.DiskLegendExpanded;
            _btnDiskExpand.Text = _settings.DiskLegendExpanded ? "▲" : "▼";
            _toolTip.SetToolTip(_btnDiskExpand, _settings.DiskLegendExpanded ? Loc.Get("DiskLegendCollapse") : Loc.Get("DiskLegendExpand"));

            int driveCount = Math.Max(1, _hardware.Disk.Drives.Count);
            int diskLegendH = (int)Math.Round((driveCount * 22 + 4) * _currentUiScale);
            int minH = (int)Math.Round(350 * _currentUiScale);

            if (_settings.DiskLegendExpanded)
            {
                var screen = Screen.FromControl(this);
                int addH = diskLegendH + (int)Math.Round(4 * _currentUiScale);
                if (this.Bottom + addH > screen.WorkingArea.Bottom)
                {
                    int shiftUp = (this.Bottom + addH) - screen.WorkingArea.Bottom;
                    this.Top = Math.Max(screen.WorkingArea.Top, this.Top - shiftUp);
                }
                this.Height += addH;
            }
            else
            {
                int subH = diskLegendH + (int)Math.Round(4 * _currentUiScale);
                if (this.Height - subH >= minH)
                {
                    this.Height -= subH;
                }
                else
                {
                    this.Height = minH;
                }
            }

            if (!_settings.DiskLegendExpanded)
            {
                _pnlDiskLegend?.ResetSelection();
            }

            _settings.Save();
            LayoutComponents();
            this.Invalidate();
        }

        private void ToggleSectionStopwatch() => ToggleSectionCollapse(() => _settings.SectionStopwatchCollapsed = !_settings.SectionStopwatchCollapsed);
        private void ToggleSectionCpu() => ToggleSectionCollapse(() => _settings.SectionCpuCollapsed = !_settings.SectionCpuCollapsed);
        private void ToggleSectionRam() => ToggleSectionCollapse(() => _settings.SectionRamCollapsed = !_settings.SectionRamCollapsed);
        private void ToggleSectionGpu() => ToggleSectionCollapse(() => _settings.SectionGpuCollapsed = !_settings.SectionGpuCollapsed);
        private void ToggleSectionVram() => ToggleSectionCollapse(() => _settings.SectionVramCollapsed = !_settings.SectionVramCollapsed);
        private void ToggleSectionDisk() => ToggleSectionCollapse(() => _settings.SectionDiskCollapsed = !_settings.SectionDiskCollapsed);
        private void ToggleSectionFans() => ToggleSectionCollapse(() => _settings.SectionFansCollapsed = !_settings.SectionFansCollapsed);

        private void ToggleSectionCollapse(Action toggleState)
        {
            float scale = _currentUiScale;
            int oldIdealH = CalculateDefaultHeight(scale);
            toggleState();
            _settings.Save();
            int newIdealH = CalculateDefaultHeight(scale);
            int diffH = newIdealH - oldIdealH;

            int minH = (int)Math.Round(160 * scale);
            this.MinimumSize = new Size((int)Math.Round(280 * scale), minH);

            if (diffH > 0)
            {
                var screen = Screen.FromControl(this) ?? Screen.PrimaryScreen;
                if (screen != null && this.Bottom + diffH > screen.WorkingArea.Bottom)
                {
                    int shiftUp = (this.Bottom + diffH) - screen.WorkingArea.Bottom;
                    this.Top = Math.Max(screen.WorkingArea.Top, this.Top - shiftUp);
                }
                this.Height += diffH;
            }
            else if (diffH < 0)
            {
                this.Height = Math.Max(minH, this.Height + diffH);
            }

            _settings.WindowHeight = this.Height;
            _settings.Save();
            LayoutComponents();
            UpdateMetricsUI();
            this.Invalidate(true);
        }

        private void OnDiskSelectionLayoutChanged(bool expanded)
        {
            int diffH = (int)Math.Round(84 * _currentUiScale);
            int minH = (int)Math.Round(350 * _currentUiScale);
            if (expanded)
            {
                var screen = Screen.FromControl(this);
                if (this.Bottom + diffH > screen.WorkingArea.Bottom)
                {
                    int shiftUp = (this.Bottom + diffH) - screen.WorkingArea.Bottom;
                    this.Top = Math.Max(screen.WorkingArea.Top, this.Top - shiftUp);
                }
                this.Height += diffH;
            }
            else
            {
                if (this.Height - diffH >= minH)
                {
                    this.Height -= diffH;
                }
                else
                {
                    this.Height = minH;
                }
            }
            LayoutComponents();
            this.Invalidate();
        }

        private void DrawMultiDiskGraphInternal(Graphics g, Panel pnl)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = pnl.Width;
            int h = pnl.Height;
            if (w <= 1 || h <= 1) return;

            // Grid lines (50%)
            using var gridPen = new Pen(Color.FromArgb(32, 32, 38), 1f);
            int midY = h / 2;
            g.DrawLine(gridPen, 0, midY, w, midY);

            var allDrives = _hardware.Disk.Drives;
            var visibleDrives = allDrives.Where(d => _settings.IsDiskVisible(d.Id, d.InstanceName)).ToList();
            if (visibleDrives.Count == 0)
            {
                Color dimStroke = Color.FromArgb(60, 6, 182, 212);
                Color dimFill = Color.FromArgb(20, 6, 182, 212);
                DrawGraphInternal(g, pnl, _hardware.Disk.History, dimStroke, dimFill);
                return;
            }

            var peakDrive = visibleDrives.OrderByDescending(d => d.LoadPercent).FirstOrDefault();

            for (int d = visibleDrives.Count - 1; d >= 0; d--)
            {
                var drive = visibleDrives[d];
                var history = drive.History;
                if (history.Count < 2) continue;

                Color color = drive.Color;
                var points = new PointF[history.Count];
                float stepX = (float)w / (history.Count - 1);

                for (int i = 0; i < history.Count; i++)
                {
                    float val = Math.Clamp(history[i], 0f, 100f);
                    float x = i * stepX;
                    float y = h - (h * (val / 100f));
                    points[i] = new PointF(x, y);
                }

                if (drive == peakDrive && drive.LoadPercent > 1f)
                {
                    using var path = new GraphicsPath();
                    path.AddLines(points);
                    path.AddLine(w, h, 0, h);
                    path.CloseFigure();

                    Color topFill = Color.FromArgb(45, color.R, color.G, color.B);
                    Color botFill = Color.FromArgb(5, 16, 18, 22);
                    using var brush = new LinearGradientBrush(new Point(0, 0), new Point(0, h), topFill, botFill);
                    g.FillPath(brush, path);
                }

                using var linePen = new Pen(color, drive == peakDrive ? 2.0f : 1.5f);
                if (visibleDrives.Count > 4 && d % 2 == 1)
                {
                    linePen.DashStyle = DashStyle.Dash;
                }
                g.DrawLines(linePen, points);
            }

            // Draw real-time active badges on top-right of graph
            var activeDrives = visibleDrives.Where(d => d.LoadPercent > 0.5f)
                .OrderByDescending(d => d.LoadPercent)
                .Take(3)
                .ToList();

            if (activeDrives.Count > 0)
            {
                float uiScale = _currentUiScale;
                using var font = new Font("Segoe UI", (float)Math.Max(5.0, Math.Round(7.0f * uiScale, 1)), FontStyle.Bold);
                int badgeX = w - (int)Math.Round(8 * uiScale);
                int badgeY = (int)Math.Round(2 * uiScale);
                int badgeH = (int)Math.Max(10, Math.Round(15 * uiScale));

                for (int i = 0; i < activeDrives.Count; i++)
                {
                    var drive = activeDrives[i];
                    string ltr = !string.IsNullOrEmpty(drive.DriveLetters) ? drive.DriveLetters : $"#{drive.PhysicalIndex}";
                    string prefix = ltr.EndsWith(":") ? ltr : $"{ltr}:";
                    string text = $"{prefix} {drive.LoadPercent:F0}%";
                    Size sz = TextRenderer.MeasureText(g, text, font, Size.Empty, TextFormatFlags.NoPadding);

                    badgeX -= sz.Width + (int)Math.Round(8 * uiScale);
                    if (badgeX < 6) break;

                    using (var bgBrush = new SolidBrush(Color.FromArgb(190, 16, 18, 24)))
                    using (var borderPen = new Pen(drive.Color, 1f))
                    {
                        var rect = new Rectangle(badgeX, badgeY, sz.Width + (int)Math.Round(6 * uiScale), badgeH);
                        g.FillRectangle(bgBrush, rect);
                        g.DrawRectangle(borderPen, rect);
                    }

                    TextRenderer.DrawText(g, text, font, new Point(badgeX + (int)Math.Round(3 * uiScale), badgeY + 1), drive.Color, TextFormatFlags.NoPadding);
                    badgeX -= 3;
                }
            }
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
                    using var font = new Font("Segoe UI", (float)Math.Max(4.8, Math.Round(8.25f * _currentUiScale, 1)), FontStyle.Italic);
                    g.DrawString("Monitoring top CPU processes...", font, brush, 8, (h - 16) / 2);
                    return;
                }

                int rowH = (int)Math.Round(18 * _currentUiScale);
                Color cpuCol = _isQuietMode ? Color.FromArgb(80, 220, 140) : Color.FromArgb(250, 180, 80);
                float fontSize = (float)Math.Max(4.8, Math.Round(8.25f * _currentUiScale, 1));
                using var fontName = new Font("Segoe UI", fontSize);
                using var fontCpu = new Font("Segoe UI", fontSize, FontStyle.Bold);
                using var fontRam = new Font("Segoe UI", fontSize);

                using var brushName = new SolidBrush(Color.FromArgb(220, 220, 230));
                using var brushCpu = new SolidBrush(cpuCol);
                using var brushRam = new SolidBrush(Color.FromArgb(56, 189, 248)); // RAM Cyan
                using var rowDividerPen = new Pen(Color.FromArgb(26, 26, 32), 1f);

                int iconSize = Math.Max(9, (int)Math.Round(16 * _currentUiScale));
                int iconYOffset = (rowH - iconSize) / 2;

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
                        g.DrawImage(proc.Icon, 4, y + iconYOffset, iconSize, iconSize);
                    }
                    else
                    {
                        using var dotBrush = new SolidBrush(Color.FromArgb(70, 70, 85));
                        int dotSize = Math.Max(3, (int)Math.Round(5 * _currentUiScale));
                        g.FillEllipse(dotBrush, 9, y + (rowH - dotSize) / 2, dotSize, dotSize);
                    }

                    // RAM value right-aligned at right edge
                    int ramW = (int)Math.Round(62 * _currentUiScale);
                    int ramX = w - ramW - (int)Math.Round(6 * _currentUiScale);
                    var ramRect = new Rectangle(ramX, y, ramW, rowH);
                    var ramFormat = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
                    g.DrawString(proc.RamFormatted, fontRam, brushRam, ramRect, ramFormat);

                    // CPU % right-aligned before RAM
                    int cpuW = (int)Math.Round(58 * _currentUiScale);
                    int cpuX = ramX - cpuW - (int)Math.Round(4 * _currentUiScale);
                    var cpuRect = new Rectangle(cpuX, y, cpuW, rowH);
                    var cpuFormat = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
                    g.DrawString($"{proc.CpuPercent:F1}%", fontCpu, brushCpu, cpuRect, cpuFormat);

                    // Process Name with ellipsis
                    int nameX = iconSize + (int)Math.Round(8 * _currentUiScale);
                    int nameW = Math.Max(30, cpuX - nameX - (int)Math.Round(4 * _currentUiScale));
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
                _stopwatchTimer?.Dispose();
                _infoForm?.Dispose();
                _settingsForm?.Dispose();
                _toolTip?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
