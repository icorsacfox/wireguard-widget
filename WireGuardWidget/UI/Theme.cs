using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WireGuardWidget.UI
{
    /// <summary>Цвета (тёмная или светлая палитра), шрифты, масштаб DPI и общие приёмы отрисовки.</summary>
    internal static class Theme
    {
        public static bool IsDark { get; private set; }
        public static Color Bg { get; private set; }
        public static Color Panel { get; private set; }
        public static Color Surface { get; private set; }
        public static Color SurfaceHover { get; private set; }
        public static Color Border { get; private set; }
        public static Color InputFill { get; private set; }
        public static Color Text { get; private set; }
        public static Color TextMuted { get; private set; }
        public static Color Accent { get; private set; }
        public static Color AccentHover { get; private set; }
        public static Color Success { get; private set; }
        public static Color Warning { get; private set; }
        public static Color Danger { get; private set; }
        /// <summary>Фон и рамка полупрозрачного оверлея (с альфа-каналом).</summary>
        public static Color OverlayBack { get; private set; }
        public static Color OverlayBorder { get; private set; }

        public static readonly float Scale;
        public static readonly Font UiFont;
        public static readonly Font UiFontBold;
        public static readonly Font NameFont;
        public static readonly Font SmallFont;
        public static readonly Font IconFont;

        static Theme()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero))
                Scale = g.DpiX / 96f;
            UiFont = new Font("Segoe UI", 9f);
            UiFontBold = new Font("Segoe UI", 9f, FontStyle.Bold);
            NameFont = new Font("Segoe UI Semibold", 9.75f);
            SmallFont = new Font("Segoe UI", 8.25f);
            IconFont = new Font(FontExists("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets", 10f);
            SetPalette(true);
        }

        /// <summary>Переключает палитру. Окна после этого нужно перерисовать.</summary>
        public static void Apply(bool dark)
        {
            SetPalette(dark);
        }

        static void SetPalette(bool dark)
        {
            IsDark = dark;
            if (dark)
            {
                Bg = Color.FromArgb(27, 28, 31);
                Panel = Color.FromArgb(32, 33, 37);
                Surface = Color.FromArgb(38, 40, 44);
                SurfaceHover = Color.FromArgb(47, 50, 55);
                Border = Color.FromArgb(56, 59, 65);
                InputFill = Bg;
                Text = Color.FromArgb(232, 234, 237);
                TextMuted = Color.FromArgb(139, 145, 152);
                Accent = Color.FromArgb(47, 129, 247);
                AccentHover = Color.FromArgb(82, 152, 250);
                Success = Color.FromArgb(63, 185, 80);
                Warning = Color.FromArgb(210, 160, 50);
                Danger = Color.FromArgb(240, 95, 88);
                OverlayBack = Color.FromArgb(205, 22, 23, 26);
                OverlayBorder = Color.FromArgb(55, 255, 255, 255);
            }
            else
            {
                Bg = Color.FromArgb(243, 244, 246);
                Panel = Color.FromArgb(234, 236, 240);
                Surface = Color.FromArgb(255, 255, 255);
                SurfaceHover = Color.FromArgb(236, 240, 245);
                Border = Color.FromArgb(208, 213, 220);
                InputFill = Surface;
                Text = Color.FromArgb(31, 35, 40);
                TextMuted = Color.FromArgb(101, 109, 118);
                Accent = Color.FromArgb(9, 105, 218);
                AccentHover = Color.FromArgb(3, 87, 189);
                Success = Color.FromArgb(26, 127, 55);
                Warning = Color.FromArgb(154, 103, 0);
                Danger = Color.FromArgb(207, 34, 46);
                OverlayBack = Color.FromArgb(220, 250, 250, 252);
                OverlayBorder = Color.FromArgb(45, 0, 0, 0);
            }
        }

        /// <summary>Тёмная ли тема приложений в настройках Windows.</summary>
        public static bool SystemPrefersDark()
        {
            return ReadPersonalize("AppsUseLightTheme", 1) == 0;
        }

        /// <summary>Светлая ли панель задач (от этого зависит цвет значка «подключение…» в трее).</summary>
        public static bool TaskbarIsLight()
        {
            return ReadPersonalize("SystemUsesLightTheme", 0) == 1;
        }

        static int ReadPersonalize(string name, int fallback)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue(name);
                    return value is int ? (int)value : fallback;
                }
            }
            catch
            {
                return fallback;
            }
        }

        /// <summary>Логические пиксели (96 DPI) → физические.</summary>
        public static int S(float value)
        {
            return (int)Math.Round(value * Scale);
        }

        static bool FontExists(string name)
        {
            using (var font = new Font(name, 10f))
                return string.Equals(font.Name, name, StringComparison.OrdinalIgnoreCase);
        }

        public static Color Blend(Color from, Color to, float amount)
        {
            return Color.FromArgb(
                (int)(from.R + (to.R - from.R) * amount),
                (int)(from.G + (to.G - from.G) * amount),
                (int)(from.B + (to.B - from.B) * amount));
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRound(Graphics g, Color color, RectangleF r, float radius)
        {
            using (var brush = new SolidBrush(color))
            using (var path = RoundRect(r, radius))
                g.FillPath(brush, path);
        }

        public static void DrawRound(Graphics g, Color color, RectangleF r, float radius, float width = 1f)
        {
            var inner = new RectangleF(r.X + width / 2, r.Y + width / 2, r.Width - width, r.Height - width);
            using (var pen = new Pen(color, width))
            using (var path = RoundRect(inner, radius))
                g.DrawPath(pen, path);
        }

        public static void DrawCheckMark(Graphics g, Color color, RectangleF box, float width)
        {
            var points = new[]
            {
                new PointF(box.X + box.Width * 0.24f, box.Y + box.Height * 0.53f),
                new PointF(box.X + box.Width * 0.43f, box.Y + box.Height * 0.71f),
                new PointF(box.X + box.Width * 0.77f, box.Y + box.Height * 0.32f),
            };
            using (var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, points);
        }

        /// <summary>Заголовок окна в цвет темы: тёмный режим (Windows 10 20H1+ / 11), цвет заголовка и рамки (Windows 11).</summary>
        public static void ApplyTitleBar(IntPtr hwnd)
        {
            int dark = IsDark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4) != 0)
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref dark, 4);
            int caption = ToColorRef(Bg);
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, 4);
            int text = ToColorRef(Text);
            DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, 4);
            int border = ToColorRef(Border);
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, 4);
        }

        /// <summary>Скруглённые углы и рамка для всплывающих окон без заголовка (Windows 11).</summary>
        public static void ApplyPopupChrome(IntPtr hwnd)
        {
            int round = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);
            int border = ToColorRef(Border);
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, 4);
        }

        static int ToColorRef(Color c)
        {
            return c.R | (c.G << 8) | (c.B << 16);
        }

        /// <summary>Тёмная всплывающая подсказка.</summary>
        public static ToolTip CreateToolTip()
        {
            var tip = new ToolTip { OwnerDraw = true, InitialDelay = 500, ReshowDelay = 100, AutoPopDelay = 8000 };
            tip.Popup += (s, e) =>
            {
                var size = TextRenderer.MeasureText(tip.GetToolTip(e.AssociatedControl), SmallFont);
                e.ToolTipSize = new Size(size.Width + S(14), size.Height + S(10));
            };
            tip.Draw += (s, e) =>
            {
                using (var bg = new SolidBrush(Surface))
                    e.Graphics.FillRectangle(bg, e.Bounds);
                using (var pen = new Pen(Border))
                    e.Graphics.DrawRectangle(pen, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, SmallFont, e.Bounds, Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            };
            return tip;
        }

        const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWA_BORDER_COLOR = 34;
        const int DWMWA_CAPTION_COLOR = 35;
        const int DWMWA_TEXT_COLOR = 36;

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
