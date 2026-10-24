using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using WireGuardWidget.WireGuard;

namespace WireGuardWidget.UI
{
    /// <summary>Основное окно: флажок «одно подключение», настройки (сворачиваются), список конфигураций, строка состояния.</summary>
    internal sealed class MainForm : Form
    {
        const int MaxVisibleRows = 8;

        readonly WidgetApp app;
        readonly DarkCheckBox chkSingle, chkHandshake, chkAutostart;
        readonly DarkButton btnAdd, btnOverlay, btnSettings, btnBrowse, btnHotkeys, btnTheme;
        readonly Panel settingsPanel;
        readonly DarkTextBox txtPath;
        readonly TunnelListControl list;
        readonly ContextMenuStrip rowMenu, choiceMenu;
        readonly ToolTip tip;
        readonly Timer footerTimer;
        readonly FileDropHook formDrop, listDrop;
        int footerTop;
        string footerText = "";
        Color footerColor;
        string localMessage;
        Func<Color> localMessageColor;
        DateTime localMessageTime;

        public MainForm(WidgetApp app)
        {
            this.app = app;
            Text = "WireGuard Widget";
            Font = Theme.UiFont;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            DoubleBuffered = true;

            tip = Theme.CreateToolTip();
            rowMenu = new ContextMenuStrip { Font = Theme.UiFont };
            choiceMenu = new ContextMenuStrip { Font = Theme.UiFont };

            // --- шапка ---
            chkSingle = new DarkCheckBox { Text = "Только одно подключение" };
            chkSingle.FitToText();
            chkSingle.Location = new Point(Theme.S(14), (HeaderHeight - chkSingle.Height) / 2);
            chkSingle.SetSilently(app.Settings.SingleConnection);
            chkSingle.CheckedChanged += (s, e) => app.SetSingleConnection(chkSingle.Checked);
            tip.SetToolTip(chkSingle, "Перед подключением отключать все остальные конфигурации");

            btnSettings = IconButton("\uE713");
            btnSettings.Location = new Point(FormWidth - Theme.S(12) - btnSettings.Width, (HeaderHeight - btnSettings.Height) / 2);
            btnSettings.Click += (s, e) => ToggleSettings();
            tip.SetToolTip(btnSettings, "Настройки");

            btnOverlay = IconButton("\uE8A7");
            btnOverlay.Location = new Point(btnSettings.Left - Theme.S(4) - btnOverlay.Width, btnSettings.Top);
            btnOverlay.Click += (s, e) => app.SwitchToOverlay();
            tip.SetToolTip(btnOverlay, "Свернуть в оверлей");

            btnAdd = IconButton("");
            btnAdd.Location = new Point(btnOverlay.Left - Theme.S(4) - btnAdd.Width, btnSettings.Top);
            btnAdd.Click += (s, e) => ChooseConfigFile();
            tip.SetToolTip(btnAdd, "Добавить конфигурацию из .conf-файла (можно перетащить файл в окно)");

            // --- настройки ---
            settingsPanel = new Panel { BackColor = Theme.Panel, Location = new Point(0, HeaderHeight), Size = new Size(FormWidth, SettingsHeight) };

            var lblPath = MakeLabel("Папка установки WireGuard");
            lblPath.Location = new Point(Theme.S(14), Theme.S(10));

            btnBrowse = IconButton("\uE838");
            btnBrowse.Kind = ButtonKind.Secondary;
            btnBrowse.Location = new Point(FormWidth - Theme.S(14) - btnBrowse.Width, Theme.S(30));
            btnBrowse.Click += (s, e) => BrowseForFolder();
            tip.SetToolTip(btnBrowse, "Выбрать папку");

            txtPath = new DarkTextBox
            {
                FillColor = Theme.InputFill,
                Location = new Point(Theme.S(14), Theme.S(30)),
                Size = new Size(btnBrowse.Left - Theme.S(6) - Theme.S(14), btnBrowse.Height),
                Text = app.Settings.WireGuardPath ?? "",
            };
            txtPath.SetPlaceholder(@"C:\Program Files\WireGuard");
            txtPath.Inner.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    ApplyPath();
                }
            };
            txtPath.Inner.Leave += (s, e) => ApplyPath();

