using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RyzenQuietPro
{
    public class FanSelectionForm : Form
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

        private Panel _pnlList = null!;
        private readonly List<(FanItem Fan, CheckBox CheckBox, Label RpmLabel)> _fanControls = new();

        public FanSelectionForm(
            HardwareMonitor hardware,
            AppSettings settings,
            Action onSettingsUpdated)
        {
            _hardware = hardware;
            _settings = settings;
            _onSettingsUpdated = onSettingsUpdated;

            this.AutoScaleMode = AutoScaleMode.None;
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.ShowInTaskbar = false;
            this.ClientSize = new Size(460, 520);
            this.MinimumSize = new Size(460, 520);
            this.BackColor = Color.FromArgb(20, 20, 24);
            this.ForeColor = Color.White;
            this.DoubleBuffered = true;
            this.Icon = AppIcons.QuietIcon;

            InitializeComponents();

            _hardware.Fans.FansUpdated += OnFansLiveUpdated;
            this.FormClosed += (s, e) => {
                _hardware.Fans.FansUpdated -= OnFansLiveUpdated;
            };

            this.Paint += (s, e) => {
                using var pen = new Pen(Color.FromArgb(50, 50, 65), 1f);
                e.Graphics.DrawRectangle(pen, 0, 0, this.ClientSize.Width - 1, this.ClientSize.Height - 1);
            };
        }

        private void InitializeComponents()
        {
            int w = this.ClientSize.Width;
            int cardW = w - 28;

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

            var lblTitle = new Label
            {
                Text = "❄️ " + Loc.Get("FanSelectionTitle"),
                Font = new Font("Segoe UI", 9.75f, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 245),
                Location = new Point(38, 9),
                AutoSize = true,
                Cursor = Cursors.SizeAll
            };
            lblTitle.MouseDown += OnHeaderDrag;
            header.Controls.Add(lblTitle);

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

            // ================= HINT CARD =================
            var cardHint = new Panel
            {
                Location = new Point(14, curY),
                Size = new Size(cardW, 68),
                BackColor = Color.FromArgb(26, 26, 34)
            };
            cardHint.Paint += (s, e) => {
                using var p = new Pen(Color.FromArgb(42, 44, 56), 1f);
                e.Graphics.DrawRectangle(p, 0, 0, cardHint.Width - 1, cardHint.Height - 1);
            };
            this.Controls.Add(cardHint);

            var lblHint = new Label
            {
                Text = Loc.Get("FanSelectionHint"),
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = Color.FromArgb(170, 175, 195),
                Location = new Point(10, 8),
                Size = new Size(cardW - 20, 52),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = false
            };
            cardHint.Controls.Add(lblHint);

            curY += 76;

            // ================= TOP ACTION BUTTONS =================
            var btnSelectAll = new Button
            {
                Text = "✓ " + Loc.Get("FanSelectAll"),
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                BackColor = Color.FromArgb(28, 36, 48),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(108, 26),
                Location = new Point(14, curY),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = Padding.Empty
            };
            btnSelectAll.FlatAppearance.BorderColor = Color.FromArgb(45, 75, 100);
            btnSelectAll.Click += (s, e) => SetAllSelection(true);
            this.Controls.Add(btnSelectAll);

            var btnDeselectAll = new Button
            {
                Text = "✕ " + Loc.Get("FanDeselectAll"),
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(160, 165, 180),
                BackColor = Color.FromArgb(30, 30, 38),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(108, 26),
                Location = new Point(128, curY),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = Padding.Empty
            };
            btnDeselectAll.FlatAppearance.BorderColor = Color.FromArgb(50, 52, 65);
            btnDeselectAll.Click += (s, e) => SetAllSelection(false);
            this.Controls.Add(btnDeselectAll);

            var btnTestGpu = new Button
            {
                Text = Loc.Get("FanGpuSpinTest"),
                Font = new Font("Segoe UI", 7.75f, FontStyle.Bold),
                ForeColor = Color.FromArgb(249, 115, 22),
                BackColor = Color.FromArgb(36, 28, 24),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(196, 26),
                Location = new Point(242, curY),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = Padding.Empty
            };
            btnTestGpu.FlatAppearance.BorderColor = Color.FromArgb(120, 60, 20);
            btnTestGpu.Click += async (s, e) => {
                btnTestGpu.Enabled = false;
                btnTestGpu.Text = Loc.Get("FanGpuSpinTesting");
                btnTestGpu.BackColor = Color.FromArgb(48, 32, 20);
                _hardware.Fans.TriggerGpuFanTest();
                await Task.Delay(10000);
                if (!btnTestGpu.IsDisposed)
                {
                    btnTestGpu.Text = Loc.Get("FanGpuSpinTest");
                    btnTestGpu.BackColor = Color.FromArgb(36, 28, 24);
                    btnTestGpu.Enabled = true;
                }
            };
            this.Controls.Add(btnTestGpu);

            curY += 34;

            // ================= FAN LIST CONTAINER =================
            int listH = this.ClientSize.Height - curY - 48;
            _pnlList = new Panel
            {
                Location = new Point(14, curY),
                Size = new Size(cardW, listH),
                BackColor = Color.FromArgb(22, 22, 28),
                AutoScroll = true
            };
            _pnlList.Paint += (s, e) => {
                using var p = new Pen(Color.FromArgb(38, 40, 52), 1f);
                e.Graphics.DrawRectangle(p, 0, 0, _pnlList.Width - 1, _pnlList.Height - 1);
            };
            this.Controls.Add(_pnlList);

            BuildFanListRows();

            // ================= FOOTER =================
            int footerY = this.ClientSize.Height - 40;
            var btnDone = new Button
            {
                Text = Loc.Get("InfoClose"),
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(14, 165, 233),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(cardW, 30),
                Location = new Point(14, footerY),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = Padding.Empty
            };
            btnDone.FlatAppearance.BorderSize = 0;
            btnDone.Click += (s, e) => this.Close();
            this.Controls.Add(btnDone);
        }

        private void BuildFanListRows()
        {
            _pnlList.Controls.Clear();
            _fanControls.Clear();

            var fans = _hardware.Fans.Fans;
            if (fans.Count == 0)
            {
                var lblEmpty = new Label
                {
                    Text = Loc.Get("FanSelectionNoLiveFans"),
                    Font = new Font("Segoe UI", 8.5f),
                    ForeColor = Color.FromArgb(140, 145, 165),
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter
                };
                _pnlList.Controls.Add(lblEmpty);
                return;
            }

            int rowH = 46;
            int rowGap = 4;
            // Subtract VerticalScrollBarWidth + margin so child never triggers horizontal scrollbar
            int itemW = _pnlList.Width - SystemInformation.VerticalScrollBarWidth - 16;

            for (int i = 0; i < fans.Count; i++)
            {
                var fan = fans[i];
                int itemY = 6 + i * (rowH + rowGap);

                var rowCard = new Panel
                {
                    Location = new Point(6, itemY),
                    Size = new Size(itemW, rowH),
                    BackColor = Color.FromArgb(28, 29, 38),
                    Cursor = Cursors.Hand
                };

                // Left color stripe
                var stripe = new Panel
                {
                    Location = new Point(0, 0),
                    Size = new Size(4, rowH),
                    BackColor = fan.Color
                };
                rowCard.Controls.Add(stripe);

                bool isVisible = _settings.IsFanVisible(fan.Id, fan.Name);

                var chk = new CheckBox
                {
                    Text = FormatFanDisplayName(fan),
                    Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(235, 238, 245),
                    Location = new Point(14, 6),
                    Size = new Size(itemW - 130, 34),
                    Checked = isVisible,
                    Cursor = Cursors.Hand
                };
                chk.CheckedChanged += (s, e) => {
                    _settings.SetFanVisibility(fan.Id, chk.Checked);
                    _settings.Save();
                    _onSettingsUpdated?.Invoke();
                };
                rowCard.Controls.Add(chk);

                // Live RPM label on right
                var lblRpm = new Label
                {
                    Text = FormatRpmText(fan.CurrentRpm),
                    Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                    ForeColor = fan.CurrentRpm > 0 ? Color.FromArgb(74, 222, 128) : Color.FromArgb(130, 135, 150),
                    Location = new Point(itemW - 120, 14),
                    Size = new Size(112, 20),
                    TextAlign = ContentAlignment.MiddleRight,
                    Cursor = Cursors.Hand
                };
                lblRpm.Click += (s, e) => chk.Checked = !chk.Checked;
                rowCard.Controls.Add(lblRpm);

                rowCard.Click += (s, e) => chk.Checked = !chk.Checked;

                rowCard.Paint += (s, e) => {
                    using var pen = new Pen(Color.FromArgb(42, 44, 58), 1f);
                    e.Graphics.DrawRectangle(pen, 0, 0, rowCard.Width - 1, rowCard.Height - 1);
                };

                _pnlList.Controls.Add(rowCard);
                _fanControls.Add((fan, chk, lblRpm));
            }
        }

        private static string FormatFanDisplayName(FanItem fan)
        {
            string cleanName = fan.Name.Trim();
            if (cleanName.EndsWith(" Fan", StringComparison.OrdinalIgnoreCase))
            {
                cleanName = cleanName.Substring(0, cleanName.Length - 4).Trim();
            }

            string hw = fan.Hardware.Trim();
            if (hw.StartsWith("NVIDIA", StringComparison.OrdinalIgnoreCase)) hw = "GPU";
            else if (hw.StartsWith("AMD", StringComparison.OrdinalIgnoreCase)) hw = "GPU";
            else if (hw.Contains("Motherboard", StringComparison.OrdinalIgnoreCase)) hw = "Motherboard";
            else if (hw.Length > 18) hw = hw.Substring(0, 18);

            return $"{cleanName} ({hw})";
        }

        private static string FormatRpmText(int rpm)
        {
            return rpm > 0 ? $"{rpm:N0} RPM" : "0 RPM (Idle)";
        }

        private void SetAllSelection(bool visible)
        {
            foreach (var item in _fanControls)
            {
                item.CheckBox.Checked = visible;
                _settings.SetFanVisibility(item.Fan.Id, visible);
            }
            _settings.Save();
            _onSettingsUpdated?.Invoke();
        }

        private void OnFansLiveUpdated()
        {
            if (this.IsDisposed || !this.IsHandleCreated) return;

            try
            {
                this.BeginInvoke((Action)(() => {
                    if (this.IsDisposed) return;
                    foreach (var item in _fanControls)
                    {
                        item.RpmLabel.Text = FormatRpmText(item.Fan.CurrentRpm);
                        item.RpmLabel.ForeColor = item.Fan.CurrentRpm > 0 ? Color.FromArgb(74, 222, 128) : Color.FromArgb(130, 135, 150);
                    }
                }));
            }
            catch { }
        }

        private void OnHeaderDrag(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(this.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }
    }
}
