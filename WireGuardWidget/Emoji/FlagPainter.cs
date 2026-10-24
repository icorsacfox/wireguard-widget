using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;

namespace WireGuardWidget.Emoji
{
    /// <summary>
    /// В Windows нет цветных эмодзи-флагов (Segoe UI Emoji рисует вместо них буквы «NL»),
    /// поэтому флаги популярных стран рисуются здесь векторно. В настройках флаг хранится
    /// обычной эмодзи-последовательностью (🇳🇱), неизвестные флаги остаются буквами.
    /// Координаты фигур — в долях высоты флага (у флага 3:2 ширина = 1.5).
    /// </summary>
    internal static class FlagPainter
    {
        sealed class Flag
        {
            public string Code;
            public string Name;
            public float Aspect;
            public Action<Graphics, RectangleF> Paint;
        }

        const int RegionalIndicatorA = 0x1F1E6;
        static readonly List<Flag> Flags = new List<Flag>();
        static readonly Dictionary<string, Flag> ByCode;

        static FlagPainter()
        {
            Add("NL", "Нидерланды", H("#AE1C28", "#FFFFFF", "#21468B"));
            Add("DE", "Германия", H("#000000", "#DD0000", "#FFCE00"));
            Add("US", "США", UnitedStates);
            Add("GB", "Великобритания", (g, r) => UnionJack(g, r));
            Add("FI", "Финляндия", Nordic("#FFFFFF", "#002F6C", null));
            Add("SE", "Швеция", Nordic("#006AA7", "#FECC00", null));
            Add("NO", "Норвегия", Nordic("#BA0C2F", "#FFFFFF", "#00205B"));
            Add("DK", "Дания", Nordic("#C8102E", "#FFFFFF", null));
            Add("IS", "Исландия", Nordic("#02529C", "#FFFFFF", "#DC1E35"));
            Add("EE", "Эстония", H("#0072CE", "#000000", "#FFFFFF"));
            Add("LV", "Латвия", HW(new[] { 2f, 1, 2 }, "#9E3039", "#FFFFFF", "#9E3039"));
            Add("LT", "Литва", H("#FDB913", "#006A44", "#C1272D"));
            Add("PL", "Польша", H("#FFFFFF", "#DC143C"));
            Add("CZ", "Чехия", Then(H("#FFFFFF", "#D7141A"), (g, r) => Poly(g, r, "#11457E", 0, 0, 0.75f, 0.5f, 0, 1)));
            Add("AT", "Австрия", H("#ED2939", "#FFFFFF", "#ED2939"));
            Add("CH", "Швейцария", Switzerland, 1f);
            Add("FR", "Франция", V("#002654", "#FFFFFF", "#CE1126"));
            Add("BE", "Бельгия", V("#000000", "#FDDA24", "#EF3340"));
            Add("LU", "Люксембург", H("#EF3340", "#FFFFFF", "#00A3E0"));
            Add("IE", "Ирландия", V("#169B62", "#FFFFFF", "#FF883E"));
            Add("ES", "Испания", HW(new[] { 1f, 2, 1 }, "#AA151B", "#F1BF00", "#AA151B"));
            Add("PT", "Португалия", Then(VW(new[] { 2f, 3 }, "#006600", "#FF0000"), (g, r) =>
            {
                Circle(g, r, "#FFE000", 0.6f, 0.5f, 0.21f);
                Circle(g, r, "#FF0000", 0.6f, 0.5f, 0.13f);
                Circle(g, r, "#FFFFFF", 0.6f, 0.5f, 0.075f);
            }));
            Add("IT", "Италия", V("#009246", "#FFFFFF", "#CE2B37"));
            Add("GR", "Греция", Greece);
            Add("RO", "Румыния", V("#002B7F", "#FCD116", "#CE1126"));
            Add("BG", "Болгария", H("#FFFFFF", "#00966E", "#D62612"));
            Add("HU", "Венгрия", H("#CE2939", "#FFFFFF", "#477050"));
            Add("HR", "Хорватия", Then(H("#FF0000", "#FFFFFF", "#171796"), Checkerboard));
            Add("RS", "Сербия", H("#C6363C", "#0C4076", "#FFFFFF"));
            Add("MD", "Молдова", Then(V("#0046AE", "#FFD200", "#CC092F"), (g, r) => Ellipse(g, r, "#B07E3A", 0.75f, 0.5f, 0.09f, 0.13f)));
            Add("UA", "Украина", H("#0057B7", "#FFD700"));
            Add("BY", "Беларусь", Belarus);
            Add("RU", "Россия", H("#FFFFFF", "#0039A6", "#D52B1E"));
            Add("GE", "Грузия", Georgia);
            Add("AM", "Армения", H("#D90012", "#0033A0", "#F2A800"));
            Add("AZ", "Азербайджан", Then(H("#00B5E2", "#EF3340", "#509E2F"), (g, r) =>
            {
                Circle(g, r, "#FFFFFF", 0.71f, 0.5f, 0.12f);
                Circle(g, r, "#EF3340", 0.745f, 0.5f, 0.1f);
                Star(g, r, "#FFFFFF", 0.86f, 0.5f, 0.055f);
            }));
            Add("KZ", "Казахстан", (g, r) =>
            {
                Fill(g, r, "#00AFCA");
                Rect(g, r, "#FEC50C", 0.09f, 0.08f, 0.05f, 0.84f);
                Star(g, r, "#FEC50C", 0.75f, 0.44f, 0.24f, 0.75f, 16);
                Circle(g, r, "#FEC50C", 0.75f, 0.44f, 0.14f);
            });
            Add("UZ", "Узбекистан", Then(HW(new[] { 10f, 1, 8, 1, 10 }, "#0099B5", "#CE1126", "#FFFFFF", "#CE1126", "#1EB53A"), (g, r) =>
            {
                Circle(g, r, "#FFFFFF", 0.2f, 0.17f, 0.1f);
                Circle(g, r, "#0099B5", 0.24f, 0.17f, 0.09f);
            }));
            Add("KG", "Кыргызстан", (g, r) =>
            {
                Fill(g, r, "#E8112D");
                Star(g, r, "#FFEF00", 0.75f, 0.5f, 0.3f, 0.7f, 20);
                Circle(g, r, "#E8112D", 0.75f, 0.5f, 0.12f);
                Circle(g, r, "#FFEF00", 0.75f, 0.5f, 0.09f);
            });
            Add("TR", "Турция", (g, r) =>
            {
                Fill(g, r, "#E30A17");
                Circle(g, r, "#FFFFFF", 0.5f, 0.5f, 0.25f);
                Circle(g, r, "#E30A17", 0.5625f, 0.5f, 0.2f);
                Star(g, r, "#FFFFFF", 0.72f, 0.5f, 0.125f, 0.382f, 5, 180);
            });
            Add("IL", "Израиль", Israel);
            Add("AE", "ОАЭ", (g, r) =>
            {
                Fill(g, r, "#FFFFFF");
                Rect(g, r, "#00732F", 0, 0, 1.5f, 1 / 3f);
                Rect(g, r, "#000000", 0, 2 / 3f, 1.5f, 1 / 3f + 0.01f);
                Rect(g, r, "#FF0000", 0, 0, 0.375f, 1);
            });
            Add("EG", "Египет", Then(H("#CE1126", "#FFFFFF", "#000000"), (g, r) => Ellipse(g, r, "#C09300", 0.75f, 0.5f, 0.07f, 0.11f)));
            Add("IN", "Индия", Then(H("#FF9933", "#FFFFFF", "#138808"), (g, r) =>
            {
                Ring(g, r, "#000080", 0.75f, 0.5f, 0.12f, 0.025f);
                Circle(g, r, "#000080", 0.75f, 0.5f, 0.03f);
            }));
            Add("TH", "Таиланд", HW(new[] { 1f, 1, 2, 1, 1 }, "#A51931", "#F4F5F8", "#2D2A4A", "#F4F5F8", "#A51931"));
            Add("VN", "Вьетнам", (g, r) =>
            {
                Fill(g, r, "#DA251D");
                Star(g, r, "#FFFF00", 0.75f, 0.52f, 0.3f);
            });
            Add("SG", "Сингапур", Then(H("#EF3340", "#FFFFFF"), (g, r) =>
            {
                Circle(g, r, "#FFFFFF", 0.27f, 0.25f, 0.16f);
                Circle(g, r, "#EF3340", 0.33f, 0.25f, 0.155f);
                for (int i = 0; i < 5; i++)
                {
                    double a = -Math.PI / 2 + i * 2 * Math.PI / 5;
                    Star(g, r, "#FFFFFF", 0.43f + (float)Math.Cos(a) * 0.08f, 0.26f + (float)Math.Sin(a) * 0.08f, 0.03f);
                }
            }));
            Add("HK", "Гонконг", (g, r) =>
            {
                Fill(g, r, "#DE2910");
                for (int i = 0; i < 5; i++)
                    Petal(g, r, "#FFFFFF", 0.75f, 0.5f, i * 72f);
            });
            Add("CN", "Китай", (g, r) =>
            {
                Fill(g, r, "#EE1C25");
                Star(g, r, "#FFFF00", 0.25f, 0.25f, 0.15f);
                Star(g, r, "#FFFF00", 0.5f, 0.1f, 0.05f);
                Star(g, r, "#FFFF00", 0.6f, 0.2f, 0.05f);
                Star(g, r, "#FFFF00", 0.6f, 0.35f, 0.05f);
                Star(g, r, "#FFFF00", 0.5f, 0.45f, 0.05f);
            });
            Add("JP", "Япония", (g, r) =>
            {
                Fill(g, r, "#FFFFFF");
                Circle(g, r, "#BC002D", 0.75f, 0.5f, 0.3f);
            });
            Add("KR", "Южная Корея", SouthKorea);
            Add("CA", "Канада", Then(VW(new[] { 1f, 2, 1 }, "#D52B1E", "#FFFFFF", "#D52B1E"), (g, r) => MapleLeaf(g, r, 0.75f, 0.5f, 0.3f)));
            Add("MX", "Мексика", Then(V("#006847", "#FFFFFF", "#CE1126"), (g, r) =>
            {
                Circle(g, r, "#8C9157", 0.75f, 0.52f, 0.12f);
                Circle(g, r, "#8C5A2B", 0.75f, 0.48f, 0.08f);
            }));
            Add("BR", "Бразилия", Brazil);
            Add("AR", "Аргентина", Then(H("#74ACDF", "#FFFFFF", "#74ACDF"), (g, r) =>
            {
                Star(g, r, "#F6B40E", 0.75f, 0.5f, 0.12f, 0.6f, 16);
                Circle(g, r, "#F6B40E", 0.75f, 0.5f, 0.075f);
            }));
            Add("AU", "Австралия", Australia);
            Add("NZ", "Новая Зеландия", NewZealand);
            Add("PH", "Филиппины", Then(H("#0038A8", "#CE1126"), (g, r) =>
            {
                Poly(g, r, "#FFFFFF", 0, 0, 0.866f, 0.5f, 0, 1);
                Star(g, r, "#FCD116", 0.29f, 0.5f, 0.13f, 0.55f, 8);
            }));
            Add("ID", "Индонезия", H("#FF0000", "#FFFFFF"));
            Add("EU", "Евросоюз", (g, r) =>
            {
                Fill(g, r, "#003399");
                for (int i = 0; i < 12; i++)
                {
                    double a = i * Math.PI / 6;
                    Star(g, r, "#FFCC00", 0.75f + (float)Math.Cos(a) * 0.333f, 0.5f + (float)Math.Sin(a) * 0.333f, 0.06f);
                }
            });

            ByCode = Flags.ToDictionary(f => f.Code);
        }

