using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RyzenQuietPro
{
    public class HardwareInfoForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        public HardwareInfoForm(HardwareMonitor hardware)
        {
            this.AutoScaleMode = AutoScaleMode.None;
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.ShowInTaskbar = false;
            this.ClientSize = new Size(500, 545);
            this.BackColor = Color.FromArgb(20, 20, 24);
            this.DoubleBuffered = true;
            this.Icon = AppIcons.QuietIcon;

            // Header Panel
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 38,
                BackColor = Color.FromArgb(14, 14, 18),
                Cursor = Cursors.SizeAll
            };
            header.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Left)
                {
                    ReleaseCapture();
                    SendMessage(this.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
                }
            };
            this.Controls.Add(header);

            var picIcon = new PictureBox
            {
                Location = new Point(12, 9),
                Size = new Size(20, 20),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = AppIcons.QuietBitmap,
                Cursor = Cursors.SizeAll
            };
            picIcon.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Left)
                {
                    ReleaseCapture();
                    SendMessage(this.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
                }
            };
            header.Controls.Add(picIcon);

            var lblTitle = new Label
            {
                Text = Loc.Get("InfoTitle"),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 245),
                Location = new Point(38, 9),
                AutoSize = true,
                Cursor = Cursors.SizeAll
            };
            lblTitle.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Left)
                {
                    ReleaseCapture();
                    SendMessage(this.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
                }
            };
            header.Controls.Add(lblTitle);

            var btnClose = new Label
            {
                Text = "✕",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(160, 160, 175),
                Size = new Size(26, 26),
                Location = new Point(this.ClientSize.Width - 34, 6),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            btnClose.MouseEnter += (s, e) => btnClose.ForeColor = Color.White;
            btnClose.MouseLeave += (s, e) => btnClose.ForeColor = Color.FromArgb(160, 160, 175);
            btnClose.Click += (s, e) => this.Close();
            header.Controls.Add(btnClose);

            int contentW = this.ClientSize.Width - 28;
            int curY = 48;

            // 1. Detected Hardware Card
            var pnlDetected = new Panel
            {
                Location = new Point(14, curY),
                Size = new Size(contentW, 185),
                BackColor = Color.FromArgb(26, 26, 32)
            };
            this.Controls.Add(pnlDetected);

            var lblSec1 = new Label
            {
                Text = Loc.Get("InfoDetected"),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                Location = new Point(12, 10),
                AutoSize = true
            };
            pnlDetected.Controls.Add(lblSec1);

            int itemY = 34;
            void AddDetectedItem(string icon, string title, string val, string status, Color statusColor)
            {
                var lblI = new Label
                {
                    Text = icon,
                    Font = new Font("Segoe UI", 9.5f),
                    ForeColor = Color.FromArgb(210, 210, 220),
                    Location = new Point(12, itemY),
                    Size = new Size(22, 22)
                };
                var lblT = new Label
                {
                    Text = title,
                    Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(220, 220, 230),
                    Location = new Point(36, itemY),
                    Size = new Size(50, 22)
                };
                var lblV = new Label
                {
                    Text = val,
                    Font = new Font("Segoe UI", 9f),
                    ForeColor = Color.FromArgb(200, 200, 215),
                    Location = new Point(88, itemY),
                    Size = new Size(contentW - 190, 22)
                };
                var lblS = new Label
                {
                    Text = status,
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                    ForeColor = statusColor,
                    Location = new Point(contentW - 100, itemY),
                    Size = new Size(90, 22),
                    TextAlign = ContentAlignment.MiddleRight
                };
                pnlDetected.Controls.AddRange(new Control[] { lblI, lblT, lblV, lblS });
                itemY += 28;
            }

            string cpuStr = hardware.Cpu.CpuName;
            AddDetectedItem("🔲", Loc.Get("Cpu") + ":", $"{cpuStr} ({hardware.Cpu.CoreCount} {Loc.Get("Threads")})", "Supported ✅", Color.FromArgb(80, 220, 140));
            string ramDesc = !string.IsNullOrEmpty(hardware.Ram.MemorySpecs) ? $"{hardware.Ram.TotalGb:F1} GB {hardware.Ram.MemorySpecs}" : $"{hardware.Ram.TotalGb:F1} GB";
            AddDetectedItem("🧠", Loc.Get("Ram") + ":", $"{ramDesc} {Loc.Get("SysMemory")}", "Active ✅", Color.FromArgb(56, 189, 248));
            string vramTypeDesc = !string.IsNullOrEmpty(hardware.Gpu.VramType) ? $" {hardware.Gpu.VramType}" : "";
            if (hardware.Gpu.GpuCount > 1)
            {
                AddDetectedItem("🎮", Loc.Get("Gpu") + ":", $"{hardware.Gpu.GpuCount} GPUs ({hardware.Gpu.GpuName})", "NVML ✅", Color.FromArgb(168, 85, 247));
                AddDetectedItem("📊", Loc.Get("Vram") + ":", $"{hardware.Gpu.VramTotalGb:F1} GB{vramTypeDesc} {Loc.Get("DedMemory")}", "Active ✅", Color.FromArgb(236, 72, 153));
            }
            else
            {
                AddDetectedItem("🎮", Loc.Get("Gpu") + ":", $"{hardware.Gpu.GpuName}", "NVML ✅", Color.FromArgb(168, 85, 247));
                AddDetectedItem("📊", Loc.Get("Vram") + ":", $"{hardware.Gpu.VramTotalGb:F1} GB{vramTypeDesc} {Loc.Get("DedMemory")}", "Active ✅", Color.FromArgb(236, 72, 153));
            }
            AddDetectedItem("💽", Loc.Get("Disk") + ":", $"{hardware.Disk.DriveCount} {Loc.Get("PhysicalDrives")} (SSD/HDD)", "Active ✅", Color.FromArgb(6, 182, 212));

            curY += 195;

            // 2. Supported Hardware Compatibility Card
            var pnlSupport = new Panel
            {
                Location = new Point(14, curY),
                Size = new Size(contentW, 195),
                BackColor = Color.FromArgb(26, 26, 32)
            };
            this.Controls.Add(pnlSupport);

            var lblSec2 = new Label
            {
                Text = Loc.Get("InfoSupported"),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(80, 220, 140),
                Location = new Point(12, 10),
                AutoSize = true
            };
            pnlSupport.Controls.Add(lblSec2);

            string[] supportItems = new[]
            {
                "• AMD Ryzen™: Zen 1/+/2/3/4/5 (1000 - 9000 series, Threadripper, Mobile APU)",
                "• Intel® Core™: 6th - 14th Gen & Ultra (CPB/Turbo throttling power limits)",
                "• NVIDIA® GeForce: RTX 20/30/40/50, GTX 900-1600 (Native NVML Telemetry)",
                "• AMD® Radeon: RX 400 - 7000 series (ADL API & DXGI fallback)",
                "• Storage: NVMe SSD, SATA SSD & HDD (Direct PhysicalDisk Telemetry)",
                "• OS Support: Windows 10 & 11 (64-bit)"
            };

            int sY = 32;
            foreach (var item in supportItems)
            {
                var lblLine = new Label
                {
                    Text = item,
                    Font = new Font("Segoe UI", 8.5f),
                    ForeColor = Color.FromArgb(180, 180, 195),
                    Location = new Point(14, sY),
                    Size = new Size(contentW - 28, 22)
                };
                pnlSupport.Controls.Add(lblLine);
                sY += 24;
            }

            curY += 205;

            // 3. Footer with Close button
            var pnlFooter = new Panel
            {
                Location = new Point(14, curY),
                Size = new Size(contentW, 46),
                BackColor = Color.Transparent
            };
            this.Controls.Add(pnlFooter);

            var btnOk = new Button
            {
                Text = Loc.Get("InfoClose"),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(38, 38, 48),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(80, 30),
                Location = new Point(contentW - 80, 7),
                Cursor = Cursors.Hand
            };
            btnOk.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 75);
            btnOk.Click += (s, e) => this.Close();
            pnlFooter.Controls.Add(btnOk);

            this.Paint += (s, e) => {
                using var pen = new Pen(Color.FromArgb(50, 50, 65));
                e.Graphics.DrawRectangle(pen, 0, 0, this.ClientSize.Width - 1, this.ClientSize.Height - 1);
            };
        }
    }
}
