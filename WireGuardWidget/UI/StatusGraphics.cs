using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using WireGuardWidget.Emoji;

namespace WireGuardWidget.UI
{
    /// <summary>Значок состояния для трея и окна: эмодзи активных конфигураций, 🚫 или многоточие.</summary>
    internal static class StatusGraphics
    {
        public const string NoConnectionEmoji = "🚫";

        /// <param name="dotsColor">Цвет многоточия «подключение…».</param>
        public static Bitmap ComposeIcon(IList<string> emojis, bool busy, int size, Color dotsColor)
        {
            var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                if (busy)
                {
                    DrawDots(g, new RectangleF(0, 0, size, size), dotsColor, -1);
                }
                else if (emojis.Count == 0)
                {
                    Draw(g, NoConnectionEmoji, 0, 0, size);
                }
                else if (emojis.Count == 1)
                {
                    Draw(g, emojis[0], 0, 0, size);
                }
                else if (emojis.Count == 2)
                {
                    // два значка по диагонали, второй поверх первого
                    int s = (int)Math.Round(size * 0.66);
                    Draw(g, emojis[0], 0, 0, s);
                    Draw(g, emojis[1], size - s, size - s, s);
                }
                else
                {
                    int s = size / 2;
                    Draw(g, emojis[0], 0, 0, s);
                    Draw(g, emojis[1], size - s, 0, s);
                    if (emojis.Count == 3)
                    {
                        Draw(g, emojis[2], (size - s) / 2, size - s, s);
                    }
                    else
                    {
                        Draw(g, emojis[2], 0, size - s, s);
                        Draw(g, emojis.Count == 4 ? emojis[3] : "➕", size - s, size - s, s);
                    }
                }
            }
            return bitmap;
        }

        static void Draw(Graphics g, string emoji, int x, int y, int size)
        {
            g.DrawImage(EmojiRenderer.Shared.Get(emoji, size), x, y, size, size);
        }

        /// <summary>Три точки по центру rect; highlight — индекс яркой точки (для анимации) или -1.</summary>
        public static void DrawDots(Graphics g, RectangleF rect, Color color, int highlight)
        {
            float diameter = Math.Max(2f, Math.Min(rect.Width / 5f, rect.Height / 3f));
            float gap = diameter * 0.55f;
            float total = diameter * 3 + gap * 2;
            float x = rect.X + (rect.Width - total) / 2f;
            float y = rect.Y + (rect.Height - diameter) / 2f;
            for (int i = 0; i < 3; i++)
            {
                int alpha = highlight < 0 || highlight == i ? 255 : 90;
                using (var brush = new SolidBrush(Color.FromArgb(alpha, color)))
                    g.FillEllipse(brush, x + i * (diameter + gap), y, diameter, diameter);
            }
        }
    }

    /// <summary>Icon, созданная из Bitmap; освобождает и Icon, и исходный HICON.</summary>
    internal sealed class OwnedIcon : IDisposable
    {
        readonly IntPtr handle;

        OwnedIcon(IntPtr handle)
        {
            this.handle = handle;
            Icon = Icon.FromHandle(handle);
        }

        public Icon Icon { get; private set; }

        /// <summary>Создаёт значок и освобождает bitmap.</summary>
        public static OwnedIcon FromBitmap(Bitmap bitmap)
        {
            using (bitmap)
                return new OwnedIcon(bitmap.GetHicon());
        }

        public void Dispose()
        {
            Icon.Dispose();
            DestroyIcon(handle);
        }

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);
    }
}