        /// <summary>Все нарисованные флаги — эмодзи-последовательностями (🇳🇱).</summary>
        public static IEnumerable<string> All
        {
            get { return Flags.Select(f => ToEmoji(f.Code)); }
        }

        public static string NameOf(string emoji)
        {
            Flag flag;
            string code = CodeOf(emoji);
            return code != null && ByCode.TryGetValue(code, out flag) ? flag.Name : null;
        }

        public static bool IsFlag(string emoji)
        {
            return CodeOf(emoji) != null;
        }

        static string ToEmoji(string code)
        {
            return char.ConvertFromUtf32(RegionalIndicatorA + code[0] - 'A') + char.ConvertFromUtf32(RegionalIndicatorA + code[1] - 'A');
        }

        /// <summary>"🇳🇱" → "NL"; null, если это не флаг.</summary>
        static string CodeOf(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;
            text = text.Replace("️", "").Trim();
            if (text.Length != 4 || !char.IsSurrogatePair(text, 0) || !char.IsSurrogatePair(text, 2))
                return null;
            int first = char.ConvertToUtf32(text, 0) - RegionalIndicatorA;
            int second = char.ConvertToUtf32(text, 2) - RegionalIndicatorA;
            if (first < 0 || first > 25 || second < 0 || second > 25)
                return null;
            return new string(new[] { (char)('A' + first), (char)('A' + second) });
        }

