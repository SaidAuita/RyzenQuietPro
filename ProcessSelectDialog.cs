using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace RyzenQuietPro
{
    public class ProcessSelectDialog : Form
    {
        private readonly ProcessMonitor _monitor;
        private TextBox _txtSearch = null!;
        private ListView _lvProcesses = null!;
        private ImageList _imageList = null!;
        private Button _btnSelect = null!;
        private Button _btnCancel = null!;
        private Button _btnAnyApp = null!;
        private Label _lblStatus = null!;

        private List<ProcessAppGroup> _allGroups = new();

        public string SelectedExeName { get; private set; } = "";
        public string SelectedFriendlyName { get; private set; } = "";

        public ProcessSelectDialog(ProcessMonitor monitor, string currentExe = "")
        {
            _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
            SelectedExeName = currentExe;

            InitializeComponents();
            LoadProcesses();
        }

        private void InitializeComponents()
        {
            this.Text = Loc.Get("StopwatchSelectApp");
            this.Size = new Size(560, 520);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(20, 20, 26);
            this.ForeColor = Color.FromArgb(240, 240, 248);
            this.Font = new Font("Segoe UI", 9f, FontStyle.Regular);

            int pad = 16;
            int curY = pad;

            // Search Box
            var lblSearch = new Label
            {
                Text = Loc.Get("StopwatchSearchProc"),
                Location = new Point(pad, curY),
                AutoSize = true,
                ForeColor = Color.FromArgb(160, 160, 180),
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold)
            };
            this.Controls.Add(lblSearch);
            curY += 20;

            _txtSearch = new TextBox
            {
                Location = new Point(pad, curY),
                Size = new Size(this.ClientSize.Width - (pad * 2), 26),
                BackColor = Color.FromArgb(28, 28, 38),
                ForeColor = Color.FromArgb(240, 240, 248),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9.5f)
            };
            _txtSearch.TextChanged += (s, e) => FilterList();
            this.Controls.Add(_txtSearch);
            curY += 34;

            // ListView for grouped applications
            _imageList = new ImageList
            {
                ImageSize = new Size(20, 20),
                ColorDepth = ColorDepth.Depth32Bit
            };

            int lvH = this.ClientSize.Height - curY - 74;
            _lvProcesses = new ListView
            {
                Location = new Point(pad, curY),
                Size = new Size(this.ClientSize.Width - (pad * 2), lvH),
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                GridLines = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BackColor = Color.FromArgb(26, 26, 34),
                ForeColor = Color.FromArgb(240, 240, 248),
                BorderStyle = BorderStyle.FixedSingle,
                SmallImageList = _imageList
            };

            _lvProcesses.Columns.Add(Loc.Get("StopwatchColApp"), 280);
            _lvProcesses.Columns.Add(Loc.Get("StopwatchColProcs"), 75, HorizontalAlignment.Right);
            _lvProcesses.Columns.Add("CPU", 65, HorizontalAlignment.Right);
            _lvProcesses.Columns.Add("RAM", 75, HorizontalAlignment.Right);

            _lvProcesses.ItemActivate += (s, e) => ConfirmSelection();
            _lvProcesses.SelectedIndexChanged += (s, e) => {
                _btnSelect.Enabled = _lvProcesses.SelectedItems.Count > 0;
            };

            this.Controls.Add(_lvProcesses);
            curY += lvH + 8;

            // Status label
            _lblStatus = new Label
            {
                Location = new Point(pad, curY),
                Size = new Size(240, 22),
                ForeColor = Color.FromArgb(140, 140, 160),
                Font = new Font("Segoe UI", 8.25f),
                TextAlign = ContentAlignment.MiddleLeft
            };
            this.Controls.Add(_lblStatus);

            // Bottom Buttons
            int btnW = 100;
            int btnH = 28;
            int btnY = curY;

            _btnAnyApp = new Button
            {
                Text = Loc.Get("StopwatchAllApps"),
                Size = new Size(130, btnH),
                Location = new Point(this.ClientSize.Width - pad - btnW * 2 - 140, btnY),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(38, 38, 50),
                ForeColor = Color.FromArgb(56, 189, 248),
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
            };
            _btnAnyApp.FlatAppearance.BorderColor = Color.FromArgb(56, 189, 248);
            _btnAnyApp.Click += (s, e) => {
                SelectedExeName = "";
                SelectedFriendlyName = "";
                this.DialogResult = DialogResult.OK;
                this.Close();
            };
            this.Controls.Add(_btnAnyApp);

            _btnSelect = new Button
            {
                Text = Loc.Get("Select"),
                Size = new Size(btnW, btnH),
                Location = new Point(this.ClientSize.Width - pad - btnW * 2 - 8, btnY),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(34, 197, 94),
                ForeColor = Color.FromArgb(18, 18, 22),
                Cursor = Cursors.Hand,
                Enabled = false,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            _btnSelect.FlatAppearance.BorderSize = 0;
            _btnSelect.Click += (s, e) => ConfirmSelection();
            this.Controls.Add(_btnSelect);

            _btnCancel = new Button
            {
                Text = Loc.Get("Cancel"),
                Size = new Size(btnW, btnH),
                Location = new Point(this.ClientSize.Width - pad - btnW, btnY),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(32, 32, 42),
                ForeColor = Color.FromArgb(200, 200, 215),
                Cursor = Cursors.Hand,
                DialogResult = DialogResult.Cancel
            };
            _btnCancel.FlatAppearance.BorderColor = Color.FromArgb(50, 50, 65);
            _btnCancel.Click += (s, e) => this.Close();
            this.Controls.Add(_btnCancel);
        }

        private void LoadProcesses()
        {
            try
            {
                _allGroups = _monitor.GetRunningAppGroups();
                FilterList();
            }
            catch (Exception ex)
            {
                Logger.Log($"ProcessSelectDialog load error: {ex.Message}");
            }
        }

        private void FilterList()
        {
            _lvProcesses.BeginUpdate();
            _lvProcesses.Items.Clear();
            _imageList.Images.Clear();

            string query = _txtSearch.Text.Trim();
            int iconIndex = 0;

            var filtered = string.IsNullOrEmpty(query)
                ? _allGroups
                : _allGroups.Where(g =>
                    g.ExeName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    g.FriendlyName.Contains(query, StringComparison.OrdinalIgnoreCase)
                ).ToList();

            foreach (var group in filtered)
            {
                if (group.Icon != null)
                {
                    _imageList.Images.Add(group.Icon);
                }
                else
                {
                    // Fallback empty icon
                    var bmp = new Bitmap(20, 20);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.FromArgb(45, 45, 60));
                    }
                    _imageList.Images.Add(bmp);
                }

                string title = string.IsNullOrWhiteSpace(group.FriendlyName) || group.FriendlyName.Equals(group.ExeName, StringComparison.OrdinalIgnoreCase)
                    ? group.ExeName
                    : $"{group.FriendlyName} ({group.ExeName})";

                var lvi = new ListViewItem(title, iconIndex++)
                {
                    Tag = group
                };

                lvi.SubItems.Add(group.ProcessCount > 1 ? $"{group.ProcessCount}" : "1");
                lvi.SubItems.Add(group.TotalCpuPercent > 0.1 ? $"{group.TotalCpuPercent:F1}%" : "0%");
                lvi.SubItems.Add(group.RamFormatted);

                if (!string.IsNullOrEmpty(SelectedExeName) && group.ExeName.Equals(SelectedExeName, StringComparison.OrdinalIgnoreCase))
                {
                    lvi.Selected = true;
                    lvi.EnsureVisible();
                }

                _lvProcesses.Items.Add(lvi);
            }

            _lvProcesses.EndUpdate();
            _lblStatus.Text = $"{filtered.Count} / {_allGroups.Count} {Loc.Get("StopwatchAppsFound")}";
            _btnSelect.Enabled = _lvProcesses.SelectedItems.Count > 0;
        }

        private void ConfirmSelection()
        {
            if (_lvProcesses.SelectedItems.Count == 0) return;

            if (_lvProcesses.SelectedItems[0].Tag is ProcessAppGroup group)
            {
                SelectedExeName = group.ExeName;
                SelectedFriendlyName = !string.IsNullOrWhiteSpace(group.FriendlyName) ? group.FriendlyName : group.ExeName;
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        }
    }
}
