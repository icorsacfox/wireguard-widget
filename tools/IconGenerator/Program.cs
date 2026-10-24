using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

/// <summary>
/// Генерирует иконку WireGuard Widget: синий «скруглённый квадрат», как у WireGuard,
/// и белый дракон с телом-восьмёркой — но с тремя головами.
/// Использование: IconGenerator.exe &lt;путь к app.ico&gt; [путь к превью .png]
/// Все координаты — в системе 256×256.
/// </summary>
static class Program
{
    static readonly Color Background = Color.FromArgb(30, 82, 184);
    static readonly Color Dragon = Color.White;

    const float BodyWidth = 21f;
    const float NeckWidth = 14f;
    const float HeadScale = 1.08f;

    /// <summary>
    /// Осевая линия тела-восьмёрки (замкнутый сплайн). Первая точка — пересечение: отсюда линия уходит
    /// вверх-вправо, обходит верхнюю петлю против часовой стрелки, снова пересекает середину
    /// вниз-вправо, обходит нижнюю петлю и возвращается в первую точку.
    /// </summary>
    static readonly PointF[] BodyLine =
    {
        new PointF(126, 168),
        new PointF(148, 152), new PointF(156, 130), new PointF(146, 109), new PointF(122, 100),
        new PointF(97, 105), new PointF(82, 124), new PointF(85, 148), new PointF(103, 163),
        new PointF(128, 172),
        new PointF(153, 182), new PointF(172, 200), new PointF(172, 224), new PointF(153, 239),
        new PointF(126, 239), new PointF(106, 224), new PointF(104, 200),
    };

    static void Main(string[] args)
    {
        string icoPath = args.Length > 0 ? args[0] : "app.ico";
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
        var images = sizes.Select(Render).ToList();
        WriteIco(icoPath, images);
        if (args.Length > 1)
            WritePreview(args[1], sizes);
        Console.WriteLine("Сохранено: " + Path.GetFullPath(icoPath));
    }

