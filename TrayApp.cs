using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RyzenQuietPro
{
    public class TrayApp : Form
    {
        private const int HOTKEY_ID = 9002;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const int WM_HOTKEY = 0x0312;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        private NotifyIcon _notifyIcon = null!;
        private ContextMenuStrip _contextMenu = null!;
        private ToolStripMenuItem _openDashItem = null!;
        private ToolStripMenuItem _quietMenuItem = null!;
        private ToolStripMenuItem _normalMenuItem = null!;
        private ToolStripMenuItem _detachMenuItem = null!;
        private ToolStripMenuItem _autoQuietMenuItem = null!;
        private ToolStripMenuItem _startupMenuItem = null!;
        private ToolStripMenuItem _topProcessesMenuItem = null!;
        private ToolStripMenuItem? _showAllGpusMenuItem;
        private ToolStripMenuItem _metricMenu = null!;
        private ToolStripMenuItem _metricCpuItem = null!;
        private ToolStripMenuItem _metricGpuItem = null!;
        private ToolStripMenuItem _metricRamItem = null!;
        private ToolStripMenuItem _metricNoneItem = null!;
        private ToolStripMenuItem _exitMenuItem = null!;

        private Bitmap _quietBitmap = null!;
        private Bitmap _normalBitmap = null!;
        private Icon _quietIcon = null!;
        private Icon _normalIcon = null!;
        private Icon? _currentDynamicIcon;

        private bool _isQuietMode;
        private bool _isStartupEnabled;
        private bool _hotkeyRegistered;

        private readonly AppSettings _settings;
        private readonly HardwareMonitor _hardware;
        private DashboardPro? _dashboard;

        public TrayApp()
        {
            this.WindowState = FormWindowState.Minimized;
            this.ShowInTaskbar = false;
            this.Opacity = 0;
            this.FormBorderStyle = FormBorderStyle.None;

            _settings = AppSettings.Load();
            _hardware = new HardwareMonitor(_settings);

            Loc.Initialize(_settings.Language);
            Loc.LanguageChanged += OnLanguageChanged;

            LoadBitmapsAndIcons();
            this.Icon = _isQuietMode ? _quietIcon : _normalIcon;

            var currentMode = PowerPlanManager.GetCurrentMode();
            _isQuietMode = (currentMode == PowerPlanManager.Mode.Quiet);
            _isStartupEnabled = StartupManager.IsStartupEnabled();

            InitializeContextMenu();
            InitializeTrayIcon();
            RegisterGlobalHotkey();

            _dashboard = new DashboardPro(
                _hardware,
                _settings,
                _isQuietMode,
                _isStartupEnabled,
                onModeChangeRequested: SwitchToMode,
                onSettingsChanged: OnSettingsChanged
            );

            _hardware.MetricsUpdated += OnMetricsUpdated;
            _hardware.Start(1000);

            if (_settings.GpuTuningEnabled)
            {
                bool gpuQuiet = _settings.SeparateCpuGpuControl ? _settings.GpuIsQuietMode : _isQuietMode;
                _hardware.GpuTuning.ApplyMode(gpuQuiet);
            }

            UpdateTrayState();

            // If user left widget detached in previous session, restore it
            if (_settings.IsDetached)
            {
                _dashboard.ShowDashboard();
            }

            Logger.Log($"TrayApp initialized. Mode: {currentMode}, Detached: {_settings.IsDetached}");
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(false);
        }

        private void LoadBitmapsAndIcons()
        {
            AppIcons.Initialize();
            _quietBitmap = AppIcons.QuietBitmap;
            _normalBitmap = AppIcons.NormalBitmap;
            _quietIcon = AppIcons.QuietIcon;
            _normalIcon = AppIcons.NormalIcon;
        }

        private void InitializeContextMenu()
        {
            _contextMenu = new ContextMenuStrip();

            var titleItem = new ToolStripMenuItem("RyzenQuiet PRO v4.0")
            {
                Enabled = false,
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold)
            };
            _contextMenu.Items.Add(titleItem);

            _openDashItem = new ToolStripMenuItem(Loc.Get("AppTitle"));
            _openDashItem.Click += (s, e) => ShowDashboard();
            _contextMenu.Items.Add(_openDashItem);

            _detachMenuItem = new ToolStripMenuItem(_settings.IsDetached ? Loc.Get("TipDock") : Loc.Get("TipDetach"));
            _detachMenuItem.Click += (s, e) => {
                _dashboard?.ToggleDetachedMode();
                if (_dashboard != null && !_dashboard.Visible) _dashboard.ShowDashboard();
                UpdateContextMenuState();
            };
            _contextMenu.Items.Add(_detachMenuItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            _quietMenuItem = new ToolStripMenuItem(Loc.Get("SilentFull"));
            _quietMenuItem.Click += (s, e) => SwitchToMode(PowerPlanManager.Mode.Quiet);
            _contextMenu.Items.Add(_quietMenuItem);

            _normalMenuItem = new ToolStripMenuItem(Loc.Get("BoostActive"));
            _normalMenuItem.Click += (s, e) => SwitchToMode(PowerPlanManager.Mode.Normal);
            _contextMenu.Items.Add(_normalMenuItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            // Tray metric selector
            _metricMenu = new ToolStripMenuItem(Loc.Get("TrayIconMenu"));
            _metricCpuItem = new ToolStripMenuItem(Loc.Get("TrayMetricCpu")) { Checked = _settings.LiveTrayIcon && _settings.TrayMetric == 0 };
            _metricCpuItem.Click += (s, e) => SetTrayMetric(0);
            _metricGpuItem = new ToolStripMenuItem(Loc.Get("TrayMetricGpu")) { Checked = _settings.LiveTrayIcon && _settings.TrayMetric == 1 };
            _metricGpuItem.Click += (s, e) => SetTrayMetric(1);
            _metricRamItem = new ToolStripMenuItem(Loc.Get("TrayMetricRam")) { Checked = _settings.LiveTrayIcon && _settings.TrayMetric == 2 };
            _metricRamItem.Click += (s, e) => SetTrayMetric(2);
            _metricNoneItem = new ToolStripMenuItem(Loc.Get("TrayMetricNone")) { Checked = !_settings.LiveTrayIcon };
            _metricNoneItem.Click += (s, e) => {
                _settings.LiveTrayIcon = false;
                _settings.Save();
                UpdateContextMenuState();
                UpdateTrayState();
            };

            _metricMenu.DropDownItems.Add(_metricCpuItem);
            _metricMenu.DropDownItems.Add(_metricGpuItem);
            _metricMenu.DropDownItems.Add(_metricRamItem);
            _metricMenu.DropDownItems.Add(_metricNoneItem);
            _contextMenu.Items.Add(_metricMenu);

            _autoQuietMenuItem = new ToolStripMenuItem(Loc.Get("AutoQuiet"))
            {
                Checked = _settings.AutoQuietOnIdle
            };
            _autoQuietMenuItem.Click += (s, e) => {
                _settings.AutoQuietOnIdle = !_settings.AutoQuietOnIdle;
                _settings.Save();
                OnSettingsChanged();
            };
            _contextMenu.Items.Add(_autoQuietMenuItem);

            _startupMenuItem = new ToolStripMenuItem(Loc.Get("Startup"))
            {
                Checked = _isStartupEnabled
            };
            _startupMenuItem.Click += (s, e) => ToggleStartup();
            _contextMenu.Items.Add(_startupMenuItem);

            _topProcessesMenuItem = new ToolStripMenuItem(Loc.Get("TopProcesses"))
            {
                Checked = _settings.ShowTopProcesses
            };
            _topProcessesMenuItem.Click += (s, e) => {
                _settings.ShowTopProcesses = !_settings.ShowTopProcesses;
                _hardware.Processes.IsEnabled = _settings.ShowTopProcesses;
                _settings.Save();
                _dashboard?.SyncTopProcessesSetting();
                UpdateContextMenuState();
            };
            _contextMenu.Items.Add(_topProcessesMenuItem);

            if (_hardware.Gpu.GpuCount > 1)
            {
                _showAllGpusMenuItem = new ToolStripMenuItem(Loc.Get("ShowAllGpus"))
                {
                    Checked = _settings.ShowAllGpus
                };
                _showAllGpusMenuItem.Click += (s, e) => {
                    _settings.ShowAllGpus = !_settings.ShowAllGpus;
                    _settings.Save();
                    _dashboard?.SyncShowAllGpusSetting();
                    UpdateContextMenuState();
                };
                _contextMenu.Items.Add(_showAllGpusMenuItem);
            }

            _contextMenu.Items.Add(new ToolStripSeparator());

            _exitMenuItem = new ToolStripMenuItem(Loc.Get("Exit"));
            _exitMenuItem.Click += (s, e) => Application.Exit();
            _contextMenu.Items.Add(_exitMenuItem);
        }

        private void LocalizeContextMenu()
        {
            if (_openDashItem != null) _openDashItem.Text = Loc.Get("AppTitle");
            if (_detachMenuItem != null) _detachMenuItem.Text = _settings.IsDetached ? Loc.Get("TipDock") : Loc.Get("TipDetach");
            if (_quietMenuItem != null) _quietMenuItem.Text = Loc.Get("SilentFull");
            if (_normalMenuItem != null) _normalMenuItem.Text = Loc.Get("BoostActive");
            if (_metricMenu != null) _metricMenu.Text = Loc.Get("TrayIconMenu");
            if (_metricCpuItem != null) _metricCpuItem.Text = Loc.Get("TrayMetricCpu");
            if (_metricGpuItem != null) _metricGpuItem.Text = Loc.Get("TrayMetricGpu");
            if (_metricRamItem != null) _metricRamItem.Text = Loc.Get("TrayMetricRam");
            if (_metricNoneItem != null) _metricNoneItem.Text = Loc.Get("TrayMetricNone");
            if (_autoQuietMenuItem != null) _autoQuietMenuItem.Text = Loc.Get("AutoQuiet");
            if (_startupMenuItem != null) _startupMenuItem.Text = Loc.Get("Startup");
            if (_topProcessesMenuItem != null) _topProcessesMenuItem.Text = Loc.Get("TopProcesses");
            if (_showAllGpusMenuItem != null) _showAllGpusMenuItem.Text = Loc.Get("ShowAllGpus");
            if (_exitMenuItem != null) _exitMenuItem.Text = Loc.Get("Exit");
        }

        private void OnLanguageChanged()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(OnLanguageChanged));
                return;
            }
            LocalizeContextMenu();
            UpdateContextMenuState();
        }

        private void SetTrayMetric(int metric)
        {
            _settings.LiveTrayIcon = true;
            _settings.TrayMetric = metric;
            _settings.Save();
            UpdateContextMenuState();
            UpdateTrayState();
        }

        private void UpdateContextMenuState()
        {
            _metricCpuItem.Checked = _settings.LiveTrayIcon && _settings.TrayMetric == 0;
            _metricGpuItem.Checked = _settings.LiveTrayIcon && _settings.TrayMetric == 1;
            _metricRamItem.Checked = _settings.LiveTrayIcon && _settings.TrayMetric == 2;
            _metricNoneItem.Checked = !_settings.LiveTrayIcon;

            _topProcessesMenuItem.Checked = _settings.ShowTopProcesses;
            if (_showAllGpusMenuItem != null)
            {
                _showAllGpusMenuItem.Checked = _settings.ShowAllGpus;
            }
            _detachMenuItem.Text = _settings.IsDetached ? Loc.Get("TipDock") : Loc.Get("TipDetach");
        }

        private void InitializeTrayIcon()
        {
            _notifyIcon = new NotifyIcon()
            {
                Icon = _isQuietMode ? _quietIcon : _normalIcon,
                ContextMenuStrip = _contextMenu,
                Visible = true,
                Text = "RyzenQuiet PRO"
            };

            _notifyIcon.MouseClick += (s, e) => {
                if (e.Button == MouseButtons.Left) ShowDashboard();
            };

            _notifyIcon.DoubleClick += (s, e) => ToggleMode();
        }

        private void ShowDashboard()
        {
            if (_dashboard == null || _dashboard.IsDisposed)
            {
                _dashboard = new DashboardPro(
                    _hardware,
                    _settings,
                    _isQuietMode,
                    _isStartupEnabled,
                    onModeChangeRequested: SwitchToMode,
                    onSettingsChanged: OnSettingsChanged
                );
            }
            _dashboard.ShowDashboard();
        }

        private void RegisterGlobalHotkey()
        {
            try
            {
                _hotkeyRegistered = RegisterHotKey(this.Handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, (uint)Keys.Q);
                if (_hotkeyRegistered) Logger.Log("Global hotkey Ctrl+Alt+Q registered.");
            }
            catch (Exception ex)
            {
                Logger.Log($"Error registering global hotkey: {ex.Message}");
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            {
                ToggleMode();
            }
            base.WndProc(ref m);
        }

        private void ToggleMode()
        {
            var targetMode = _isQuietMode ? PowerPlanManager.Mode.Normal : PowerPlanManager.Mode.Quiet;
            SwitchToMode(targetMode);
        }

        private void SwitchToMode(PowerPlanManager.Mode targetMode)
        {
            if (PowerPlanManager.SetMode(targetMode))
            {
                _isQuietMode = (targetMode == PowerPlanManager.Mode.Quiet);
                if (!_settings.SeparateCpuGpuControl && _settings.GpuTuningEnabled)
                {
                    _hardware.GpuTuning.ApplyMode(_isQuietMode);
                }
                UpdateTrayState();
                _dashboard?.SetModeState(_isQuietMode);
                ShowNotification(targetMode);
            }
            else
            {
                _notifyIcon.ShowBalloonTip(3000, "RyzenQuiet PRO", "Failed to switch power setting.", ToolTipIcon.Error);
            }
        }

        private void OnSettingsChanged()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(OnSettingsChanged));
                return;
            }

            UpdateContextMenuState();
            _autoQuietMenuItem.Checked = _settings.AutoQuietOnIdle;
            _startupMenuItem.Checked = StartupManager.IsStartupEnabled();
            _dashboard?.SetStartupState(_startupMenuItem.Checked);

            UpdateTrayState();
        }

        private void OnMetricsUpdated()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(OnMetricsUpdated));
                return;
            }

            float cpu = _hardware.Cpu.TotalCpuUsage;
            float ram = _hardware.Ram.MemoryLoadPercent;
            float gpu = _hardware.Gpu.GpuLoadPercent;
            float vram = _hardware.Gpu.VramLoadPercent;
            float temp = _hardware.Gpu.GpuTemperatureC;

            // 1. Tooltip (max 63 chars on older Win32 or 127 chars on Win10/11)
            string modeName = _isQuietMode ? "Quiet" : "Normal";
            string tooltip;
            if (_settings.ShowAllGpus && _hardware.Gpu.GpuCount > 1)
            {
                tooltip = $"RQ PRO [{modeName}] CPU:{cpu:F0}% RAM:{ram:F0}% GPUs({_hardware.Gpu.GpuCount}):{gpu:F0}%";
            }
            else
            {
                tooltip = (temp > 0)
                    ? $"RQ PRO [{modeName}] CPU:{cpu:F0}% RAM:{ram:F0}% GPU:{gpu:F0}% ({temp:F0}°C) VR:{vram:F0}%"
                    : $"RQ PRO [{modeName}] CPU:{cpu:F0}% RAM:{ram:F0}% GPU:{gpu:F0}% VR:{vram:F0}%";
            }
            if (tooltip.Length > 63) tooltip = tooltip.Substring(0, 63);
            _notifyIcon.Text = tooltip;

            // 2. Dynamic icon if enabled
            if (_settings.LiveTrayIcon)
            {
                float metricValue = _settings.TrayMetric switch
                {
                    1 => gpu,
                    2 => ram,
                    _ => cpu
                };
                RenderDynamicTrayIcon(metricValue);
            }

            // 3. Auto-quiet check
            if (_settings.AutoQuietOnIdle && !_isQuietMode)
            {
                var idle = CpuMonitor.GetIdleTime();
                if (idle.TotalMinutes >= _settings.IdleMinutes)
                {
                    Logger.Log($"Idle detected ({idle.TotalMinutes:F1} min). Switching to Quiet Mode.");
                    SwitchToMode(PowerPlanManager.Mode.Quiet);
                }
            }
        }

        private void RenderDynamicTrayIcon(float value)
        {
            try
            {
                int iconSize = SystemInformation.SmallIconSize.Width;
                if (iconSize <= 0) iconSize = 16;

                int intVal = Math.Clamp((int)Math.Round(value), 0, 99);
                string text = intVal.ToString();
                bool isHighLoad = intVal > 80;

                // Red if > 80%, vibrant green otherwise
                Color textColor = isHighLoad
                    ? Color.FromArgb(255, 65, 65)
                    : Color.FromArgb(50, 235, 100);

                using var bmp = new Bitmap(iconSize, iconSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;

                    float fontSize = Math.Max(9f, (float)(iconSize * 11.0 / 16.0));
                    using var font = new Font("Tahoma", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
                    using var sf = new StringFormat(StringFormat.GenericTypographic)
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };

                    var rect = new RectangleF(0, 0, iconSize, iconSize);

                    // 1px subtle dark shadow for contrast on light taskbars
                    using var shadowBrush = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
                    g.DrawString(text, font, shadowBrush, new RectangleF(1, 1, iconSize, iconSize), sf);

                    // Primary colored text (green or red)
                    using var textBrush = new SolidBrush(textColor);
                    g.DrawString(text, font, textBrush, rect, sf);
                }

                IntPtr hIcon = bmp.GetHicon();
                var newIcon = (Icon)Icon.FromHandle(hIcon).Clone();
                DestroyIcon(hIcon);

                var oldDynamic = _currentDynamicIcon;
                _currentDynamicIcon = newIcon;
                _notifyIcon.Icon = newIcon;
                oldDynamic?.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Log($"RenderDynamicTrayIcon error: {ex.Message}");
            }
        }

        private void UpdateTrayState()
        {
            this.Icon = _isQuietMode ? _quietIcon : _normalIcon;
            _quietMenuItem.Checked = _isQuietMode;
            _normalMenuItem.Checked = !_isQuietMode;

            if (!_settings.LiveTrayIcon)
            {
                _notifyIcon.Icon = _isQuietMode ? _quietIcon : _normalIcon;
                if (_currentDynamicIcon != null)
                {
                    _currentDynamicIcon.Dispose();
                    _currentDynamicIcon = null;
                }
            }
            else
            {
                float metricValue = _settings.TrayMetric switch
                {
                    1 => _hardware.Gpu.GpuLoadPercent,
                    2 => _hardware.Ram.MemoryLoadPercent,
                    _ => _hardware.Cpu.TotalCpuUsage
                };
                RenderDynamicTrayIcon(metricValue);
            }
        }

        private void ToggleStartup()
        {
            _isStartupEnabled = !_isStartupEnabled;
            StartupManager.SetStartup(_isStartupEnabled);
            _startupMenuItem.Checked = _isStartupEnabled;
            _dashboard?.SetStartupState(_isStartupEnabled);
        }

        private void ShowNotification(PowerPlanManager.Mode mode)
        {
            string msg = mode == PowerPlanManager.Mode.Quiet ? Loc.Get("SilentFull") : Loc.Get("BoostActive");
            _notifyIcon.ShowBalloonTip(1500, "RyzenQuiet PRO", msg, ToolTipIcon.Info);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Logger.Log($"TrayApp.OnFormClosing: CloseReason={e.CloseReason}");
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            Logger.Log($"TrayApp.Dispose(disposing={disposing}) called. StackTrace: {Environment.StackTrace}");
            if (disposing)
            {
                Loc.LanguageChanged -= OnLanguageChanged;

                if (_hotkeyRegistered && this.Handle != IntPtr.Zero)
                {
                    UnregisterHotKey(this.Handle, HOTKEY_ID);
                }

                _hardware.MetricsUpdated -= OnMetricsUpdated;
                _hardware.Dispose();
                _dashboard?.Dispose();

                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                }

                _contextMenu?.Dispose();
                _currentDynamicIcon?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
