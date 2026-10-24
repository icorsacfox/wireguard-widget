using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using WireGuardWidget.Emoji;
using WireGuardWidget.WireGuard;

namespace WireGuardWidget.UI
{
    internal sealed class TunnelRow
    {
        public string Name;
        public string Emoji;
        /// <summary>1…9 — номер для хоткея, 0 — без номера.</summary>
        public int Number;
        public TunnelState State;
    }

    /// <summary>
    /// Список конфигураций: номер, эмодзи (клик — выбор иконки), имя и статус, кнопка подключения.
    /// Строки можно перетаскивать мышью — так меняется порядок и номера хоткеев.
    /// </summary>
    internal sealed class TunnelListControl : Control
    {
        enum Part { None, Row, Emoji, Button }

        List<TunnelRow> rows = new List<TunnelRow>();
        string emptyText = "";
        int scroll;
        int hoverIndex = -1;
        Part hoverPart;
        int pressIndex = -1;
        Part pressPart;
        Point pressPoint;
        bool dragging;
        int dropIndex = -1;

        public TunnelListControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable | ControlStyles.StandardDoubleClick, false);

        }

        public event Action<string> ToggleRequested;
        /// <summary>Клик по эмодзи: имя и экранный прямоугольник иконки.</summary>
        public event Action<string, Rectangle> EmojiRequested;
        public event Action<string, Point> MenuRequested;
        /// <summary>Перетаскивание: откуда, куда (индексы в списке).</summary>
        public event Action<int, int> MoveRequested;

        public static int RowHeight { get { return Theme.S(50); } }
        static int RowGap { get { return Theme.S(4); } }
        static int Pad { get { return Theme.S(10); } }

        public int RowCount { get { return rows.Count; } }

        public string EmptyText
        {
            get { return emptyText; }
            set
            {
                if (emptyText == value)
                    return;
                emptyText = value ?? "";
                Invalidate();
            }
        }

        public void SetRows(List<TunnelRow> newRows)
        {
            if (dragging && newRows.Count != rows.Count)
                CancelDrag();
            rows = newRows;
            ClampScroll();
            Invalidate();
        }

        public int PreferredHeight(int maxRows)
        {
            if (rows.Count == 0)
                return Theme.S(110);
            return Pad * 2 + Math.Min(rows.Count, maxRows) * RowHeight - RowGap;
        }

        int ContentHeight { get { return Pad * 2 + rows.Count * RowHeight - RowGap; } }
        int MaxScroll { get { return Math.Max(0, ContentHeight - Height); } }

        void ClampScroll()
        {
            scroll = Math.Max(0, Math.Min(scroll, MaxScroll));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ClampScroll();
        }

        #region Геометрия

        Rectangle RowRect(int index)
        {
            int scrollbar = MaxScroll > 0 ? Theme.S(8) : 0;
            return new Rectangle(Pad, Pad + index * RowHeight - scroll, Width - Pad * 2 - scrollbar, RowHeight - RowGap);
        }

        static Rectangle NumberRect(Rectangle row)
        {
            return new Rectangle(row.X + Theme.S(4), row.Y, Theme.S(22), row.Height);
        }

        static Rectangle EmojiRect(Rectangle row)
        {
            int size = Theme.S(34);
            return new Rectangle(row.X + Theme.S(26), row.Y + (row.Height - size) / 2, size, size);
        }

        static Rectangle ButtonRect(Rectangle row)
        {
            int width = Theme.S(100), height = Theme.S(30);
            return new Rectangle(row.Right - Theme.S(10) - width, row.Y + (row.Height - height) / 2, width, height);
        }

        static Rectangle TextRect(Rectangle row)
        {
            int left = EmojiRect(row).Right + Theme.S(10);
            return new Rectangle(left, row.Y, ButtonRect(row).Left - Theme.S(8) - left, row.Height);
        }

        Part HitTest(Point point, out int index)
        {
            index = -1;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = RowRect(i);
                if (!row.Contains(point))
                    continue;
                index = i;
                if (ButtonRect(row).Contains(point))
                    return Part.Button;
                if (EmojiRect(row).Contains(point))
                    return Part.Emoji;
                return Part.Row;
            }
            return Part.None;
        }

        #endregion

        #region Отрисовка

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            if (rows.Count == 0)
            {
                var rect = Rectangle.Inflate(ClientRectangle, -Theme.S(24), -Theme.S(12));
                TextRenderer.DrawText(g, emptyText, Theme.UiFont, rect, Theme.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak
                    | TextFormatFlags.NoPrefix);
                return;
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = RowRect(i);
                if (row.Bottom >= 0 && row.Top <= Height)
                    PaintRow(g, i, row);
            }

            if (dragging && dropIndex >= 0)
            {
                int y = Pad + dropIndex * RowHeight - scroll - RowGap / 2;
                var row = RowRect(0);
                Theme.FillRound(g, Theme.Accent, new RectangleF(row.X, y - Theme.S(1.5f), row.Width, Theme.S(3)), Theme.S(1.5f));
            }

            if (MaxScroll > 0)
            {
                int trackTop = Pad, trackHeight = Height - Pad * 2;
                int thumbHeight = Math.Max(Theme.S(24), trackHeight * Height / ContentHeight);
                int thumbTop = trackTop + (trackHeight - thumbHeight) * scroll / MaxScroll;
                Theme.FillRound(g, Theme.Border, new RectangleF(Width - Theme.S(6), thumbTop, Theme.S(3), thumbHeight), Theme.S(1.5f));
            }
        }

        void PaintRow(Graphics g, int index, Rectangle r)
        {
            var row = rows[index];
            bool hovered = index == hoverIndex && !dragging;
            bool dragged = dragging && index == pressIndex;
            Theme.FillRound(g, hovered || dragged ? Theme.SurfaceHover : Theme.Surface, r, Theme.S(8));
            if (dragged)
                Theme.DrawRound(g, Theme.Accent, r, Theme.S(8));

            if (row.State == TunnelState.Active)
                Theme.FillRound(g, Theme.Success, new RectangleF(r.X, r.Y + Theme.S(12), Theme.S(3), r.Height - Theme.S(24)), Theme.S(1.5f));

            if (row.Number > 0)
            {
                TextRenderer.DrawText(g, row.Number.ToString(), Theme.SmallFont, NumberRect(r), Theme.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }

            var emojiRect = EmojiRect(r);
            if (hovered && hoverPart == Part.Emoji)
                Theme.FillRound(g, Theme.Border, emojiRect, Theme.S(6));
            int emojiSize = Theme.S(24);
            g.DrawImage(EmojiRenderer.Shared.Get(row.Emoji, emojiSize),
                emojiRect.X + (emojiRect.Width - emojiSize) / 2, emojiRect.Y + (emojiRect.Height - emojiSize) / 2, emojiSize, emojiSize);

            var text = TextRect(r);
            int half = text.Height / 2;
            TextRenderer.DrawText(g, row.Name, Theme.NameFont, new Rectangle(text.X, text.Y + Theme.S(2), text.Width, half),
                Theme.Text, TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix
                | TextFormatFlags.EndEllipsis);
            string status;
            Color statusColor;
            switch (row.State)
            {
                case TunnelState.Active: status = "подключено"; statusColor = Theme.Success; break;
                case TunnelState.Connecting: status = "подключение…"; statusColor = Theme.Warning; break;
                case TunnelState.Disconnecting: status = "отключение…"; statusColor = Theme.Warning; break;
                default: status = "отключено"; statusColor = Theme.TextMuted; break;
            }
            TextRenderer.DrawText(g, status, Theme.SmallFont, new Rectangle(text.X, text.Y + half, text.Width, half),
                statusColor, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

            PaintButton(g, ButtonRect(r), row.State, hovered && hoverPart == Part.Button);
        }

        static void PaintButton(Graphics g, Rectangle b, TunnelState state, bool hovered)
        {
            string text;
            Color fg;
            float radius = Theme.S(6);
            switch (state)
            {
                case TunnelState.Active:
                    text = "Отключить";
                    fg = Theme.Danger;
                    Theme.FillRound(g, Theme.Blend(Theme.Surface, Theme.Danger, hovered ? 0.24f : 0.1f), b, radius);
                    Theme.DrawRound(g, Theme.Blend(Theme.Surface, Theme.Danger, 0.45f), b, radius);
                    break;
                case TunnelState.Connecting:
                    text = "Отмена";
                    fg = hovered ? Theme.Text : Theme.TextMuted;
                    Theme.FillRound(g, hovered ? Theme.SurfaceHover : Theme.Surface, b, radius);
                    Theme.DrawRound(g, Theme.Border, b, radius);
                    break;
                case TunnelState.Disconnecting:
                    text = "…";
                    fg = Theme.TextMuted;
                    Theme.DrawRound(g, Theme.Border, b, radius);
                    break;
                default:
                    text = "Подключить";
                    fg = Color.White;
                    Theme.FillRound(g, hovered ? Theme.AccentHover : Theme.Accent, b, radius);
                    break;
            }
            TextRenderer.DrawText(g, text, Theme.UiFont, b, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        #endregion

        #region Мышь

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (pressIndex >= 0 && pressPart == Part.Row && e.Button == MouseButtons.Left)
            {
                if (!dragging && Math.Abs(e.Y - pressPoint.Y) > Theme.S(5) && rows.Count > 1)
                {
                    dragging = true;
                    Cursor = Cursors.SizeNS;
                }
                if (dragging)
                {
                    if (e.Y < Pad) scroll -= Theme.S(8);
                    else if (e.Y > Height - Pad) scroll += Theme.S(8);
                    ClampScroll();
                    int position = e.Y + scroll - Pad + RowHeight / 2;
                    dropIndex = Math.Max(0, Math.Min(rows.Count, position / RowHeight));
                    Invalidate();
                    return;
                }
            }

            int index;
            var part = HitTest(e.Location, out index);
            if (index != hoverIndex || part != hoverPart)
            {
                hoverIndex = index;
                hoverPart = part;
                Cursor = part == Part.Button || part == Part.Emoji ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (dragging)
                return;
            hoverIndex = -1;
            hoverPart = Part.None;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                pressPart = HitTest(e.Location, out pressIndex);
                pressPoint = e.Location;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                if (dragging)
                {
                    int from = pressIndex, to = dropIndex;
                    CancelDrag();
                    if (to > from)
                        to--;
                    if (to >= 0 && to != from && MoveRequested != null)
                        MoveRequested(from, to);
                    return;
                }
                int index;
                var part = HitTest(e.Location, out index);
                if (index >= 0 && index == pressIndex && part == pressPart)
                {
                    string name = rows[index].Name;
                    if (part == Part.Button && ToggleRequested != null)
                        ToggleRequested(name);
                    else if (part == Part.Emoji && EmojiRequested != null)
                        EmojiRequested(name, RectangleToScreen(EmojiRect(RowRect(index))));
                }
                pressIndex = -1;
            }
            else if (e.Button == MouseButtons.Right)
            {
                int index;
                HitTest(e.Location, out index);
                if (index >= 0 && MenuRequested != null)
                    MenuRequested(rows[index].Name, PointToScreen(e.Location));
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (MaxScroll <= 0)
                return;
            scroll -= e.Delta * RowHeight / 120;
            ClampScroll();
            Invalidate();
        }

        void CancelDrag()
        {
            dragging = false;
            pressIndex = -1;
            dropIndex = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }

        #endregion
    }
}
