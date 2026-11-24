using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RyzenQuietPro
{
    public class TranslationPopupForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const byte VK_CONTROL = 0x11;
        private const byte VK_V = 0x56;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int WM_NCHITTEST = 0x84;
        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        private readonly IntPtr _targetWindow;
        private readonly string? _originalText;
        private readonly byte[]? _imageBytes;
        private readonly bool _isOcr;
        private readonly bool _andTranslate;

        private Panel _pnlHeader = null!;
        private Label _lblTitle = null!;
        private Label _lblModelPill = null!;
        private ComboBox _cbTargetLang = null!;
        private Button _btnClose = null!;
        private Label _lblSourceSnippet = null!;
        private TextBox _tbResult = null!;
        private Label _lblLatency = null!;
        private Button _btnReplace = null!;
        private Button _btnCopy = null!;
        private Label _lblResizeGrip = null!;
        private ToolTip _toolTip = null!;

        private string _translatedResult = string.Empty;
        private CancellationTokenSource? _cts;

        public TranslationPopupForm(
            string? text,
            byte[]? imageBytes,
            bool isOcr,
            bool andTranslate,
            IntPtr targetWindow)
        {
            _originalText = text;
            _imageBytes = imageBytes;
            _isOcr = isOcr;
            _andTranslate = andTranslate;
            _targetWindow = targetWindow;

            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.DoubleBuffered = true;

            var settings = AppSettings.Load();
            int initW = Math.Clamp(settings.TranslatePopupWidth, 380, 1600);
            int initH = Math.Clamp(settings.TranslatePopupHeight, 220, 1200);
            this.Size = new Size(initW, initH);
            this.MinimumSize = new Size(380, 220);
            this.MaximumSize = new Size(1600, 1200);

            this.BackColor = Color.FromArgb(26, 28, 35);
            this.KeyPreview = true;

            InitializeComponents();
            PositionNearCursor();
            StartExecution();
        }

        private void InitializeComponents()
        {
            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;

            // Header Panel
            _pnlHeader = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(w, 40),
                BackColor = Color.FromArgb(22, 24, 30),
                Cursor = Cursors.SizeAll
            };
            _pnlHeader.MouseDown += Header_MouseDown;

            string title = _isOcr
                ? (_andTranslate ? "📷 Распознавание и перевод" : "📷 Распознавание текста (OCR)")
                : "🌐 Быстрый перевод";

            _lblTitle = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(12, 10),
                AutoSize = true,
                Cursor = Cursors.SizeAll
            };
            _lblTitle.MouseDown += Header_MouseDown;
            _pnlHeader.Controls.Add(_lblTitle);

            // Close button
            _btnClose = new Button
            {
                Text = "✕",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(26, 26),
                Location = new Point(w - 32, 7),
                Cursor = Cursors.Hand
            };
            _btnClose.FlatAppearance.BorderSize = 0;
            _btnClose.MouseEnter += (s, e) => { _btnClose.BackColor = Color.FromArgb(220, 38, 38); _btnClose.ForeColor = Color.White; };
            _btnClose.MouseLeave += (s, e) => { _btnClose.BackColor = Color.Transparent; _btnClose.ForeColor = Color.FromArgb(148, 163, 184); };
            _btnClose.Click += (s, e) => this.Close();
            _pnlHeader.Controls.Add(_btnClose);

            // Target Language ComboBox
            _cbTargetLang = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(36, 41, 54),
                ForeColor = Color.FromArgb(226, 232, 240),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f),
                Size = new Size(130, 24),
                Location = new Point(w - 170, 8),
                Cursor = Cursors.Hand
            };

            _cbTargetLang.Items.Add("⚡ Автовыбор (auto)");
            foreach (var lang in TranslationService.SupportedLanguages)
            {
                _cbTargetLang.Items.Add($"{lang.LocalName} ({lang.Code})");
            }

            // Select current setting
            string curLang = AppSettings.Load().TranslateTargetLang ?? "ru";
            int selIdx = 0;
            if (string.Equals(curLang, "auto", StringComparison.OrdinalIgnoreCase))
            {
                selIdx = 0;
            }
            else
            {
                for (int i = 0; i < TranslationService.SupportedLanguages.Length; i++)
                {
                    if (TranslationService.SupportedLanguages[i].Code.Equals(curLang, StringComparison.OrdinalIgnoreCase))
                    {
                        selIdx = i + 1;
                        break;
                    }
                }
            }
            _cbTargetLang.SelectedIndex = selIdx;
            _cbTargetLang.SelectedIndexChanged += (s, e) =>
            {
                try
                {
                    string newTargetLang = GetSelectedTargetLang();
                    var sSettings = AppSettings.Load();
                    if (!string.Equals(sSettings.TranslateTargetLang, newTargetLang, StringComparison.OrdinalIgnoreCase))
                    {
                        sSettings.TranslateTargetLang = newTargetLang;
                        sSettings.Save();
                        TranslationService.Instance.UpdateSettings(sSettings);
                    }
                }
                catch { }
                StartExecution();
            };
            _pnlHeader.Controls.Add(_cbTargetLang);

            // Model pill
            _lblModelPill = new Label
            {
                Text = "LLM",
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                BackColor = Color.FromArgb(30, 41, 59),
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(65, 22),
                Location = new Point(w - 242, 9)
            };
            _pnlHeader.Controls.Add(_lblModelPill);

            this.Controls.Add(_pnlHeader);

            // Source snippet preview
            _lblSourceSnippet = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 8f, FontStyle.Italic),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(14, 46),
                Size = new Size(w - 28, 20),
                AutoEllipsis = true
            };
            if (!string.IsNullOrEmpty(_originalText))
            {
                string snip = _originalText.Replace("\r", " ").Replace("\n", " ").Trim();
                if (snip.Length > 80) snip = snip.Substring(0, 80) + "...";
                _lblSourceSnippet.Text = $"Исходный: {snip}";
            }
            else if (_isOcr)
            {
                _lblSourceSnippet.Text = "Источник: Снимок экрана";
            }
            this.Controls.Add(_lblSourceSnippet);

            // Main Text Area
            _tbResult = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(18, 20, 26),
                ForeColor = Color.FromArgb(241, 245, 249),
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(14, 70),
                Size = new Size(w - 28, h - 124)
            };
            this.Controls.Add(_tbResult);

            // Bottom bar: Latency
            _lblLatency = new Label
            {
                Text = "⏳ Обработка...",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(16, 185, 129),
                Location = new Point(14, h - 42),
                Size = new Size(160, 28),
                TextAlign = ContentAlignment.MiddleLeft
            };
            this.Controls.Add(_lblLatency);

            // Replace button
            _btnReplace = new Button
            {
                Text = "Вставить (Ctrl+Enter)",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(203, 213, 225),
                BackColor = Color.FromArgb(36, 41, 54),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(140, 28),
                Location = new Point(w - 290, h - 42),
                Cursor = Cursors.Hand
            };
            _btnReplace.FlatAppearance.BorderColor = Color.FromArgb(51, 65, 85);
            _btnReplace.Click += (s, e) => ReplaceInTargetWindow();
            this.Controls.Add(_btnReplace);

            // Copy button
            _btnCopy = new Button
            {
                Text = "Копировать (Enter)",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(37, 99, 235),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(130, 28),
                Location = new Point(w - 144, h - 42),
                Cursor = Cursors.Hand
            };
            _btnCopy.FlatAppearance.BorderSize = 0;
            _btnCopy.MouseEnter += (s, e) => _btnCopy.BackColor = Color.FromArgb(29, 78, 216);
            _btnCopy.MouseLeave += (s, e) => _btnCopy.BackColor = Color.FromArgb(37, 99, 235);
            _btnCopy.Click += (s, e) => CopyAndClose();
            this.Controls.Add(_btnCopy);

            // Resize Grip
            _lblResizeGrip = new Label
            {
                Text = "⤡",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139),
                Size = new Size(18, 18),
                Location = new Point(w - 20, h - 20),
                Cursor = Cursors.SizeNWSE,
                TextAlign = ContentAlignment.BottomRight,
                BackColor = Color.Transparent
            };
            _lblResizeGrip.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ReleaseCapture();
                    SendMessage(this.Handle, WM_NCLBUTTONDOWN, HTBOTTOMRIGHT, 0);
                }
            };
            this.Controls.Add(_lblResizeGrip);
            _lblResizeGrip.BringToFront();

            this.Paint += (s, e) =>
            {
                using var p = new Pen(Color.FromArgb(45, 49, 62), 1f);
                e.Graphics.DrawRectangle(p, 0, 0, this.Width - 1, this.Height - 1);
            };

            _toolTip = new ToolTip
            {
                InitialDelay = 350,
                ReshowDelay = 100,
                AutoPopDelay = 30000,
                ShowAlways = true
            };
            _toolTip.SetToolTip(_btnClose, Loc.Get("TipPopupClose"));
            _toolTip.SetToolTip(_cbTargetLang, Loc.Get("TipPopupTargetLang"));
            _toolTip.SetToolTip(_lblModelPill, Loc.Get("TipPopupModel"));
            _toolTip.SetToolTip(_btnReplace, Loc.Get("TipPopupReplace"));
            _toolTip.SetToolTip(_btnCopy, Loc.Get("TipPopupCopy"));

            UpdateLayout();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateLayout();
        }

        private void UpdateLayout()
        {
            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;
            if (w <= 0 || h <= 0 || _pnlHeader == null) return;

            _pnlHeader.Width = w;
            if (_btnClose != null) _btnClose.Location = new Point(w - 32, 7);
            if (_cbTargetLang != null) _cbTargetLang.Location = new Point(w - 170, 8);
            if (_lblModelPill != null) _lblModelPill.Location = new Point(w - 242, 9);
            if (_lblSourceSnippet != null) _lblSourceSnippet.Width = Math.Max(50, w - 28);
            if (_tbResult != null) _tbResult.Size = new Size(Math.Max(50, w - 28), Math.Max(50, h - 124));
            if (_lblLatency != null) _lblLatency.Location = new Point(14, h - 42);
            if (_btnReplace != null) _btnReplace.Location = new Point(w - 290, h - 42);
            if (_btnCopy != null) _btnCopy.Location = new Point(w - 144, h - 42);
            if (_lblResizeGrip != null)
            {
                _lblResizeGrip.Location = new Point(w - 20, h - 20);
                _lblResizeGrip.BringToFront();
            }
            this.Invalidate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            try
            {
                var s = AppSettings.Load();
                s.TranslatePopupWidth = this.Width;
                s.TranslatePopupHeight = this.Height;
                s.TranslateTargetLang = GetSelectedTargetLang();
                s.Save();
            }
            catch { }
            try { _toolTip?.Dispose(); } catch { }
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

        private void PositionNearCursor()
        {
            var cur = Cursor.Position;
            var screen = Screen.FromPoint(cur);
            var area = screen.WorkingArea;

            int x = cur.X + 15;
            int y = cur.Y + 15;

            if (x + this.Width > area.Right)
                x = area.Right - this.Width - 10;
            if (y + this.Height > area.Bottom)
                y = area.Bottom - this.Height - 10;
            if (x < area.Left)
                x = area.Left + 10;
            if (y < area.Top)
                y = area.Top + 10;

            this.Location = new Point(x, y);
        }

        private string GetSelectedTargetLang()
        {
            if (_cbTargetLang.SelectedIndex <= 0)
                return "auto";
            int idx = _cbTargetLang.SelectedIndex - 1;
            if (idx >= 0 && idx < TranslationService.SupportedLanguages.Length)
                return TranslationService.SupportedLanguages[idx].Code;
            return "ru";
        }

        private async void StartExecution()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            _tbResult.Text = "⏳ Обработка запроса...";
            _lblLatency.Text = "⏳ Отправка...";
            _btnCopy.Text = "Копировать (Enter)";

            string targetLang = GetSelectedTargetLang();
            var service = TranslationService.Instance;

            try
            {
                string resultText;
                double latencyMs;
                string modelUsed;

                if (_isOcr && _imageBytes != null)
                {
                    (resultText, latencyMs, modelUsed) = await service.RecognizeImageAsync(
                        _imageBytes,
                        andTranslate: _andTranslate,
                        targetLang: targetLang);
                }
                else
                {
                    (resultText, latencyMs, modelUsed) = await service.TranslateTextAsync(
                        _originalText ?? string.Empty,
                        targetLang: targetLang);
                }

                if (this.IsDisposed) return;

                _translatedResult = resultText;
                _tbResult.Text = resultText;
                _lblLatency.Text = $"⚡ {latencyMs:F0} ms";

                if (!string.IsNullOrEmpty(modelUsed))
                {
                    string shortModel = modelUsed;
                    if (shortModel.Contains('/')) shortModel = shortModel.Substring(shortModel.LastIndexOf('/') + 1);
                    if (shortModel.Length > 12) shortModel = shortModel.Substring(0, 10) + "..";
                    _lblModelPill.Text = shortModel;
                }

                var settings = AppSettings.Load();
                if (settings.TranslateAutoCopy && !string.IsNullOrEmpty(resultText))
                {
                    Clipboard.SetText(resultText);
                    _btnCopy.Text = "Скопировано! ✓";
                }
            }
            catch (Exception ex)
            {
                if (this.IsDisposed) return;
                _tbResult.Text = $"❌ Ошибка:\n{ex.Message}";
                _lblLatency.Text = "Ошибка";
            }
        }

        private void CopyAndClose()
        {
            string text = !string.IsNullOrEmpty(_tbResult.SelectedText) ? _tbResult.SelectedText : _tbResult.Text;
            if (!string.IsNullOrWhiteSpace(text) && !text.StartsWith("⏳") && !text.StartsWith("❌"))
            {
                try
                {
                    Clipboard.SetText(text);
                    _btnCopy.Text = "Скопировано! ✓";
                }
                catch { }

                Task.Delay(180).ContinueWith(_ => {
                    if (!this.IsDisposed) this.Invoke(new Action(this.Close));
                });
            }
            else
            {
                this.Close();
            }
        }

        private void ReplaceInTargetWindow()
        {
            string text = !string.IsNullOrEmpty(_tbResult.SelectedText) ? _tbResult.SelectedText : _tbResult.Text;
            if (string.IsNullOrWhiteSpace(text) || text.StartsWith("⏳") || text.StartsWith("❌"))
            {
                this.Close();
                return;
            }

            try
            {
                Clipboard.SetText(text);
            }
            catch { }

            this.Close();

            if (_targetWindow != IntPtr.Zero)
            {
                Task.Run(() =>
                {
                    Thread.Sleep(60);
                    SetForegroundWindow(_targetWindow);
                    Thread.Sleep(60);

                    // Simulate Ctrl+V
                    keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                    keybd_event(VK_V, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(20);
                    keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                });
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                this.Close();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Enter))
            {
                ReplaceInTargetWindow();
                return true;
            }
            if (keyData == Keys.Enter && !_tbResult.Focused)
            {
                CopyAndClose();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void Header_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(this.Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cts?.Cancel();
                _cts?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
