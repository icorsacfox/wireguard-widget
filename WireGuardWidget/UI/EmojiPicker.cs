using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using WireGuardWidget.Emoji;

namespace WireGuardWidget.UI
{
    /// <summary>
    /// Всплывающая палитра: вкладки «Эмодзи» и «Флаги» + поле для любого своего эмодзи
    /// (Win + . открывает системную панель эмодзи).
    /// </summary>
    internal sealed class EmojiPicker : Form
    {
        static readonly string[] Emojis =
        {
            "🌐", "🌍", "🌎", "🌏", "🗺️", "🏠", "🏢", "🏰", "🗽", "🗼",
            "🛡️", "🔒", "🔐", "🔑", "🧱", "🚀", "✈️", "🚄", "⚡", "🔥",
            "🟢", "🔵", "🟣", "🟠", "🟡", "🔴", "🟤", "⚫", "⚪", "💎",
            "⭐", "🌙", "☀️", "🌈", "🍀", "🌷", "🌲", "🌵", "🍁", "🌊",
            "🐱", "🐶", "🦊", "🐻", "🐼", "🐧", "🦉", "🐙", "🦄", "🐉",
            "💼", "🎮", "🎬", "🎵", "📺", "💻", "🧪", "🔧", "📦", "👻",
        };

        static readonly string[] Flags = FlagPainter.All.ToArray();
        static readonly string[] TabNames = { "Эмодзи", "Флаги" };

        const int Columns = 10;
        readonly string tunnelName;
        readonly string current;
        readonly DarkTextBox custom;
        readonly DarkButton apply;
        readonly Rectangle[] tabRects;
        int tab;
        int hover = -1, hoverTab = -1;
        bool committed, closing, customUsed;

        public EmojiPicker(string tunnelName, string currentEmoji)
        {
            this.tunnelName = tunnelName;
            current = currentEmoji;
            tab = FlagPainter.IsFlag(currentEmoji) ? 1 : 0;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Surface;
            Font = Theme.UiFont;
            DoubleBuffered = true;

            tabRects = new Rectangle[TabNames.Length];
            int x = Pad;
            for (int i = 0; i < TabNames.Length; i++)
            {
                int width = TextRenderer.MeasureText(TabNames[i], Theme.UiFont).Width + Theme.S(22);
                tabRects[i] = new Rectangle(x, Pad, width, Theme.S(28));
                x += width + Theme.S(4);
            }

            int rows = (Math.Max(Emojis.Length, Flags.Length) + Columns - 1) / Columns;
            int formWidth = Pad * 2 + Columns * Cell;
            int inputTop = GridTop + rows * Cell + Theme.S(10);
            ClientSize = new Size(formWidth, inputTop + Theme.S(32) + Pad);

            apply = new DarkButton
            {
                Text = "OK",
                Kind = ButtonKind.Primary,
                Size = new Size(Theme.S(56), Theme.S(32)),
                Location = new Point(formWidth - Pad - Theme.S(56), inputTop),
            };
            apply.Click += (s, e) => Commit(custom.Text);

            custom = new DarkTextBox
            {
                FillColor = Theme.InputFill,
                Location = new Point(Pad, inputTop),
                Size = new Size(apply.Left - Theme.S(8) - Pad, Theme.S(32)),
            };
            custom.SetPlaceholder("Свой эмодзи (Win + .)");
            // поле получает фокус само при показе, поэтому «используется» — только после ввода или клика
            custom.Inner.KeyDown += (s, e) => customUsed = true;
            custom.Inner.MouseDown += (s, e) => customUsed = true;
            custom.Inner.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    Commit(custom.Text);
                }
            };