            chkHandshake = new DarkCheckBox { Text = "Проверять handshake с сервером после подключения" };
            chkHandshake.FitToText();
            chkHandshake.Location = new Point(Theme.S(14), Theme.S(70));
            chkHandshake.SetSilently(app.Settings.VerifyHandshake);
            chkHandshake.CheckedChanged += (s, e) => app.SetVerifyHandshake(chkHandshake.Checked);
            tip.SetToolTip(chkHandshake, "Подключение считается успешным, только если сервер ответил");

            chkAutostart = new DarkCheckBox { Text = "Запускать при входе в Windows" };
            chkAutostart.FitToText();
            chkAutostart.Location = new Point(Theme.S(14), Theme.S(98));
            chkAutostart.SetSilently(app.Settings.RunAtStartup);
            chkAutostart.CheckedChanged += (s, e) => ToggleAutostart();

            // строки «подпись — кнопка с выпадающим меню»; кнопки выровнены по самой длинной подписи
            var lblHotkeys = MakeLabel("Горячие клавиши");
            var lblTheme = MakeLabel("Тема оформления");
            int choiceLeft = Theme.S(14) + Math.Max(lblHotkeys.PreferredWidth, lblTheme.PreferredWidth) + Theme.S(12);
            btnHotkeys = ChoiceButton(choiceLeft, Theme.S(126), lblHotkeys);
            btnHotkeys.Click += (s, e) => ShowHotkeyMenu();
            UpdateHotkeyButton();
            btnTheme = ChoiceButton(choiceLeft, Theme.S(160), lblTheme);
            btnTheme.Click += (s, e) => ShowThemeMenu();
            UpdateThemeButton();

            settingsPanel.Controls.AddRange(new Control[]
            {
                lblPath, txtPath, btnBrowse, chkHandshake, chkAutostart, lblHotkeys, btnHotkeys, lblTheme, btnTheme,
            });

            // --- список ---
            list = new TunnelListControl();
            list.ToggleRequested += name => app.Manager.Toggle(name);
            list.EmojiRequested += OpenEmojiPicker;
            list.MenuRequested += ShowRowMenu;
            list.MoveRequested += (from, to) => app.MoveTunnel(from, to);

            Controls.AddRange(new Control[] { chkSingle, btnAdd, btnOverlay, btnSettings, settingsPanel, list });

            formDrop = new FileDropHook(this, OnFilesDropped);
            listDrop = new FileDropHook(list, OnFilesDropped);

            footerTimer = new Timer { Interval = 1000 };
            footerTimer.Tick += (s, e) => UpdateFooter();