    /// <summary>Рисует в 4 раза крупнее и уменьшает — так края получаются чище.</summary>
    static Bitmap Render(int size)
    {
        int big = Math.Max(size, 64) * 4;
        using (var large = new Bitmap(big, big, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(large))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.ScaleTransform(big / 256f, big / 256f);
                Draw(g);
            }
            var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(large, new Rectangle(0, 0, size, size));
            }
            return result;
        }
    }

    static void Draw(Graphics g)
    {
        using (var back = new SolidBrush(Background))
        using (var shape = Superellipse(128, 128, 126, 5.0))
            g.FillPath(back, shape);

        DrawBody(g);

        // три шеи вырастают из верхней дуги восьмёрки
        DrawNeck(g, new PointF(104, 104), new PointF(94, 98), new PointF(82, 96), new PointF(74, 82));
        DrawNeck(g, new PointF(122, 101), new PointF(124, 84), new PointF(112, 70), new PointF(118, 50));
        DrawNeck(g, new PointF(140, 105), new PointF(152, 98), new PointF(168, 98), new PointF(178, 82));

        DrawHead(g, new PointF(74, 82), -24, true);
        DrawHead(g, new PointF(118, 50), -30, false);
        DrawHead(g, new PointF(178, 82), -14, false);
    }

    static void DrawBody(Graphics g)
    {
        using (var path = new GraphicsPath())
        {
            path.AddClosedCurve(BodyLine, 0.55f);
            using (var pen = new Pen(Dragon, BodyWidth) { LineJoin = LineJoin.Round })
                g.DrawPath(pen, path);

            // В точке пересечения часть тела, идущая вверх-вправо, проходит поверх — с зазором цвета фона.
            // Это начало и конец замкнутого сплайна, берём их из той же ломаной, чтобы совпасть пиксель в пиксель.
            path.Flatten(null, 0.1f);
            PointF[] flat = path.PathPoints;
            var cross = BodyLine[0];
            var over = flat.Reverse().TakeWhile(p => Distance(p, cross) < 26).Reverse()
                .Concat(flat.TakeWhile(p => Distance(p, cross) < 26))
                .ToArray();
            using (var gap = new Pen(Background, BodyWidth + 9) { StartCap = LineCap.Flat, EndCap = LineCap.Flat, LineJoin = LineJoin.Round })
                g.DrawLines(gap, over);
            using (var pen = new Pen(Dragon, BodyWidth) { StartCap = LineCap.Flat, EndCap = LineCap.Flat, LineJoin = LineJoin.Round })
                g.DrawLines(pen, over);
        }

        // хвостовой плавник слева, как у оригинала
        using (var brush = new SolidBrush(Dragon))
        {
            g.FillPolygon(brush, new[]
            {
                new PointF(88, 147), new PointF(58, 161), new PointF(74, 165), new PointF(62, 179), new PointF(95, 165),
            });
        }
    }

    static float Distance(PointF a, PointF b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    static void DrawNeck(Graphics g, PointF from, PointF c1, PointF c2, PointF to)
    {
        using (var pen = new Pen(Dragon, NeckWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawBezier(pen, from, c1, c2, to);
    }

    /// <summary>Голова в профиль (смотрит вправо), точка (0,0) — место крепления шеи.</summary>
    static readonly PointF[] HeadShape =
    {
        new PointF(-3, 8),
        new PointF(10, 10), new PointF(24, 9), new PointF(36, 6), new PointF(45, 4),  // нижняя челюсть
        new PointF(35, 0.5f),                                                       // пасть
        new PointF(47, -2), new PointF(51, -6), new PointF(47, -11),                 // нос
        new PointF(35, -14), new PointF(24, -18), new PointF(15, -19),               // лоб
        new PointF(9, -18), new PointF(-3, -32),                                     // гребень: шипы назад
        new PointF(1, -16), new PointF(-13, -25),
        new PointF(-5, -11), new PointF(-17, -14),
        new PointF(-6, -5),
    };

    static void DrawHead(Graphics g, PointF at, float angle, bool mirror)
    {
        var state = g.Save();
        g.TranslateTransform(at.X, at.Y);
        if (mirror)
            g.ScaleTransform(-1, 1);
        g.RotateTransform(angle);
        g.ScaleTransform(HeadScale, HeadScale);
        using (var brush = new SolidBrush(Dragon))
            g.FillPolygon(brush, HeadShape);
        using (var eye = new SolidBrush(Background))
            g.FillEllipse(eye, 23, -13.5f, 6.5f, 6.5f);
        g.Restore(state);
    }

    static GraphicsPath Superellipse(float cx, float cy, float radius, double n)
    {
        var points = new List<PointF>();
        for (int i = 0; i < 360; i++)
        {
            double a = i * Math.PI / 180;
            double c = Math.Cos(a), s = Math.Sin(a);
            double x = Math.Sign(c) * Math.Pow(Math.Abs(c), 2 / n);
            double y = Math.Sign(s) * Math.Pow(Math.Abs(s), 2 / n);
            points.Add(new PointF(cx + (float)(x * radius), cy + (float)(y * radius)));
        }
        var path = new GraphicsPath();
        path.AddPolygon(points.ToArray());
        return path;
    }

    static void WriteIco(string path, List<Bitmap> images)
    {
        var encoded = images.Select(b =>
        {
            using (var ms = new MemoryStream())
            {
                b.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }).ToList();
        using (var stream = File.Create(path))
        using (var w = new BinaryWriter(stream))
        {
            w.Write((short)0);
            w.Write((short)1);
            w.Write((short)images.Count);
            int offset = 6 + 16 * images.Count;
            for (int i = 0; i < images.Count; i++)
            {
                int size = images[i].Width;
                w.Write((byte)(size >= 256 ? 0 : size));
                w.Write((byte)(size >= 256 ? 0 : size));
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((short)1);
                w.Write((short)32);
                w.Write(encoded[i].Length);
                w.Write(offset);
                offset += encoded[i].Length;
            }
            foreach (var bytes in encoded)
                w.Write(bytes);
        }
    }

    static void WritePreview(string path, int[] sizes)
    {
        int width = 256 + 20 + sizes.Where(s => s < 256).Sum(s => s + 12) + 10;
        using (var sheet = new Bitmap(width * 2, 276))
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(32, 32, 32));
            using (var light = new SolidBrush(Color.FromArgb(240, 240, 240)))
                g.FillRectangle(light, width, 0, width, 276);
            foreach (int half in new[] { 0, width })
            {
                int x = half + 10;
                using (var big = Render(256))
                    g.DrawImage(big, x, 10);
                x += 276;
                foreach (int size in sizes.Where(s => s < 256))
                {
                    using (var small = Render(size))
                        g.DrawImage(small, x, 266 - size);
                    x += size + 12;
                }
            }
            sheet.Save(path, ImageFormat.Png);
        }
    }
}
