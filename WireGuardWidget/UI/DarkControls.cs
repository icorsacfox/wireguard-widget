using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WireGuardWidget.UI
{
    internal enum ButtonKind { Primary, Secondary, Ghost }

    /// <summary>Плоская кнопка со скруглёнными углами.</summary>
    internal sealed class DarkButton : Control
    {
        ButtonKind kind = ButtonKind.Secondary;
        bool hover, pressed;

        public DarkButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable | ControlStyles.StandardDoubleClick, false);
            Cursor = Cursors.Hand;
            Font = Theme.UiFont;
            ForeColor = Theme.Text;
        }

        public ButtonKind Kind
        {
            get { return kind; }
            set { kind = value; Invalidate(); }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                pressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            pressed = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new RectangleF(0, 0, Width, Height);
            float radius = Theme.S(6);
            Color fg = Theme.Text;
            switch (kind)
            {
                case ButtonKind.Primary:
                    if (!Enabled)
                    {
                        Theme.FillRound(g, Theme.Border, rect, radius);
                        break;
                    }
                    Theme.FillRound(g, pressed ? Theme.Accent : hover ? Theme.AccentHover : Theme.Accent, rect, radius);
                    fg = Color.White;
                    break;
                case ButtonKind.Secondary:
                    Theme.FillRound(g, pressed ? Theme.Border : hover ? Theme.SurfaceHover : Theme.Surface, rect, radius);
                    Theme.DrawRound(g, Theme.Border, rect, radius);
                    break;
                case ButtonKind.Ghost:
                    if (hover)
                        Theme.FillRound(g, pressed ? Theme.Border : Theme.SurfaceHover, rect, radius);
                    fg = hover ? Theme.Text : Theme.TextMuted;
                    break;
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? fg : Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Флажок в стиле тёмной темы.</summary>
    internal sealed class DarkCheckBox : Control
    {
        bool isChecked, hover;

        public DarkCheckBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable | ControlStyles.StandardDoubleClick, false);
            Cursor = Cursors.Hand;
            Font = Theme.UiFont;
        }

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return isChecked; }
            set
            {
                if (isChecked == value)
                    return;
                isChecked = value;
                Invalidate();
                var handler = CheckedChanged;
                if (handler != null)
                    handler(this, EventArgs.Empty);
            }
        }

        /// <summary>Устанавливает Checked, не вызывая CheckedChanged.</summary>
        public void SetSilently(bool value)
        {
            isChecked = value;
            Invalidate();
        }

        public void FitToText()
        {
            var size = TextRenderer.MeasureText(Text, Font);
            Size = new Size(Theme.S(26) + size.Width, Math.Max(Theme.S(24), size.Height + Theme.S(4)));
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Checked = !Checked;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = false;
            Invalidate();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float boxSize = Theme.S(16);
            var box = new RectangleF(Theme.S(1), (Height - boxSize) / 2f, boxSize, boxSize);
            if (isChecked)
            {
                Theme.FillRound(g, hover ? Theme.AccentHover : Theme.Accent, box, Theme.S(4));
                Theme.DrawCheckMark(g, Color.White, box, Theme.S(1.8f));
            }
            else
            {
                Theme.FillRound(g, Theme.Surface, box, Theme.S(4));
                Theme.DrawRound(g, hover ? Theme.TextMuted : Theme.Border, box, Theme.S(4), Math.Max(1f, Theme.S(1.2f)));
            }
            var textRect = new Rectangle(Theme.S(26), 0, Width - Theme.S(26), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix
                | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Однострочное поле ввода с рамкой в тёмной теме (обёртка над TextBox).</summary>
    internal sealed class DarkTextBox : Control
    {
        readonly TextBox inner;
        Color fillColor = Theme.Surface;

        public DarkTextBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw, true);
            inner = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = fillColor,
                ForeColor = Theme.Text,
                Font = Theme.UiFont,
            };
            inner.GotFocus += (s, e) => Invalidate();
            inner.LostFocus += (s, e) => Invalidate();
            Controls.Add(inner);
            Cursor = Cursors.IBeam;
        }

        public TextBox Inner { get { return inner; } }

        /// <summary>Цвет поля; задание цвета также обновляет цвет текста по текущей теме.</summary>
        public Color FillColor
        {
            get { return fillColor; }
            set
            {
                fillColor = value;
                inner.BackColor = value;
                inner.ForeColor = Theme.Text;
                Invalidate();
            }
        }

        public override string Text
        {
            get { return inner.Text; }
            set { inner.Text = value; }
        }

        /// <summary>Серая подсказка в пустом поле.</summary>
        public void SetPlaceholder(string text)
        {
            if (inner.IsHandleCreated)
                SendMessage(inner.Handle, EM_SETCUEBANNER, (IntPtr)1, text);
            else
                inner.HandleCreated += (s, e) => SendMessage(inner.Handle, EM_SETCUEBANNER, (IntPtr)1, text);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int pad = Theme.S(8);
            inner.Width = Math.Max(10, Width - pad * 2);
            inner.Location = new Point(pad, (Height - inner.Height) / 2);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            inner.Focus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new RectangleF(0, 0, Width, Height);
            Theme.FillRound(g, fillColor, rect, Theme.S(6));
            Theme.DrawRound(g, inner.Focused ? Theme.Accent : Theme.Border, rect, Theme.S(6));
        }

        const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
    }
}
