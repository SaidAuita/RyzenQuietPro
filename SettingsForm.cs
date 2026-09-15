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

        private readonly List<Button> _langButtons = new();
        private readonly List<Button> _trayButtons = new();

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
        private Button _btnFanFolder = null!;
        private Button _btnFanModeGraph = null!;
        private Button _btnFanModeIcons = null!;
        private CheckBox _chkFanDemo = null!;
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
            int formH = multiGpu ? 916 : 886;

            this.AutoScaleMode = AutoScaleMode.None;
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.ShowInTaskbar = false;
            this.ClientSize = new Size(420, formH);
            this.BackColor = Color.FromArgb(20, 20, 24);
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
                using var pen = new Pen(Color.FromArgb(50, 50, 65), 1f);
                e.Graphics.DrawRectangle(pen, 0, 0, this.ClientSize.Width - 1, this.ClientSize.Height - 1);
            };
        }

        private void InitializeComponents()
        {
            int w = this.ClientSize.Width;
            int cardW = w - 28;
            bool multiGpu = _hardware.Gpu.GpuCount > 1;

            // ================= HEADER =================
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 38,
                BackColor = Color.FromArgb(14, 14, 18),
                Cursor = Cursors.SizeAll
            };
            header.MouseDown += OnHeaderDrag;
            this.Controls.Add(header);

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
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            btnX.MouseEnter += (s, e) => btnX.ForeColor = Color.White;
            btnX.MouseLeave += (s, e) => btnX.ForeColor = Color.FromArgb(160, 160, 175);
            btnX.Click += (s, e) => this.Close();
            header.Controls.Add(btnX);

            int curY = 46;

            // ================= 1. LANGUAGE CARD =================
            // 8 clean 2-letter buttons in 1 row: RU, EN, DE, ES, FR, JA, PT, ZH
            var cardLang = CreateCard(14, curY, cardW, 70);
            this.Controls.Add(cardLang);

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
            int cardGraphsH = multiGpu ? 418 : 388;
            var cardGraphs = CreateCard(14, curY, cardW, cardGraphsH);
            this.Controls.Add(cardGraphs);

            _lblSecGraphs = CreateSectionHeader(Loc.Get("MenuGraphs"), 12, 8);
            cardGraphs.Controls.Add(_lblSecGraphs);

            int chkX = 14;
            int chkY = 32;
            int chkGap = 26;

            _chkCpuGraph = CreateCheckbox(Loc.Get("CpuGraph"), chkX, chkY, _settings.ShowCpuGraph, v => _settings.ShowCpuGraph = v);
            _chkCpuCores = CreateCheckbox(Loc.Get("CpuCores"), chkX, chkY + (chkGap * 1), _settings.ShowCpuCores, v => _settings.ShowCpuCores = v);
            _chkTopProcesses = CreateCheckbox(Loc.Get("TopProcesses"), chkX, chkY + (chkGap * 2), _settings.ShowTopProcesses, v => {
                _settings.ShowTopProcesses = v;
                _hardware.Processes.IsEnabled = v;
            });
            _chkRamGraph = CreateCheckbox(Loc.Get("RamGraph"), chkX, chkY + (chkGap * 3), _settings.ShowRamGraph, v => _settings.ShowRamGraph = v);
            _chkGpuGraph = CreateCheckbox(Loc.Get("GpuGraph"), chkX, chkY + (chkGap * 4), _settings.ShowGpuGraph, v => _settings.ShowGpuGraph = v);
            _chkGpuTemp = CreateCheckbox(Loc.Get("GpuTemp"), chkX, chkY + (chkGap * 5), _settings.ShowGpuTempLine, v => _settings.ShowGpuTempLine = v);
            _chkGpuFan = CreateCheckbox(Loc.Get("GpuFan"), chkX, chkY + (chkGap * 6), _settings.ShowGpuFanSpeed, v => _settings.ShowGpuFanSpeed = v);
            _chkVramGraph = CreateCheckbox(Loc.Get("VramGraph"), chkX, chkY + (chkGap * 7), _settings.ShowVramGraph, v => _settings.ShowVramGraph = v);
            _chkDiskGraph = CreateCheckbox(Loc.Get("DiskGraph"), chkX, chkY + (chkGap * 8), _settings.ShowDiskGraph, v => _settings.ShowDiskGraph = v);

            int fanY = chkY + (chkGap * 9);
            _chkFanGraph = CreateCheckbox(Loc.Get("FanGraph"), chkX, fanY, _settings.ShowFanGraph, v => {
                _settings.ShowFanGraph = v;
                _settings.EnableFanAddon = v;
                _hardware.Fans.SetEnabled(v);
                UpdateFanStatusUI();
            });

            _lblFanStatus = new Label
            {
                Location = new Point(cardW - 142, fanY + 3),
                Size = new Size(106, 20),
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.FromArgb(120, 120, 130)
            };

            _btnFanFolder = new Button
            {
                Text = "📁",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(170, 170, 185),
                BackColor = Color.FromArgb(32, 32, 42),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(24, 22),
                Location = new Point(cardW - 32, fanY + 2),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = Padding.Empty
            };
            _btnFanFolder.FlatAppearance.BorderSize = 0;
            _toolTip.SetToolTip(_btnFanFolder, Loc.Get("FanPluginFolder"));
            _btnFanFolder.Click += (s, e) => FanMonitorClient.OpenPluginFolder();

            // Fan visual sub-mode buttons: [ Graph ] [ Icons ] and [✓] Demo
            int fanModeY = fanY + 25;
            _btnFanModeGraph = new Button
            {
                Text = Loc.Get("FanVisualMode_Graph"),
                Font = new Font("Segoe UI", 7.75f, FontStyle.Bold),
                Size = new Size(84, 23),
                Location = new Point(chkX + 16, fanModeY),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = Padding.Empty
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
                Font = new Font("Segoe UI", 7.75f, FontStyle.Bold),
                Size = new Size(84, 23),
                Location = new Point(chkX + 16 + 84 + 5, fanModeY),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = Padding.Empty
            };
            _btnFanModeIcons.FlatAppearance.BorderSize = 1;
            _btnFanModeIcons.Click += (s, e) => {
                _settings.FanVisualMode = 1;
                UpdateFanModeUI();
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            };

            _chkFanDemo = CreateCheckbox(Loc.Get("FanDemoMode"), chkX + 16 + (84 * 2) + 12, fanModeY - 1, _settings.EnableFanDemo, v => {
                _settings.EnableFanDemo = v;
                _hardware.Fans.DemoMode = v;
                _settings.Save();
                UpdateFanStatusUI();
                _onSettingsUpdated?.Invoke();
            });
            _chkFanDemo.Font = new Font("Segoe UI", 8f);

            cardGraphs.Controls.AddRange(new Control[] {
                _chkCpuGraph, _chkCpuCores, _chkTopProcesses,
                _chkRamGraph, _chkGpuGraph, _chkGpuTemp, _chkGpuFan,
                _chkVramGraph, _chkDiskGraph, _chkFanGraph,
                _lblFanStatus, _btnFanFolder,
                _btnFanModeGraph, _btnFanModeIcons, _chkFanDemo
            });
            UpdateFanModeUI();

            int nextY = fanModeY + 30;

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
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                BackColor = Color.FromArgb(34, 34, 44),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(cardW - 28, 32),
                Location = new Point(14, nextY + 4),
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = Padding.Empty,
                Cursor = Cursors.Hand
            };
            _btnResetGraphs.FlatAppearance.BorderColor = Color.FromArgb(52, 52, 68);
            _btnResetGraphs.Click += (s, e) => {
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
                UpdateFanModeUI();
                _hardware.Fans.SetEnabled(true);
                UpdateFanStatusUI();
                if (multiGpu) _settings.ShowAllGpus = true;
                _settings.Save();
                _onSettingsUpdated?.Invoke();
            };
            cardGraphs.Controls.Add(_btnResetGraphs);

            curY += cardGraphsH + 8;

            // ================= 3. TRAY ICON CARD =================
            var cardTray = CreateCard(14, curY, cardW, 70);
            this.Controls.Add(cardTray);

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
            var cardSys = CreateCard(14, curY, cardW, 192);
            this.Controls.Add(cardSys);

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

            // ================= FOOTER BUTTONS =================
            var pnlFooter = new Panel
            {
                Location = new Point(14, curY),
                Size = new Size(cardW, 36),
                BackColor = Color.Transparent
            };
            this.Controls.Add(pnlFooter);

            _btnSpecs = new Button
            {
                Text = "ℹ",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 215),
                BackColor = Color.FromArgb(32, 32, 40),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(110, 32),
                Location = new Point(0, 2),
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
                Size = new Size(110, 32),
                Location = new Point(116, 2),
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
                Size = new Size(cardW - 232, 32),
                Location = new Point(232, 2),
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = Padding.Empty,
                Cursor = Cursors.Hand
            };
            _btnClose.FlatAppearance.BorderSize = 0;
            _btnClose.Click += (s, e) => this.Close();
            pnlFooter.Controls.Add(_btnClose);
        }

        private Panel CreateCard(int x, int y, int w, int h)
        {
            var pnl = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
                BackColor = Color.FromArgb(26, 26, 32)
            };
            pnl.Paint += (s, e) => {
                using var borderPen = new Pen(Color.FromArgb(40, 40, 50), 1f);
                e.Graphics.DrawRectangle(borderPen, 0, 0, pnl.Width - 1, pnl.Height - 1);
            };
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
            _btnFanModeGraph.Text = Loc.Get("FanVisualMode_Graph");
            _btnFanModeIcons.Text = Loc.Get("FanVisualMode_Icons");
            _chkFanDemo.Text = Loc.Get("FanDemoMode");
            _toolTip.SetToolTip(_btnFanFolder, Loc.Get("FanPluginFolder"));
            if (_chkShowAllGpus != null) _chkShowAllGpus.Text = Loc.Get("ShowAllGpus");

            _btnResetGraphs.Text = "⚡ " + Loc.Get("ShowAllGraphs");

            foreach (var btn in _trayButtons)
            {
                var opt = ((string labelKey, string tipKey, int metricVal, bool isLive))btn.Tag!;
                btn.Text = opt.isLive ? $"{Loc.Get(opt.labelKey)} %" : Loc.Get(opt.labelKey);
                _toolTip.SetToolTip(btn, Loc.Get(opt.tipKey));
            }

            _chkAlwaysOnTop.Text = Loc.Get("AlwaysOnTop");
            _chkAutoQuiet.Text = Loc.Get("AutoQuiet");
            _chkStartup.Text = Loc.Get("Startup");

            _btnSpecs.Text = "ℹ";
            _toolTip.SetToolTip(_btnSpecs, Loc.Get("TipInfo"));
            _toolTip.SetToolTip(_btnTools, "https://ph-cu-s.com/tools");
            _btnClose.Text = Loc.Get("InfoClose");

            UpdateFanModeUI();
            UpdateFanStatusUI();
        }

        private void UpdateFanModeUI()
        {
            bool isIcons = _settings.FanVisualMode == 1;

            _btnFanModeGraph.BackColor = !isIcons ? Color.FromArgb(38, 54, 75) : Color.FromArgb(28, 28, 36);
            _btnFanModeGraph.ForeColor = !isIcons ? Color.FromArgb(56, 189, 248) : Color.FromArgb(140, 140, 155);
            _btnFanModeGraph.FlatAppearance.BorderColor = !isIcons ? Color.FromArgb(56, 189, 248) : Color.FromArgb(45, 45, 58);

            _btnFanModeIcons.BackColor = isIcons ? Color.FromArgb(38, 54, 75) : Color.FromArgb(28, 28, 36);
            _btnFanModeIcons.ForeColor = isIcons ? Color.FromArgb(56, 189, 248) : Color.FromArgb(140, 140, 155);
            _btnFanModeIcons.FlatAppearance.BorderColor = isIcons ? Color.FromArgb(56, 189, 248) : Color.FromArgb(45, 45, 58);
        }

        private void UpdateFanStatusUI()
        {
            if (!_settings.ShowFanGraph)
            {
                _lblFanStatus.Text = Loc.Get("FanPluginStatus_Disabled");
                _lblFanStatus.ForeColor = Color.FromArgb(120, 120, 130);
                return;
            }

            if (_hardware.Fans.DemoMode)
            {
                _lblFanStatus.Text = Loc.Get("FanDemoMode");
                _lblFanStatus.ForeColor = Color.FromArgb(56, 189, 248);
                return;
            }

            switch (_hardware.Fans.Status)
            {
                case FanPluginStatus.Connected:
                    _lblFanStatus.Text = string.Format(Loc.Get("FanPluginStatus_Connected"), _hardware.Fans.FanCount);
                    _lblFanStatus.ForeColor = Color.FromArgb(74, 222, 128);
                    break;
                case FanPluginStatus.Connecting:
                case FanPluginStatus.Starting:
                    _lblFanStatus.Text = Loc.Get("FanPluginStatus_Connecting");
                    _lblFanStatus.ForeColor = Color.FromArgb(250, 204, 21);
                    break;
                case FanPluginStatus.NotInstalled:
                    _lblFanStatus.Text = Loc.Get("FanPluginStatus_NotInstalled");
                    _lblFanStatus.ForeColor = Color.FromArgb(248, 113, 113);
                    break;
                case FanPluginStatus.Error:
                    _lblFanStatus.Text = Loc.Get("FanPluginStatus_NeedAdmin");
                    _lblFanStatus.ForeColor = Color.FromArgb(248, 113, 113);
                    break;
                default:
                    _lblFanStatus.Text = Loc.Get("FanPluginStatus_Disabled");
                    _lblFanStatus.ForeColor = Color.FromArgb(120, 120, 130);
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
