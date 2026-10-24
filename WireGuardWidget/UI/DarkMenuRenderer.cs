using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WireGuardWidget.UI
{
    /// <summary>Оформление всех ContextMenuStrip в цветах темы (подключается через ToolStripManager.Renderer).</summary>
    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var menuItem = e.Item as ToolStripMenuItem;
            bool isShortcut = menuItem != null && !string.IsNullOrEmpty(menuItem.ShortcutKeyDisplayString)
                && e.Text == menuItem.ShortcutKeyDisplayString;
            e.TextColor = !e.Item.Enabled || isShortcut ? Theme.TextMuted : Theme.Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.TextMuted;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // У пунктов с иконкой «отмечено» = подсветка под иконкой (её рисует базовый рендерер цветом CheckBackground).
            Rectangle r = e.ImageRectangle;
            if (r.Width <= 0 || e.Item.Image != null)
                return;
            var g = e.Graphics;
            var oldMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.DrawCheckMark(g, Theme.Text, new RectangleF(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), Theme.S(1.6f));
            g.SmoothingMode = oldMode;
        }

        sealed class DarkColorTable : ProfessionalColorTable
        {
            static Color Checked { get { return Theme.Blend(Theme.Surface, Theme.Success, 0.3f); } }
            static Color CheckedHover { get { return Theme.Blend(Theme.SurfaceHover, Theme.Success, 0.4f); } }

            public override Color ToolStripDropDownBackground { get { return Theme.Surface; } }
            public override Color ImageMarginGradientBegin { get { return Theme.Surface; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.Surface; } }
            public override Color ImageMarginGradientEnd { get { return Theme.Surface; } }
            public override Color MenuBorder { get { return Theme.Border; } }
            public override Color MenuItemBorder { get { return Theme.SurfaceHover; } }
            public override Color MenuItemSelected { get { return Theme.SurfaceHover; } }
            public override Color MenuItemSelectedGradientBegin { get { return Theme.SurfaceHover; } }
            public override Color MenuItemSelectedGradientEnd { get { return Theme.SurfaceHover; } }
            public override Color MenuItemPressedGradientBegin { get { return Theme.SurfaceHover; } }
            public override Color MenuItemPressedGradientMiddle { get { return Theme.SurfaceHover; } }
            public override Color MenuItemPressedGradientEnd { get { return Theme.SurfaceHover; } }
            public override Color SeparatorDark { get { return Theme.Border; } }
            public override Color SeparatorLight { get { return Theme.Surface; } }
            public override Color CheckBackground { get { return Checked; } }
            public override Color CheckSelectedBackground { get { return CheckedHover; } }
            public override Color CheckPressedBackground { get { return CheckedHover; } }
            public override Color ButtonSelectedBorder { get { return Checked; } }
        }
    }
}
