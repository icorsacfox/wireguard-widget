using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WireGuardWidget.Emoji;

namespace WireGuardWidget.UI
{
    /// <summary>
    /// Маленький полупрозрачный оверлей поверх всех окон: эмодзи активных конфигураций, 🚫 или анимированное многоточие.
    /// Layered-окно с попиксельной прозрачностью; не забирает фокус. Перетаскивается мышью,
    /// двойной клик — открыть окно, правый клик — меню.
    /// </summary>
    internal sealed class OverlayForm : Form
    {
        readonly WidgetApp app;
        readonly Timer animation;
        readonly ToolTip tip;
        List<string> emojis = new List<string>();
        bool busy;
        int phase;
        string tipText;
        bool mouseDown, dragged;
        Point dragStartCursor, dragStartLocation;

        public OverlayForm(WidgetApp app)
        {
            this.app = app;
            Text = "WireGuard Widget Overlay";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            animation = new Timer { Interval = 320 };
            animation.Tick += (s, e) =>
            {
                phase = (phase + 1) % 3;
                Render();
            };
            tip = Theme.CreateToolTip();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000, WS_EX_TOPMOST = 0x8;
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        public void SetContent(IList<string> activeEmojis, bool isBusy, string tooltip)
        {
            bool changed = isBusy != busy || !activeEmojis.SequenceEqual(emojis);
            emojis = activeEmojis.ToList();
            busy = isBusy;
            if (busy && Visible)
                animation.Start();
            else if (!busy)
                animation.Stop();
            if (tooltip != tipText)
            {
                tipText = tooltip;
                tip.SetToolTip(this, tooltip);
            }
            if (changed)
                Render();
        }

        public void ShowOverlay()
        {
            using (var bitmap = BuildBitmap())
            {
                var location = app.Settings.OverlayX.HasValue && app.Settings.OverlayY.HasValue
                    ? new Point(app.Settings.OverlayX.Value, app.Settings.OverlayY.Value)
                    : DefaultLocation(bitmap.Size);
                Bounds = new Rectangle(ClampToScreen(location, bitmap.Size), bitmap.Size);
            }
            if (!Visible)
                Show();
            if (busy)
                animation.Start();
            Render();
        }

        public void ApplyAppearance()
        {
            Render();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible)
                animation.Stop();
        }

        static Point DefaultLocation(Size size)
        {
            var area = Screen.PrimaryScreen.WorkingArea;
            return new Point(area.Right - size.Width - Theme.S(24), area.Top + Theme.S(24));
        }

        static Point ClampToScreen(Point location, Size size)
        {
            var area = Screen.FromRectangle(new Rectangle(location, size)).WorkingArea;
            if (!area.IntersectsWith(new Rectangle(location, size)))
                return DefaultLocation(size);
            return new Point(
                Math.Max(area.Left, Math.Min(location.X, area.Right - size.Width)),
                Math.Max(area.Top, Math.Min(location.Y, area.Bottom - size.Height)));
        }

        internal Bitmap BuildBitmap()
        {
            int emojiSize = Theme.S(app.Settings.OverlaySize);
            int pad = Math.Max(Theme.S(5), emojiSize / 5);
            int gap = Math.Max(Theme.S(3), emojiSize / 6);
            int count = Math.Max(1, emojis.Count);
            int contentWidth = busy ? emojiSize * 3 / 2 : count * emojiSize + (count - 1) * gap;
            int width = contentWidth + pad * 2, height = emojiSize + pad * 2;

            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                var rect = new RectangleF(0, 0, width, height);
                float radius = Math.Min(height / 2f, emojiSize * 0.4f);
                Theme.FillRound(g, Theme.OverlayBack, rect, radius);
                Theme.DrawRound(g, Theme.OverlayBorder, rect, radius);
                if (busy)
                {
                    StatusGraphics.DrawDots(g, new RectangleF(pad, pad, contentWidth, emojiSize), Theme.Text, phase);
                }
                else
                {
                    var list = emojis.Count == 0 ? new List<string> { StatusGraphics.NoConnectionEmoji } : emojis;
                    for (int i = 0; i < list.Count; i++)
                        g.DrawImage(EmojiRenderer.Shared.Get(list[i], emojiSize), pad + i * (emojiSize + gap), pad, emojiSize, emojiSize);
                }
            }
            return bitmap;
        }

        void Render()
        {
            if (!IsHandleCreated || !Visible)
                return;
            using (var bitmap = BuildBitmap())
            {
                byte alpha = (byte)(255 * app.Settings.OverlayOpacity / 100);
                // если оверлей прижат к правому краю экрана — растём влево
                var area = Screen.FromControl(this).WorkingArea;
                int x = Left;
                if (Right >= area.Right - Theme.S(2) && bitmap.Width != Width)
                    x = Right - bitmap.Width;
                UpdateLayered(bitmap, new Point(x, Top), alpha);
            }
        }

        void UpdateLayered(Bitmap bitmap, Point location, byte alpha)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memoryDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
            IntPtr old = SelectObject(memoryDc, hBitmap);
            try
            {
                var size = new SIZE { cx = bitmap.Width, cy = bitmap.Height };
                var source = new POINT();
                var destination = new POINT { x = location.X, y = location.Y };
                var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = alpha, AlphaFormat = 1 };
                UpdateLayeredWindow(Handle, screenDc, ref destination, ref size, memoryDc, ref source, 0, ref blend, ULW_ALPHA);
            }
            finally
            {
                SelectObject(memoryDc, old);
                DeleteObject(hBitmap);
                DeleteDC(memoryDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        #region Мышь

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;
            mouseDown = true;
            dragged = false;
            dragStartCursor = Cursor.Position;
            dragStartLocation = Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!mouseDown)
                return;
            var cursor = Cursor.Position;
            int dx = cursor.X - dragStartCursor.X, dy = cursor.Y - dragStartCursor.Y;
            if (!dragged && Math.Abs(dx) + Math.Abs(dy) < Theme.S(4))
                return;
            dragged = true;
            Location = Snap(new Point(dragStartLocation.X + dx, dragStartLocation.Y + dy));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                mouseDown = false;
                if (dragged)
                    app.SaveOverlayLocation(Location);
            }
            else if (e.Button == MouseButtons.Right)
            {
                SetForegroundWindow(Handle); // чтобы меню закрывалось кликом мимо
                app.ShowOverlayMenu(Cursor.Position);
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left)
                app.ShowMainWindow();
        }

        /// <summary>Прилипание к краям рабочей области.</summary>
        Point Snap(Point p)
        {
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            int snap = Theme.S(14);
            if (Math.Abs(p.X - area.Left) < snap) p.X = area.Left;
            if (Math.Abs(p.X + Width - area.Right) < snap) p.X = area.Right - Width;
            if (Math.Abs(p.Y - area.Top) < snap) p.Y = area.Top;
            if (Math.Abs(p.Y + Height - area.Bottom) < snap) p.Y = area.Bottom - Height;
            return p;
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                animation.Dispose();
                tip.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Win32

        const int ULW_ALPHA = 2;

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        struct SIZE { public int cx, cy; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct BLENDFUNCTION
        {
            public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc,
            ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")]
        static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll")]
        static extern bool DeleteDC(IntPtr dc);

        #endregion
    }
}