        /// <summary>Флаг шириной width пикселей со скруглёнными углами; null — такого флага нет.</summary>
        public static Bitmap TryRender(string emoji, int width)
        {
            Flag flag;
            string code = CodeOf(emoji);
            if (code == null || !ByCode.TryGetValue(code, out flag))
                return null;

            int height = (int)Math.Round(width / flag.Aspect);
            var rect = new RectangleF(0, 0, width, height);
            using (var field = new Bitmap(width, height, PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(field))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    flag.Paint(g, rect);
                }

                // скругление через текстурную кисть — так края сглажены (обрезка SetClip их не сглаживает)
                var result = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(result))
                using (var brush = new TextureBrush(field, WrapMode.Clamp))
                using (var shape = RoundRect(rect, height * 0.12f))
                using (var outline = new Pen(Color.FromArgb(60, 0, 0, 0), Math.Max(1f, width / 40f)))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.FillPath(brush, shape);
                    float inset = outline.Width / 2;
                    using (var edge = RoundRect(new RectangleF(inset, inset, width - inset * 2, height - inset * 2), height * 0.12f - inset))
                        g.DrawPath(outline, edge);
                }
                return result;
            }
        }

        static void Add(string code, string name, Action<Graphics, RectangleF> paint, float aspect = 1.5f)
        {
            Flags.Add(new Flag { Code = code, Name = name, Paint = paint, Aspect = aspect });
        }

        #region Составные флаги

        static void UnitedStates(Graphics g, RectangleF r)
        {
            var colors = Enumerable.Range(0, 13).Select(i => i % 2 == 0 ? "#B22234" : "#FFFFFF").ToArray();
            H(colors)(g, r);
            var canton = new RectangleF(r.X, r.Y, r.Width * 0.4f, r.Height * 7 / 13f);
            using (var blue = new SolidBrush(C("#3C3B6E")))
                g.FillRectangle(blue, canton);
            using (var white = new SolidBrush(Color.White))
            {
                float dot = canton.Height * 0.09f;
                for (int row = 0; row < 5; row++)
                    for (int col = 0; col < 6; col++)
                    {
                        float x = canton.X + canton.Width * (col + 0.5f + (row % 2) * 0.25f - 0.125f) / 6f;
                        float y = canton.Y + canton.Height * (row + 0.5f) / 5f;
                        g.FillEllipse(white, x - dot / 2, y - dot / 2, dot, dot);
                    }
            }
        }

        /// <summary>Флаг Великобритании в прямоугольнике r (используется и как кантон).</summary>
        static void UnionJack(Graphics g, RectangleF r)
        {
            var state = g.Save();
            g.SetClip(r, CombineMode.Intersect);
            using (var blue = new SolidBrush(C("#012169")))
                g.FillRectangle(blue, r);
            float h = r.Height;
            float dx = r.Width * 0.2f, dy = r.Height * 0.2f; // линии чуть длиннее флага, чтобы концы не обрезались
            using (var white = new Pen(Color.White, h * 0.2f))
            using (var red = new Pen(C("#C8102E"), h * 0.067f))
            {
                foreach (var pen in new[] { white, red })
                {
                    g.DrawLine(pen, r.Left - dx, r.Top - dy, r.Right + dx, r.Bottom + dy);
                    g.DrawLine(pen, r.Right + dx, r.Top - dy, r.Left - dx, r.Bottom + dy);
                }
            }
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            using (var white = new SolidBrush(Color.White))
            {
                g.FillRectangle(white, r.X, cy - h / 6, r.Width, h / 3);
                g.FillRectangle(white, cx - h / 6, r.Y, h / 3, h);
            }
            using (var red = new SolidBrush(C("#C8102E")))
            {
                g.FillRectangle(red, r.X, cy - h / 10, r.Width, h / 5);
                g.FillRectangle(red, cx - h / 10, r.Y, h / 5, h);
            }
            g.Restore(state);
        }

        static void Switzerland(Graphics g, RectangleF r)
        {
            Fill(g, r, "#DA291C");
            Rect(g, r, "#FFFFFF", 0.2f, 0.4f, 0.6f, 0.2f);
            Rect(g, r, "#FFFFFF", 0.4f, 0.2f, 0.2f, 0.6f);
        }

        static void Greece(Graphics g, RectangleF r)
        {
            H(Enumerable.Range(0, 9).Select(i => i % 2 == 0 ? "#0D5EAF" : "#FFFFFF").ToArray())(g, r);
            Rect(g, r, "#0D5EAF", 0, 0, 5 / 9f, 5 / 9f);
            Rect(g, r, "#FFFFFF", 0, 2 / 9f, 5 / 9f, 1 / 9f);
            Rect(g, r, "#FFFFFF", 2 / 9f, 0, 1 / 9f, 5 / 9f);
        }

        static void Checkerboard(Graphics g, RectangleF r)
        {
            const int cells = 5;
            float size = 0.36f / cells, left = 0.75f - 0.18f, top = 0.32f;
            for (int row = 0; row < cells; row++)
                for (int col = 0; col < cells; col++)
                    Rect(g, r, (row + col) % 2 == 0 ? "#FF0000" : "#FFFFFF", left + col * size, top + row * size, size, size);
        }

        static void Belarus(Graphics g, RectangleF r)
        {
            HW(new[] { 2f, 1 }, "#C8313E", "#4AA657")(g, r);
            Rect(g, r, "#FFFFFF", 0, 0, 0.17f, 1);
            foreach (float y in new[] { 0.17f, 0.5f, 0.83f })
                Poly(g, r, "#C8313E", 0.085f, y - 0.09f, 0.15f, y, 0.085f, y + 0.09f, 0.02f, y);
        }

        static void Georgia(Graphics g, RectangleF r)
        {
            Fill(g, r, "#FFFFFF");
            Rect(g, r, "#FF0000", 0, 0.4f, 1.5f, 0.2f);
            Rect(g, r, "#FF0000", 0.65f, 0, 0.2f, 1);
            foreach (var p in new[] { new PointF(0.325f, 0.2f), new PointF(1.175f, 0.2f), new PointF(0.325f, 0.8f), new PointF(1.175f, 0.8f) })
            {
                Rect(g, r, "#FF0000", p.X - 0.08f, p.Y - 0.025f, 0.16f, 0.05f);
                Rect(g, r, "#FF0000", p.X - 0.025f, p.Y - 0.08f, 0.05f, 0.16f);
            }
        }

        static void Israel(Graphics g, RectangleF r)
        {
            Fill(g, r, "#FFFFFF");
            Rect(g, r, "#0038B8", 0, 0.1f, 1.5f, 0.15f);
            Rect(g, r, "#0038B8", 0, 0.75f, 1.5f, 0.15f);
            using (var pen = new Pen(C("#0038B8"), r.Height * 0.035f) { LineJoin = LineJoin.Miter })
            {
                for (int t = 0; t < 2; t++)
                {
                    var points = new PointF[3];
                    for (int i = 0; i < 3; i++)
                    {
                        double a = (t == 0 ? -Math.PI / 2 : Math.PI / 2) + i * 2 * Math.PI / 3;
                        points[i] = P(r, 0.75f + (float)Math.Cos(a) * 0.17f, 0.5f + (float)Math.Sin(a) * 0.17f);
                    }
                    g.DrawPolygon(pen, points);
                }
            }
        }

        static void SouthKorea(Graphics g, RectangleF r)
        {
            Fill(g, r, "#FFFFFF");
            float cx = 0.75f, cy = 0.5f, radius = 0.25f;
            using (var red = new SolidBrush(C("#CD2E3A")))
            using (var blue = new SolidBrush(C("#0047A0")))
            {
                var box = Box(r, cx - radius, cy - radius, radius * 2, radius * 2);
                g.FillPie(red, box.X, box.Y, box.Width, box.Height, 180, 180);
                g.FillPie(blue, box.X, box.Y, box.Width, box.Height, 0, 180);
            }
            Circle(g, r, "#CD2E3A", cx - radius / 2, cy, radius / 2);
            Circle(g, r, "#0047A0", cx + radius / 2, cy, radius / 2);

            // триграммы — по три чёрные полосы в углах, поперёк диагоналей
            var diagonals = new[] { new PointF(-1, -1), new PointF(1, 1), new PointF(1, -1), new PointF(-1, 1) };
            using (var black = new SolidBrush(Color.Black))
            {
                foreach (var d in diagonals)
                {
                    double angle = Math.Atan2(d.Y * 0.5, d.X * 0.75);
                    var center = P(r, cx + (float)Math.Cos(angle) * 0.47f, cy + (float)Math.Sin(angle) * 0.47f);
                    var state = g.Save();
                    g.TranslateTransform(center.X, center.Y);
                    g.RotateTransform((float)(angle * 180 / Math.PI) + 90);
                    float h = r.Height, length = 0.2f * h, thickness = 0.035f * h;
                    for (int i = -1; i <= 1; i++)
                        g.FillRectangle(black, -length / 2, i * thickness * 1.6f - thickness / 2, length, thickness);
                    g.Restore(state);
                }
            }
        }

        static void Brazil(Graphics g, RectangleF r)
        {
            Fill(g, r, "#009C3B");
            Poly(g, r, "#FEDF00", 0.13f, 0.5f, 0.75f, 0.09f, 1.37f, 0.5f, 0.75f, 0.91f);
            Circle(g, r, "#002776", 0.75f, 0.5f, 0.25f);
            var state = g.Save();
            using (var clip = new GraphicsPath())
            {
                clip.AddEllipse(Box(r, 0.5f, 0.25f, 0.5f, 0.5f));
                g.SetClip(clip, CombineMode.Intersect);
                using (var band = new Pen(Color.White, r.Height * 0.045f))
                {
                    var arc = Box(r, 0.3f, 0.42f, 0.95f, 0.95f);
                    g.DrawArc(band, arc, 200, 120);
                }
            }
            g.Restore(state);
        }

        static void Australia(Graphics g, RectangleF r)
        {
            Fill(g, r, "#012169");
            UnionJack(g, Box(r, 0, 0, 0.75f, 0.5f));
            Star(g, r, "#FFFFFF", 0.375f, 0.75f, 0.15f, 0.45f, 7);
            Star(g, r, "#FFFFFF", 1.125f, 0.17f, 0.065f, 0.45f, 7);
            Star(g, r, "#FFFFFF", 1.125f, 0.83f, 0.065f, 0.45f, 7);
            Star(g, r, "#FFFFFF", 0.97f, 0.42f, 0.065f, 0.45f, 7);
            Star(g, r, "#FFFFFF", 1.3f, 0.38f, 0.065f, 0.45f, 7);
            Star(g, r, "#FFFFFF", 1.2f, 0.55f, 0.04f);
        }

        static void NewZealand(Graphics g, RectangleF r)
        {
            Fill(g, r, "#012169");
            UnionJack(g, Box(r, 0, 0, 0.75f, 0.5f));
            foreach (var p in new[] { new PointF(1.12f, 0.2f), new PointF(0.98f, 0.45f), new PointF(1.28f, 0.4f), new PointF(1.12f, 0.8f) })
            {
                Star(g, r, "#FFFFFF", p.X, p.Y, 0.085f);
                Star(g, r, "#C8102E", p.X, p.Y, 0.06f);
            }
        }

        static void MapleLeaf(Graphics g, RectangleF r, float cx, float cy, float scale)
        {
            float[] right =
            {
                0f, -1f, 0.17f, -0.68f, 0.38f, -0.78f, 0.3f, -0.3f, 0.68f, -0.52f, 0.6f, -0.3f, 0.92f, -0.22f,
                0.78f, 0f, 0.85f, 0.12f, 0.42f, 0.42f, 0.48f, 0.58f, 0.05f, 0.5f, 0.05f, 0.95f,
            };
            var points = new List<PointF>();
            for (int i = 0; i < right.Length; i += 2)
                points.Add(P(r, cx + right[i] * scale, cy + right[i + 1] * scale));
            for (int i = right.Length - 2; i >= 2; i -= 2)
                points.Add(P(r, cx - right[i] * scale, cy + right[i + 1] * scale));
            using (var brush = new SolidBrush(C("#D52B1E")))
                g.FillPolygon(brush, points.ToArray());
        }

        static void Petal(Graphics g, RectangleF r, string color, float cx, float cy, float angle)
        {
            var state = g.Save();
            var center = P(r, cx, cy);
            g.TranslateTransform(center.X, center.Y);
            g.RotateTransform(angle);
            float h = r.Height;
            using (var brush = new SolidBrush(C(color)))
                g.FillEllipse(brush, -0.06f * h, -0.3f * h, 0.12f * h, 0.26f * h);
            g.Restore(state);
        }

        #endregion

        #region Примитивы

        static Action<Graphics, RectangleF> H(params string[] colors)
        {
            return HW(Enumerable.Repeat(1f, colors.Length).ToArray(), colors);
        }

        /// <summary>Горизонтальные полосы с весами.</summary>
        static Action<Graphics, RectangleF> HW(float[] weights, params string[] colors)
        {
            return (g, r) =>
            {
                float total = weights.Sum(), y = r.Y;
                for (int i = 0; i < colors.Length; i++)
                {
                    // каждая полоса до самого низа — без щелей между полосами
                    using (var brush = new SolidBrush(C(colors[i])))
                        g.FillRectangle(brush, r.X, y, r.Width, r.Bottom - y);
                    y += r.Height * weights[i] / total;
                }
            };
        }

        static Action<Graphics, RectangleF> V(params string[] colors)
        {
            return VW(Enumerable.Repeat(1f, colors.Length).ToArray(), colors);
        }

        /// <summary>Вертикальные полосы с весами.</summary>
        static Action<Graphics, RectangleF> VW(float[] weights, params string[] colors)
        {
            return (g, r) =>
            {
                float total = weights.Sum(), x = r.X;
                for (int i = 0; i < colors.Length; i++)
                {
                    using (var brush = new SolidBrush(C(colors[i])))
                        g.FillRectangle(brush, x, r.Y, r.Right - x, r.Height);
                    x += r.Width * weights[i] / total;
                }
            };
        }

        /// <summary>Скандинавский крест; inner — узкий крест внутри (Норвегия, Исландия).</summary>
        static Action<Graphics, RectangleF> Nordic(string background, string cross, string inner)
        {
            return (g, r) =>
            {
                Fill(g, r, background);
                float cx = 0.525f, half = inner == null ? 0.125f : 0.15f;
                Rect(g, r, cross, cx - half, 0, half * 2, 1);
                Rect(g, r, cross, 0, 0.5f - half, 1.5f, half * 2);
                if (inner != null)
                {
                    Rect(g, r, inner, cx - 0.075f, 0, 0.15f, 1);
                    Rect(g, r, inner, 0, 0.425f, 1.5f, 0.15f);
                }
            };
        }

        static Action<Graphics, RectangleF> Then(Action<Graphics, RectangleF> first, Action<Graphics, RectangleF> second)
        {
            return (g, r) =>
            {
                first(g, r);
                second(g, r);
            };
        }

        static Color C(string hex)
        {
            return ColorTranslator.FromHtml(hex);
        }

        static PointF P(RectangleF r, float x, float y)
        {
            return new PointF(r.X + x * r.Height, r.Y + y * r.Height);
        }

        static RectangleF Box(RectangleF r, float x, float y, float w, float h)
        {
            return new RectangleF(r.X + x * r.Height, r.Y + y * r.Height, w * r.Height, h * r.Height);
        }

        static void Fill(Graphics g, RectangleF r, string color)
        {
            using (var brush = new SolidBrush(C(color)))
                g.FillRectangle(brush, r);
        }

        static void Rect(Graphics g, RectangleF r, string color, float x, float y, float w, float h)
        {
            using (var brush = new SolidBrush(C(color)))
                g.FillRectangle(brush, Box(r, x, y, w, h));
        }

        static void Circle(Graphics g, RectangleF r, string color, float cx, float cy, float radius)
        {
            Ellipse(g, r, color, cx, cy, radius, radius);
        }

        static void Ellipse(Graphics g, RectangleF r, string color, float cx, float cy, float rx, float ry)
        {
            using (var brush = new SolidBrush(C(color)))
                g.FillEllipse(brush, Box(r, cx - rx, cy - ry, rx * 2, ry * 2));
        }

        static void Ring(Graphics g, RectangleF r, string color, float cx, float cy, float radius, float width)
        {
            using (var pen = new Pen(C(color), width * r.Height))
                g.DrawEllipse(pen, Box(r, cx - radius, cy - radius, radius * 2, radius * 2));
        }

        static void Poly(Graphics g, RectangleF r, string color, params float[] xy)
        {
            var points = new PointF[xy.Length / 2];
            for (int i = 0; i < points.Length; i++)
                points[i] = P(r, xy[i * 2], xy[i * 2 + 1]);
            using (var brush = new SolidBrush(C(color)))
                g.FillPolygon(brush, points);
        }

        /// <summary>Звезда с points лучами; innerRatio — отношение внутреннего радиуса к внешнему.</summary>
        static void Star(Graphics g, RectangleF r, string color, float cx, float cy, float radius,
            float innerRatio = 0.382f, int points = 5, float rotation = -90)
        {
            var vertices = new PointF[points * 2];
            for (int i = 0; i < vertices.Length; i++)
            {
                double angle = (rotation + i * 180.0 / points) * Math.PI / 180;
                float rad = i % 2 == 0 ? radius : radius * innerRatio;
                vertices[i] = P(r, cx + (float)Math.Cos(angle) * rad, cy + (float)Math.Sin(angle) * rad);
            }
            using (var brush = new SolidBrush(C(color)))
                g.FillPolygon(brush, vertices);
        }

        static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Max(1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        #endregion
    }
}
