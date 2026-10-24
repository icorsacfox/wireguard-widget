using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WireGuardWidget.UI
{
    /// <summary>Подтверждение добавления .conf: имя туннеля (с проверкой) и «подключить сразу».</summary>
    internal sealed class AddConfigDialog : Form
    {
        readonly Func<string, string> validate;
        readonly DarkTextBox txtName;
        readonly DarkCheckBox chkConnect;
        readonly DarkButton btnAdd;
        readonly Label lblError;

        /// <param name="validate">Проверка имени: текст ошибки или null.</param>
        public AddConfigDialog(string filePath, string suggestedName, Func<string, string> validate)
        {
            this.validate = validate;
            Text = "Добавить конфигурацию";
            Font = Theme.UiFont;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;

            int width = Theme.S(380), pad = Theme.S(16);
            int inner = width - pad * 2;

            var lblFile = MakeLabel("Файл", pad, pad);
            var lblPath = new Label
            {
                Text = Path.GetFileName(filePath),
                AutoEllipsis = true,
                Font = Theme.UiFont,
                ForeColor = Theme.Text,
                BackColor = Theme.Bg,
                Location = new Point(pad, lblFile.Bottom + Theme.S(2)),
                Size = new Size(inner, Theme.S(20)),
            };
            var tip = Theme.CreateToolTip();
            tip.SetToolTip(lblPath, filePath);

            var lblName = MakeLabel("Имя туннеля", pad, lblPath.Bottom + Theme.S(10));
            txtName = new DarkTextBox
            {
                FillColor = Theme.InputFill,
                Location = new Point(pad, lblName.Bottom + Theme.S(4)),
                Size = new Size(inner, Theme.S(30)),
                Text = suggestedName,
            };
            txtName.Inner.MaxLength = 32;
            txtName.Inner.TextChanged += (s, e) => Revalidate();

            lblError = new Label
            {
                Font = Theme.SmallFont,
                ForeColor = Theme.Danger,
                BackColor = Theme.Bg,
                Location = new Point(pad, txtName.Bottom + Theme.S(4)),
                Size = new Size(inner, Theme.S(18)),
                AutoEllipsis = true,
            };

            chkConnect = new DarkCheckBox { Text = "Подключить после добавления" };
            chkConnect.FitToText();
            chkConnect.Location = new Point(pad, lblError.Bottom + Theme.S(4));

            int buttonsTop = chkConnect.Bottom + Theme.S(16);
            btnAdd = new DarkButton
            {
                Text = "Добавить",
                Kind = ButtonKind.Primary,
                Size = new Size(Theme.S(100), Theme.S(32)),
                Location = new Point(width - pad - Theme.S(100), buttonsTop),
            };
            btnAdd.Click += (s, e) => Accept();
            var btnCancel = new DarkButton
            {
                Text = "Отмена",
                Kind = ButtonKind.Secondary,
                Size = new Size(Theme.S(90), Theme.S(32)),
                Location = new Point(btnAdd.Left - Theme.S(8) - Theme.S(90), buttonsTop),
            };
            btnCancel.Click += (s, e) => DialogResult = DialogResult.Cancel;

            Controls.AddRange(new Control[] { lblFile, lblPath, lblName, txtName, lblError, chkConnect, btnCancel, btnAdd });
            ClientSize = new Size(width, btnAdd.Bottom + pad);
            Revalidate();
        }

        public string TunnelName { get { return txtName.Text.Trim(); } }
        public bool ConnectAfterAdding { get { return chkConnect.Checked; } }

        static Label MakeLabel(string text, int x, int y)
        {
            var label = new Label { Text = text, AutoSize = true, Font = Theme.SmallFont, ForeColor = Theme.TextMuted, BackColor = Theme.Bg, Location = new Point(x, y) };
            label.Height = label.PreferredHeight;
            return label;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyTitleBar(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            txtName.Inner.Focus();
            txtName.Inner.SelectAll();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                Accept();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
            }
        }

        bool Revalidate()
        {
            string error = validate(TunnelName);
            lblError.Text = error ?? "";
            btnAdd.Enabled = error == null;
            btnAdd.Invalidate();
            return error == null;
        }

        void Accept()
        {
            if (Revalidate())
                DialogResult = DialogResult.OK;
        }
    }
}