            Relayout();
            RefreshState();
        }

        static int FormWidth { get { return Theme.S(400); } }
        static int HeaderHeight { get { return Theme.S(50); } }
        static int SettingsHeight { get { return Theme.S(198); } }
        static int FooterHeight { get { return Theme.S(34); } }

        static DarkButton IconButton(string glyph)
        {
            return new DarkButton { Kind = ButtonKind.Ghost, Font = Theme.IconFont, Text = glyph, Size = new Size(Theme.S(34), Theme.S(30)) };
        }

        static DarkButton ChoiceButton(int left, int top, Label caption)
        {
            var button = new DarkButton { Kind = ButtonKind.Secondary, Size = new Size(Theme.S(150), Theme.S(28)), Location = new Point(left, top) };
            caption.Location = new Point(Theme.S(14), top + (button.Height - caption.PreferredHeight) / 2);
            return button;
        }

        static Label MakeLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, Font = Theme.SmallFont, ForeColor = Theme.TextMuted, BackColor = Theme.Panel };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyTitleBar(Handle);
        }

        /// <summary>Перекрасить окно после смены темы.</summary>
        public void ApplyTheme()
        {
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            settingsPanel.BackColor = Theme.Panel;
            foreach (var label in settingsPanel.Controls.OfType<Label>())
            {
                label.BackColor = Theme.Panel;
                label.ForeColor = Theme.TextMuted;
            }
            txtPath.FillColor = Theme.InputFill;
            UpdateThemeButton();
            if (IsHandleCreated)
                Theme.ApplyTitleBar(Handle);
            footerText = null; // принудительно перерисовать строку состояния новым цветом
            UpdateFooter();
            Invalidate(true);
        }

        #region Показ и раскладка

        public void ShowAndActivate()
        {
            if (!Visible)
            {
                Location = InitialLocation();
                Show();
            }
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;
            Activate();
        }

        Point InitialLocation()
        {
            var s = app.Settings;
            if (s.WindowX.HasValue && s.WindowY.HasValue)
            {
                // хотя бы верхняя часть окна должна быть на каком-нибудь экране
                var caption = new Rectangle(s.WindowX.Value, s.WindowY.Value, Width, Theme.S(40));
                var screen = Screen.AllScreens.FirstOrDefault(sc => sc.WorkingArea.IntersectsWith(caption));
                if (screen != null)
                {
                    var area = screen.WorkingArea;
                    return new Point(
                        Math.Max(area.Left, Math.Min(s.WindowX.Value, area.Right - Width)),
                        Math.Max(area.Top, Math.Min(s.WindowY.Value, area.Bottom - Height)));
                }
            }
            var primary = Screen.PrimaryScreen.WorkingArea;
            return new Point(primary.Right - Width - Theme.S(16), primary.Bottom - Height - Theme.S(16));
        }

        void Relayout()
        {
            bool expanded = app.Settings.SettingsExpanded;
            settingsPanel.Visible = expanded;
            btnSettings.Kind = expanded ? ButtonKind.Secondary : ButtonKind.Ghost;
            int top = HeaderHeight + (expanded ? SettingsHeight : 0);
            list.SetBounds(0, top, FormWidth, list.PreferredHeight(MaxVisibleRows));
            footerTop = list.Bottom;
            ClientSize = new Size(FormWidth, footerTop + FooterHeight);

            if (Visible)
            {
                // окно по умолчанию стоит у нижнего края — не даём ему уехать за экран при росте
                var area = Screen.FromControl(this).WorkingArea;
                if (Bottom > area.Bottom)
                    Top = Math.Max(area.Top, area.Bottom - Height);
            }
            Invalidate();
        }

        void ToggleSettings()
        {
            app.Settings.SettingsExpanded = !app.Settings.SettingsExpanded;
            Relayout();
            app.SaveSettings();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            using (var pen = new Pen(Theme.Border))
            {
                g.DrawLine(pen, 0, HeaderHeight - 1, ClientSize.Width, HeaderHeight - 1);
                if (settingsPanel.Visible)
                    g.DrawLine(pen, 0, settingsPanel.Bottom, ClientSize.Width, settingsPanel.Bottom);
                g.DrawLine(pen, 0, footerTop, ClientSize.Width, footerTop);
            }
            TextRenderer.DrawText(g, footerText, Theme.SmallFont,
                new Rectangle(Theme.S(14), footerTop, ClientSize.Width - Theme.S(28), FooterHeight), footerColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix
                | TextFormatFlags.EndEllipsis);
        }

        #endregion

        #region Состояние

        /// <summary>Перечитать состояние из WidgetApp/TunnelManager.</summary>
        public void RefreshState()
        {
            var manager = app.Manager;
            var tunnels = app.Tunnels;
            var rows = new List<TunnelRow>(tunnels.Count);
            for (int i = 0; i < tunnels.Count; i++)
            {
                rows.Add(new TunnelRow
                {
                    Name = tunnels[i].Name,
                    Emoji = tunnels[i].Emoji,
                    Number = i < 9 ? i + 1 : 0,
                    State = manager.GetState(tunnels[i].Name),
                });
            }
            list.EmptyText = manager.ConfigError ?? "Загрузка…";
            int before = list.RowCount;
            list.SetRows(rows);
            if (before != rows.Count)
                Relayout();
            if (manager.ConfigError != null && !WireGuardBackend.IsValidInstallDir(app.Settings.WireGuardPath) && !app.Settings.SettingsExpanded)
            {
                app.Settings.SettingsExpanded = true;
                Relayout();
            }
            UpdateFooter();
        }

        void UpdateFooter()
        {
            var m = app.Manager;
            double statusAge = (DateTime.Now - m.StatusTime).TotalSeconds;
            string text;
            Color color;
            if (localMessage != null && (DateTime.Now - localMessageTime).TotalSeconds < 15)
            {
                text = localMessage;
                color = localMessageColor();
            }
            else if (m.StatusText != null && m.StatusKind == StatusKind.Progress && m.IsBusy)
            {
                text = m.StatusText;
                color = Theme.Text;
            }
            else if (m.StatusText != null && m.StatusKind == StatusKind.Error && statusAge < 30)
            {
                text = m.StatusText;
                color = Theme.Danger;
            }
            else if (m.StatusText != null && m.StatusKind == StatusKind.Success && statusAge < 5)
            {
                text = m.StatusText;
                color = Theme.Success;
            }
            else if (app.FailedHotkeys.Count > 0)
            {
                text = "Заняты другими программами: " + string.Join(", ", app.FailedHotkeys.Select(d => app.HotkeyPrefix + d));
                color = Theme.Warning;
            }
            else if (app.HotkeyPrefix == null)
            {
                text = "Горячие клавиши выключены";
                color = Theme.TextMuted;
            }
            else
            {
                string p = app.HotkeyPrefix;
                text = p + "1…9 — вкл/выкл   ·   " + p + "0 — отключить все";
                color = Theme.TextMuted;
            }

            if (text != footerText || color != footerColor)
            {
                footerText = text;
                footerColor = color;
                Invalidate(new Rectangle(0, footerTop, ClientSize.Width, FooterHeight));
            }
        }

        void ShowLocalError(string message)
        {
            ShowLocalMessage(message, () => Theme.Danger);
        }

        /// <summary>Сообщение в строке состояния; цвет — функцией, чтобы он следовал за сменой темы.</summary>
        void ShowLocalMessage(string message, Func<Color> color)
        {
            localMessage = message;
            localMessageColor = color;
            localMessageTime = DateTime.Now;
            UpdateFooter();
        }

        #endregion

        #region Действия

        void ApplyPath()
        {
            string path = txtPath.Text.Trim();
            if (!string.Equals(path, app.Settings.WireGuardPath ?? "", StringComparison.OrdinalIgnoreCase))
                app.SetWireGuardPath(path);
        }

        void BrowseForFolder()
        {
            using (var dialog = new FolderBrowserDialog
            {
                Description = "Папка установки WireGuard (в ней лежит wireguard.exe)",
                ShowNewFolderButton = false,
                SelectedPath = txtPath.Text,
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                txtPath.Text = dialog.SelectedPath;
                ApplyPath();
            }
        }

        void ToggleAutostart()
        {
            bool wanted = chkAutostart.Checked;
            chkAutostart.Enabled = false;
            app.SetAutostart(wanted, error =>
            {
                chkAutostart.Enabled = true;
                if (error != null)
                {
                    chkAutostart.SetSilently(!wanted);
                    ShowLocalError("Автозапуск: " + error);
                }
            });
        }

        void UpdateHotkeyButton()
        {
            string modifiers = app.Settings.HotkeyModifiers;
            btnHotkeys.Text = string.IsNullOrEmpty(modifiers) ? "Выключены  ▾" : modifiers + " + цифра  ▾";
        }

        void ShowHotkeyMenu()
        {
            ShowChoiceMenu(btnHotkeys,
                WidgetApp.HotkeyChoices.Select(c => new KeyValuePair<string, string>(c, c.Length == 0 ? "Выключены" : c + " + цифра")),
                app.Settings.HotkeyModifiers,
                value =>
                {
                    app.SetHotkeyModifiers(value);
                    UpdateHotkeyButton();
                });
        }

        void UpdateThemeButton()
        {
            var current = WidgetApp.ColorThemes.FirstOrDefault(t => t.Key == app.Settings.ColorTheme);
            btnTheme.Text = (current.Value ?? WidgetApp.ColorThemes[0].Value) + "  ▾";
        }

        void ShowThemeMenu()
        {
            ShowChoiceMenu(btnTheme, WidgetApp.ColorThemes, app.Settings.ColorTheme, value => app.SetColorTheme(value));
        }

        /// <summary>Выпадающее меню выбора одного значения из списка (значение → подпись).</summary>
        void ShowChoiceMenu(Control anchor, IEnumerable<KeyValuePair<string, string>> choices, string current, Action<string> pick)
        {
            choiceMenu.Items.Clear();
            foreach (var choice in choices)
            {
                string value = choice.Key;
                choiceMenu.Items.Add(new ToolStripMenuItem(choice.Value, null, (s, e) => pick(value))
                {
                    Checked = string.Equals(value, current, StringComparison.OrdinalIgnoreCase),
                });
            }
            choiceMenu.Show(anchor, new Point(0, anchor.Height + Theme.S(2)));
        }

        #endregion

        #region Добавление конфигураций

        void ChooseConfigFile()
        {
            using (var dialog = new OpenFileDialog
            {
                Title = "Конфигурация WireGuard",
                Filter = "Конфигурации WireGuard (*.conf)|*.conf|Все файлы (*.*)|*.*",
                Multiselect = true,
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    AddConfigFiles(dialog.FileNames);
            }
        }

        void OnFilesDropped(string[] files)
        {
            var configs = files.Where(f => f.EndsWith(".conf", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (configs.Length == 0)
            {
                ShowLocalError("Перетащите файл конфигурации WireGuard (.conf)");
                return;
            }
            // модальный диалог — после завершения перетаскивания, чтобы не держать Проводник
            BeginInvoke(new Action(() =>
            {
                ShowAndActivate();
                AddConfigFiles(configs);
            }));
        }

        void AddConfigFiles(IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                string content;
                string error = ConfigImport.ReadConfig(path, out content);
                if (error != null)
                {
                    ShowLocalError(System.IO.Path.GetFileName(path) + ": " + error);
                    continue;
                }
                using (var dialog = new AddConfigDialog(path, ConfigImport.SuggestName(path), app.ValidateNewTunnelName))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                        continue;
                    error = app.AddConfig(dialog.TunnelName, content);
                    if (error != null)
                    {
                        ShowLocalError("Не удалось добавить «" + dialog.TunnelName + "»: " + error);
                        continue;
                    }
                    ShowLocalMessage("Добавлено: " + dialog.TunnelName, () => Theme.Success);
                    if (dialog.ConnectAfterAdding)
                        app.Manager.Connect(dialog.TunnelName);
                }
            }
        }

        #endregion

        #region Строки списка

        void OpenEmojiPicker(string name, Rectangle anchor)
        {
            var pref = app.FindTunnel(name);
            var picker = new EmojiPicker(name, pref != null ? pref.Emoji : null);
            picker.Picked += emoji => app.SetEmoji(name, emoji);
            picker.ShowNear(anchor, this);
        }

        void ShowRowMenu(string name, Point screenPoint)
        {
            int index = app.Tunnels.ToList().FindIndex(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return;
            var state = app.Manager.GetState(name);
            bool up = state == TunnelState.Active || state == TunnelState.Connecting;
            rowMenu.Items.Clear();
            rowMenu.Items.Add(new ToolStripMenuItem(up ? "Отключить" : "Подключить", null, (s, e) => app.Manager.Toggle(name))
            {
                Enabled = state != TunnelState.Disconnecting,
            });
            rowMenu.Items.Add(new ToolStripMenuItem("Сменить иконку…", null,
                (s, e) => OpenEmojiPicker(name, new Rectangle(screenPoint, new Size(1, 1)))));
            rowMenu.Items.Add(new ToolStripSeparator());
            rowMenu.Items.Add(new ToolStripMenuItem("Переместить вверх", null, (s, e) => app.MoveTunnel(index, index - 1))
            {
                Enabled = index > 0,
            });
            rowMenu.Items.Add(new ToolStripMenuItem("Переместить вниз", null, (s, e) => app.MoveTunnel(index, index + 1))
            {
                Enabled = index < app.Tunnels.Count - 1,
            });
            rowMenu.Show(screenPoint);
        }

        #endregion

        #region События окна

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            footerTimer.Enabled = Visible;
            if (Visible)
                RefreshState();
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            if (Visible && WindowState == FormWindowState.Normal)
            {
                app.Settings.WindowX = Left;
                app.Settings.WindowY = Top;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                app.HideMainWindow(true);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !app.IsExiting)
            {
                // крестик прячет окно в трей; выход — через меню значка
                e.Cancel = true;
                app.HideMainWindow(true);
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                footerTimer.Dispose();
                tip.Dispose();
                rowMenu.Dispose();
                choiceMenu.Dispose();
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}
