using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace RyzenQuietPro
{
    public class ScreenSnipperForm : Form
    {
        private readonly Action<byte[]> _onCaptured;
        private readonly Action? _onCancelled;

        private Bitmap? _desktopBmp;
        private Point _startPoint;
        private Point _currentPoint;
        private bool _isSelecting = false;
        private Rectangle _selectionRect = Rectangle.Empty;

        public ScreenSnipperForm(Action<byte[]> onCaptured, Action? onCancelled = null)
        {
            _onCaptured = onCaptured;
            _onCancelled = onCancelled;

            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.Bounds = SystemInformation.VirtualScreen;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.DoubleBuffered = true;
            this.Cursor = Cursors.Cross;

            CaptureVirtualDesktop();
        }

        private void CaptureVirtualDesktop()
        {
            try
            {
                int w = this.Width;
                int h = this.Height;
                _desktopBmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(_desktopBmp);
                g.CopyFromScreen(this.Left, this.Top, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ScreenSnipper] Error capturing desktop: {ex.Message}");
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;

            if (_desktopBmp == null) return;

            // 1. Draw base captured desktop
            g.DrawImageUnscaled(_desktopBmp, 0, 0);

            // 2. Draw dimmed overlay
            using (var dimBrush = new SolidBrush(Color.FromArgb(135, 10, 15, 26)))
            {
                g.FillRectangle(dimBrush, this.ClientRectangle);
            }

            // 3. Clear and highlight selected region
            if (_selectionRect.Width > 2 && _selectionRect.Height > 2)
            {
                g.DrawImage(_desktopBmp, _selectionRect, _selectionRect, GraphicsUnit.Pixel);

                // Blue border
                using var penBorder = new Pen(Color.FromArgb(59, 130, 246), 2f);
                g.DrawRectangle(penBorder, _selectionRect);

                // Corner accents
                int rX = _selectionRect.Left;
                int rY = _selectionRect.Top;
                int rW = _selectionRect.Width;
                int rH = _selectionRect.Height;
                int cLen = Math.Min(14, Math.Min(rW / 4, rH / 4));

                using var penCorner = new Pen(Color.FromArgb(147, 197, 253), 3f);
                // Top-left
                g.DrawLine(penCorner, rX, rY, rX + cLen, rY);
                g.DrawLine(penCorner, rX, rY, rX, rY + cLen);
                // Top-right
                g.DrawLine(penCorner, rX + rW, rY, rX + rW - cLen, rY);
                g.DrawLine(penCorner, rX + rW, rY, rX + rW, rY + cLen);
                // Bottom-left
                g.DrawLine(penCorner, rX, rY + rH, rX + cLen, rY + rH);
                g.DrawLine(penCorner, rX, rY + rH, rX, rY + rH - cLen);
                // Bottom-right
                g.DrawLine(penCorner, rX + rW, rY + rH, rX + rW - cLen, rY + rH);
                g.DrawLine(penCorner, rX + rW, rY + rH, rX + rW, rY + rH - cLen);

                // Size badge (e.g. "480 × 260")
                string badgeText = $"{rW} × {rH}";
                using var badgeFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                var sz = g.MeasureString(badgeText, badgeFont);
                int bw = (int)sz.Width + 14;
                int bh = 22;
                int bx = Math.Min(this.Width - bw - 10, Math.Max(10, rX + rW - bw));
                int by = (rY + rH + bh + 10 < this.Height) ? rY + rH + 6 : Math.Max(10, rY - bh - 6);

                var badgeRect = new Rectangle(bx, by, bw, bh);
                using var badgeBg = new SolidBrush(Color.FromArgb(230, 15, 23, 42));
                using var badgePen = new Pen(Color.FromArgb(59, 130, 246), 1f);
                using var badgeBrush = new SolidBrush(Color.FromArgb(241, 245, 249));

                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillRoundedRectangle(badgeBg, badgeRect, 4);
                g.DrawRoundedRectangle(badgePen, badgeRect, 4);

                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(badgeText, badgeFont, badgeBrush, badgeRect, sf);
                g.SmoothingMode = SmoothingMode.Default;
            }

            // 4. Top instruction pill on primary screen
            var primScreen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            int relPrimX = primScreen.Bounds.X - this.Left;
            int relPrimY = primScreen.Bounds.Y - this.Top;

            string hintText = "📷 Выделите область экрана для распознавания | Enter - применить | Esc - отмена";
            using var hintFont = new Font("Segoe UI", 10f, FontStyle.Bold);
            var hsz = g.MeasureString(hintText, hintFont);
            int hw = (int)hsz.Width + 28;
            int hh = 36;
            int hx = relPrimX + (primScreen.Bounds.Width - hw) / 2;
            int hy = relPrimY + 24;

            var hintRect = new Rectangle(hx, hy, hw, hh);
            using var hintBg = new SolidBrush(Color.FromArgb(235, 15, 23, 42));
            using var hintPen = new Pen(Color.FromArgb(59, 130, 246), 1.5f);
            using var hintBrush = new SolidBrush(Color.FromArgb(248, 250, 252));

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.FillRoundedRectangle(hintBg, hintRect, 8);
            g.DrawRoundedRectangle(hintPen, hintRect, 8);

            var hsf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(hintText, hintFont, hintBrush, hintRect, hsf);
            g.SmoothingMode = SmoothingMode.Default;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _startPoint = e.Location;
                _currentPoint = e.Location;
                _selectionRect = new Rectangle(_startPoint, Size.Empty);
                _isSelecting = true;
                this.Invalidate();
            }
            else if (e.Button == MouseButtons.Right)
            {
                CancelAndClose();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_isSelecting)
            {
                _currentPoint = e.Location;
                int x = Math.Min(_startPoint.X, _currentPoint.X);
                int y = Math.Min(_startPoint.Y, _currentPoint.Y);
                int w = Math.Abs(_startPoint.X - _currentPoint.X);
                int h = Math.Abs(_startPoint.Y - _currentPoint.Y);
                _selectionRect = new Rectangle(x, y, w, h);
                this.Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && _isSelecting)
            {
                _isSelecting = false;
                if (_selectionRect.Width > 10 && _selectionRect.Height > 10)
                {
                    CommitSelection();
                }
                else
                {
                    CancelAndClose();
                }
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                CancelAndClose();
            }
            else if (e.KeyCode == Keys.Enter)
            {
                if (_selectionRect.Width > 10 && _selectionRect.Height > 10)
                {
                    CommitSelection();
                }
            }
        }

        private void CommitSelection()
        {
            if (_desktopBmp == null || _selectionRect.Width <= 0 || _selectionRect.Height <= 0)
            {
                CancelAndClose();
                return;
            }

            try
            {
                using var cropped = new Bitmap(_selectionRect.Width, _selectionRect.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(cropped))
                {
                    g.DrawImage(_desktopBmp,
                        new Rectangle(0, 0, _selectionRect.Width, _selectionRect.Height),
                        _selectionRect,
                        GraphicsUnit.Pixel);
                }

                using var ms = new MemoryStream();
                cropped.Save(ms, ImageFormat.Png);
                byte[] pngBytes = ms.ToArray();

                this.Close();
                _onCaptured(pngBytes);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ScreenSnipper] Error cropping selection: {ex.Message}");
                CancelAndClose();
            }
        }

        private void CancelAndClose()
        {
            this.Close();
            _onCancelled?.Invoke();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _desktopBmp?.Dispose();
                _desktopBmp = null;
            }
            base.Dispose(disposing);
        }
    }

    internal static class GraphicsExtensions
    {
        public static void FillRoundedRectangle(this Graphics g, Brush brush, Rectangle bounds, int radius)
        {
            using var path = CreateRoundedRectPath(bounds, radius);
            g.FillPath(brush, path);
        }

        public static void DrawRoundedRectangle(this Graphics g, Pen pen, Rectangle bounds, int radius)
        {
            using var path = CreateRoundedRectPath(bounds, radius);
            g.DrawPath(pen, path);
        }

        private static GraphicsPath CreateRoundedRectPath(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int diameter = radius * 2;
            var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));

            // Top-left
            path.AddArc(arc, 180, 90);
            // Top-right
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            // Bottom-right
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            // Bottom-left
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);

            path.CloseFigure();
            return path;
        }
    }
}
