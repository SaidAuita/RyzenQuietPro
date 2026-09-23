using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RyzenQuietPro
{
    public class StopwatchEditDialog : Form
    {
        private readonly ProcessMonitor _monitor;
        private readonly StopwatchProfile _profile;
        private readonly bool _isNew;

        private TextBox _txtName = null!;
        private RadioButton _rbAllApps = null!;
        private RadioButton _rbSpecificApp = null!;
        private Panel _pnlAppSelect = null!;
        private Label _lblTargetApp = null!;
        private Button _btnPickApp = null!;
        private CheckBox _chkShowOnCpuGraph = null!;
        private Button[] _scaleButtons = Array.Empty<Button>();
        private int _selectedGraphScale = 0;

        private string _targetExe = "";
        private string _targetFriendlyName = "";
        private string _selectedColorHex = "#34D399";
        private readonly Panel _pnlColorPalette = new();
        private readonly Button[] _colorButtons;
        private Button _btnCustomColor = null!;

        private static readonly (string Hex, string Name)[] Palette = new[]
        {
            ("#34D399", "Emerald"),
            ("#F59E0B", "Amber"),
            ("#38BDF8", "Sky Blue"),
            ("#A855F7", "Purple"),
            ("#F43F5E", "Rose"),
            ("#FB923C", "Orange"),
            ("#06B6D4", "Cyan"),
            ("#E2E8F0", "White")
        };

        public StopwatchProfile Profile => _profile;

        public StopwatchEditDialog(StopwatchProfile? profile, ProcessMonitor monitor)
        {
            _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
            _isNew = profile == null;
            _profile = profile ?? new StopwatchProfile
            {
                Name = "",
                ColorHex = Palette[new Random().Next(Palette.Length)].Hex
            };

            _targetExe = _profile.TargetExe;
            _targetFriendlyName = _profile.TargetFriendlyName;
            _selectedColorHex = _profile.ColorHex;
            _selectedGraphScale = _profile.GraphScale == -1 ? 0 : _profile.GraphScale;
            _colorButtons = new Button[Palette.Length];

            InitializeComponents();
        }

        private void InitializeComponents()
        {
            this.Text = _isNew ? Loc.Get("StopwatchAdd") : Loc.Get("StopwatchEdit");
            this.Size = new Size(470, 500);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(20, 20, 26);
            this.ForeColor = Color.FromArgb(240, 240, 248);
            this.Font = new Font("Segoe UI", 9f);

            int pad = 20;
            int curY = pad;

            // 1. Name
            var lblName = new Label
            {
                Text = Loc.Get("StopwatchName"),
                Location = new Point(pad, curY),
                AutoSize = true,
                ForeColor = Color.FromArgb(170, 170, 185),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
            };
            this.Controls.Add(lblName);
            curY += 20;

            _txtName = new TextBox
            {
                Text = _profile.Name,
                Location = new Point(pad, curY),
                Size = new Size(this.ClientSize.Width - (pad * 2), 26),
                BackColor = Color.FromArgb(28, 28, 38),
                ForeColor = Color.FromArgb(240, 240, 248),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9.5f)
            };
            this.Controls.Add(_txtName);
            curY += 36;

            // 2. Color Palette
            var lblColor = new Label
            {
                Text = Loc.Get("StopwatchColor"),
                Location = new Point(pad, curY),
                AutoSize = true,
                ForeColor = Color.FromArgb(170, 170, 185),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
            };
            this.Controls.Add(lblColor);
            curY += 20;

            _pnlColorPalette.Location = new Point(pad, curY);
            _pnlColorPalette.Size = new Size(this.ClientSize.Width - (pad * 2), 34);
            _pnlColorPalette.BackColor = Color.Transparent;
            this.Controls.Add(_pnlColorPalette);

            int swW = 32;
            int swH = 30;
            int swGap = 6;
            for (int i = 0; i < Palette.Length; i++)
            {
                var entry = Palette[i];
                var c = StopwatchItem.ParseColor(entry.Hex, Color.White);

                var btn = new Button
                {
                    Location = new Point(i * (swW + swGap), 2),
                    Size = new Size(swW, swH),
                    BackColor = c,
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand,
                    Tag = entry.Hex
                };
                btn.FlatAppearance.BorderSize = 0;
                btn.Click += (s, e) => {
                    _selectedColorHex = (string)((Button)s!).Tag!;
                    UpdateColorHighlights();
                };
                _colorButtons[i] = btn;
                _pnlColorPalette.Controls.Add(btn);
            }

            // Custom Color Button
            _btnCustomColor = new Button
            {
                Location = new Point(Palette.Length * (swW + swGap), 2),
                Size = new Size(110, swH),
                Text = "🎨 " + Loc.Get("StopwatchCustomColor"),
                Font = new Font("Segoe UI", 7.75f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _btnCustomColor.Click += (s, e) => {
                using var cd = new ColorDialog
                {
                    Color = StopwatchItem.ParseColor(_selectedColorHex, Color.FromArgb(52, 211, 153)),
                    FullOpen = true,
                    AnyColor = true
                };
                if (cd.ShowDialog(this) == DialogResult.OK)
                {
                    _selectedColorHex = $"#{cd.Color.R:X2}{cd.Color.G:X2}{cd.Color.B:X2}";
                    UpdateColorHighlights();
                }
            };
            _pnlColorPalette.Controls.Add(_btnCustomColor);

            UpdateColorHighlights();
            curY += 44;

            // 3. Target Application Selection
            var lblTarget = new Label
            {
                Text = Loc.Get("StopwatchTargetApp"),
                Location = new Point(pad, curY),
                AutoSize = true,
                ForeColor = Color.FromArgb(170, 170, 185),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
            };
            this.Controls.Add(lblTarget);
            curY += 20;

            bool isSpecific = !string.IsNullOrWhiteSpace(_targetExe);

            _rbAllApps = new RadioButton
            {
                Text = Loc.Get("StopwatchAllAppsDesc"),
                Location = new Point(pad, curY),
                AutoSize = true,
                Checked = !isSpecific,
                ForeColor = Color.FromArgb(220, 220, 235),
                Font = new Font("Segoe UI", 9f)
            };
            _rbAllApps.CheckedChanged += (s, e) => UpdateAppSelectionUI();
            this.Controls.Add(_rbAllApps);
            curY += 26;

            _rbSpecificApp = new RadioButton
            {
                Text = Loc.Get("StopwatchSpecificAppDesc"),
                Location = new Point(pad, curY),
                AutoSize = true,
                Checked = isSpecific,
                ForeColor = Color.FromArgb(220, 220, 235),
                Font = new Font("Segoe UI", 9f)
            };
            _rbSpecificApp.CheckedChanged += (s, e) => UpdateAppSelectionUI();
            this.Controls.Add(_rbSpecificApp);
            curY += 28;

            _pnlAppSelect = new Panel
            {
                Location = new Point(pad + 20, curY),
                Size = new Size(this.ClientSize.Width - (pad * 2) - 20, 38),
                BackColor = Color.FromArgb(26, 26, 36)
            };
            this.Controls.Add(_pnlAppSelect);

            _lblTargetApp = new Label
            {
                Location = new Point(8, 9),
                Size = new Size(_pnlAppSelect.Width - 110, 20),
                ForeColor = Color.FromArgb(56, 189, 248),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                AutoEllipsis = true,
                Text = GetDisplayTargetText()
            };
            _pnlAppSelect.Controls.Add(_lblTargetApp);

            _btnPickApp = new Button
            {
                Text = Loc.Get("StopwatchSelectAppBtn"),
                Location = new Point(_pnlAppSelect.Width - 96, 6),
                Size = new Size(90, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(40, 40, 54),
                ForeColor = Color.FromArgb(240, 240, 248),
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnPickApp.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 80);
            _btnPickApp.Click += (s, e) => OpenProcessPicker();
            _pnlAppSelect.Controls.Add(_btnPickApp);

            curY += 46;
            UpdateAppSelectionUI();

            // 4. Show on CPU Graph Checkbox
            _chkShowOnCpuGraph = new CheckBox
            {
                Text = Loc.Get("StopwatchShowOnCpuGraph"),
                Location = new Point(pad, curY),
                Size = new Size(this.ClientSize.Width - (pad * 2), 24),
                Checked = _profile.ShowOnCpuGraph && _profile.GraphScale != -1,
                ForeColor = Color.FromArgb(220, 220, 235),
                Font = new Font("Segoe UI", 9f),
                Cursor = Cursors.Hand
            };
            this.Controls.Add(_chkShowOnCpuGraph);
            curY += 28;

            int[] scales = new[] { 0, 2, 4, 8, 16, 1 };
            string[] scaleLabels = new[] { Loc.Get("StopwatchScale_Auto"), "2x", "4x", "8x", "16x", "1x" };
            _scaleButtons = new Button[scales.Length];
            int scBtnX = pad + 20;
            int scBtnW = 46;
            int scBtnH = 26;

            for (int i = 0; i < scales.Length; i++)
            {
                int scVal = scales[i];
                var btn = new Button
                {
                    Text = scaleLabels[i],
                    Tag = scVal,
                    Location = new Point(scBtnX + i * (scBtnW + 6), curY),
                    Size = new Size(scBtnW, scBtnH),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand,
                    Font = new Font("Segoe UI", 8.0f, FontStyle.Bold)
                };
                btn.FlatAppearance.BorderSize = 1;
                btn.Click += (s, e) => {
                    _selectedGraphScale = scVal;
                    _chkShowOnCpuGraph.Checked = true;
                    UpdateScaleButtonsHighlight();
                };
                this.Controls.Add(btn);
                _scaleButtons[i] = btn;
            }
            _chkShowOnCpuGraph.CheckedChanged += (s, e) => UpdateScaleButtonsHighlight();
            UpdateScaleButtonsHighlight();

            // Bottom Buttons
            int btnW = 100;
            int btnH = 30;
            int btnY = this.ClientSize.Height - pad - btnH;

            var btnSave = new Button
            {
                Text = Loc.Get("Save"),
                Location = new Point(this.ClientSize.Width - pad - btnW * 2 - 10, btnY),
                Size = new Size(btnW, btnH),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(34, 197, 94),
                ForeColor = Color.FromArgb(18, 18, 22),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += (s, e) => SaveAndClose();
            this.Controls.Add(btnSave);

            var btnCancel = new Button
            {
                Text = Loc.Get("Cancel"),
                Location = new Point(this.ClientSize.Width - pad - btnW, btnY),
                Size = new Size(btnW, btnH),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(32, 32, 42),
                ForeColor = Color.FromArgb(200, 200, 215),
                Cursor = Cursors.Hand,
                DialogResult = DialogResult.Cancel
            };
            btnCancel.FlatAppearance.BorderColor = Color.FromArgb(50, 50, 65);
            btnCancel.Click += (s, e) => this.Close();
            this.Controls.Add(btnCancel);
        }

        private void UpdateColorHighlights()
        {
            bool presetMatched = false;
            for (int i = 0; i < Palette.Length; i++)
            {
                var btn = _colorButtons[i];
                string hex = (string)btn.Tag!;
                bool isSelected = string.Equals(hex, _selectedColorHex, StringComparison.OrdinalIgnoreCase);

                if (isSelected)
                {
                    presetMatched = true;
                    btn.FlatAppearance.BorderSize = 2;
                    btn.FlatAppearance.BorderColor = Color.White;
                }
                else
                {
                    btn.FlatAppearance.BorderSize = 1;
                    btn.FlatAppearance.BorderColor = Color.FromArgb(40, 40, 50);
                }
            }

            if (_btnCustomColor != null)
            {
                if (!presetMatched)
                {
                    var customCol = StopwatchItem.ParseColor(_selectedColorHex, Color.FromArgb(52, 211, 153));
                    _btnCustomColor.BackColor = customCol;
                    double luminance = (0.299 * customCol.R + 0.587 * customCol.G + 0.114 * customCol.B) / 255.0;
                    _btnCustomColor.ForeColor = luminance > 0.55 ? Color.FromArgb(18, 18, 22) : Color.White;
                    _btnCustomColor.FlatAppearance.BorderSize = 2;
                    _btnCustomColor.FlatAppearance.BorderColor = Color.White;
                }
                else
                {
                    _btnCustomColor.BackColor = Color.FromArgb(28, 28, 38);
                    _btnCustomColor.ForeColor = Color.FromArgb(200, 200, 220);
                    _btnCustomColor.FlatAppearance.BorderSize = 1;
                    _btnCustomColor.FlatAppearance.BorderColor = Color.FromArgb(48, 48, 62);
                }
            }
        }

        private void UpdateAppSelectionUI()
        {
            bool specific = _rbSpecificApp.Checked;
            _pnlAppSelect.Visible = specific;
            _lblTargetApp.Text = GetDisplayTargetText();
        }

        private string GetDisplayTargetText()
        {
            if (string.IsNullOrWhiteSpace(_targetExe))
            {
                return Loc.Get("StopwatchNoneSelected");
            }
            if (!string.IsNullOrWhiteSpace(_targetFriendlyName) && !_targetFriendlyName.Equals(_targetExe, StringComparison.OrdinalIgnoreCase))
            {
                return $"{_targetFriendlyName} ({_targetExe})";
            }
            return _targetExe;
        }

        private void OpenProcessPicker()
        {
            using var dlg = new ProcessSelectDialog(_monitor, _targetExe);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                if (string.IsNullOrEmpty(dlg.SelectedExeName))
                {
                    _targetExe = "";
                    _targetFriendlyName = "";
                    _rbAllApps.Checked = true;
                }
                else
                {
                    _targetExe = dlg.SelectedExeName;
                    _targetFriendlyName = dlg.SelectedFriendlyName;
                    _rbSpecificApp.Checked = true;
                }
                UpdateAppSelectionUI();
            }
        }

        private void UpdateScaleButtonsHighlight()
        {
            bool enabled = _chkShowOnCpuGraph.Checked;
            foreach (var btn in _scaleButtons)
            {
                btn.Enabled = enabled;
                if (btn.Tag is int val && val == _selectedGraphScale && enabled)
                {
                    btn.BackColor = Color.FromArgb(38, 54, 75);
                    btn.ForeColor = Color.FromArgb(56, 189, 248);
                    btn.FlatAppearance.BorderColor = Color.FromArgb(56, 189, 248);
                }
                else
                {
                    btn.BackColor = Color.FromArgb(28, 28, 36);
                    btn.ForeColor = enabled ? Color.FromArgb(150, 150, 165) : Color.FromArgb(80, 80, 95);
                    btn.FlatAppearance.BorderColor = Color.FromArgb(44, 44, 56);
                }
            }
        }

        private void SaveAndClose()
        {
            string name = _txtName.Text.Trim();
            _profile.Name = name;
            _profile.ColorHex = _selectedColorHex;
            _profile.ShowOnCpuGraph = _chkShowOnCpuGraph.Checked;
            _profile.GraphScale = _chkShowOnCpuGraph.Checked ? _selectedGraphScale : -1;

            if (_rbAllApps.Checked)
            {
                _profile.TargetExe = "";
                _profile.TargetFriendlyName = "";
            }
            else
            {
                _profile.TargetExe = _targetExe;
                _profile.TargetFriendlyName = _targetFriendlyName;
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