            Controls.Add(custom);
            Controls.Add(apply);
        }

        public event Action<string> Picked;

        static int Cell { get { return Theme.S(34); } }
        static int Pad { get { return Theme.S(10); } }
        static int GridTop { get { return Pad + Theme.S(28) + Theme.S(8); } }

        string[] Items { get { return tab == 0 ? Emojis : Flags; } }

        protected override CreateParams CreateParams
        {
            get
            {
                const int CS_DROPSHADOW = 0x20000;
                var cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyPopupChrome(Handle);
        }

        /// <summary>Показать под (или над) прямоугольником anchor в экранных координатах.</summary>
        public void ShowNear(Rectangle anchor, IWin32Window owner)
        {
            var area = Screen.FromRectangle(anchor).WorkingArea;
            int x = Math.Max(area.Left, Math.Min(anchor.Left - Pad, area.Right - Width));
            int y = anchor.Bottom + Theme.S(4);
            if (y + Height > area.Bottom)
                y = Math.Max(area.Top, anchor.Top - Theme.S(4) - Height);
            Location = new Point(x, y);
            Show(owner);
            Activate();
        }

        Rectangle CellRect(int index)
        {
            return new Rectangle(Pad + index % Columns * Cell, GridTop + index / Columns * Cell, Cell, Cell);
        }

        int IndexAt(Point point)
        {
            var items = Items;
            for (int i = 0; i < items.Length; i++)
            {
                if (CellRect(i).Contains(point))
                    return i;
            }
            return -1;
        }

        int TabAt(Point point)
        {
            return Array.FindIndex(tabRects, r => r.Contains(point));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            for (int i = 0; i < tabRects.Length; i++)
            {
                bool selected = i == tab;
                if (selected || i == hoverTab)
                    Theme.FillRound(g, selected ? Theme.SurfaceHover : Theme.Panel, tabRects[i], Theme.S(6));
                TextRenderer.DrawText(g, TabNames[i], Theme.UiFont, tabRects[i], selected || i == hoverTab ? Theme.Text : Theme.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }

            // справа от вкладок — название страны под курсором или имя конфигурации
            var items = Items;
            string caption = tab == 1 && hover >= 0 ? FlagPainter.NameOf(items[hover]) : "для «" + tunnelName + "»";
            int captionLeft = tabRects[tabRects.Length - 1].Right + Theme.S(10);
            TextRenderer.DrawText(g, caption, Theme.SmallFont,
                new Rectangle(captionLeft, tabRects[0].Y, Width - Pad - captionLeft, tabRects[0].Height),
                Theme.TextMuted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            int size = tab == 0 ? Theme.S(22) : Theme.S(26);
            for (int i = 0; i < items.Length; i++)
            {
                var cell = CellRect(i);
                var inner = Rectangle.Inflate(cell, -Theme.S(2), -Theme.S(2));
                if (i == hover)
                    Theme.FillRound(g, Theme.SurfaceHover, inner, Theme.S(6));
                if (items[i] == current)
                    Theme.DrawRound(g, Theme.Accent, inner, Theme.S(6), Math.Max(1f, Theme.S(1.5f)));
                g.DrawImage(EmojiRenderer.Shared.Get(items[i], size),
                    cell.X + (cell.Width - size) / 2, cell.Y + (cell.Height - size) / 2, size, size);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = IndexAt(e.Location);
            int overTab = TabAt(e.Location);
            if (index != hover || overTab != hoverTab)
            {
                hover = index;
                hoverTab = overTab;
                Cursor = index >= 0 || overTab >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = hoverTab = -1;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left)
                return;
            int clickedTab = TabAt(e.Location);
            if (clickedTab >= 0)
            {
                tab = clickedTab;
                hover = -1;
                Invalidate();
                return;
            }
            int index = IndexAt(e.Location);
            if (index >= 0)
                Commit(Items[index]);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
                Close();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            // Если пользователь печатает свой эмодзи, системная панель Win + . может забрать активность — не закрываемся.
            if (!closing && !customUsed)
            {
                closing = true;
                BeginInvoke(new Action(Close));
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            closing = true;
            base.OnFormClosing(e);
        }

        void Commit(string text)
        {
            string emoji = FirstGraphemes((text ?? "").Trim(), 4);
            if (emoji.Length == 0 || committed || closing)
                return;
            committed = true;
            if (Picked != null)
                Picked(emoji);
            Close();
        }

        /// <summary>Первые N «символов» строки, не разрывая суррогатные пары.</summary>
        static string FirstGraphemes(string text, int count)
        {
            var result = new StringBuilder();
            var enumerator = StringInfo.GetTextElementEnumerator(text);
            while (count-- > 0 && enumerator.MoveNext())
                result.Append(enumerator.GetTextElement());
            return result.ToString();
        }
    }
}
