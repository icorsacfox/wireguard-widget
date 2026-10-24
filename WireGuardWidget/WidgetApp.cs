using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WireGuardWidget.Emoji;
using WireGuardWidget.UI;
using WireGuardWidget.WireGuard;

namespace WireGuardWidget
{
    /// <summary>
    /// Корень приложения: настройки, менеджер туннелей, значок в трее, хоткеи, главное окно и оверлей.
    /// Главное окно и оверлей — альтернативные представления: одновременно виден только один из них.
    /// </summary>
    internal sealed class WidgetApp : ApplicationContext
    {
        public static readonly string[] HotkeyChoices = { "Ctrl", "Ctrl+Alt", "Ctrl+Shift", "Alt+Shift", "" };
        public static readonly IReadOnlyList<KeyValuePair<string, string>> ColorThemes = new[]
        {
            new KeyValuePair<string, string>("Dark", "Тёмная"),
            new KeyValuePair<string, string>("Light", "Светлая"),
            new KeyValuePair<string, string>("System", "Как в Windows"),
        };
        static readonly string[] DefaultEmojis = { "🟢", "🔵", "🟣", "🟠", "🟡", "🔴", "🟤", "⚪" };
        static readonly int[] OverlaySizes = { 36, 52, 72 };
        static readonly int[] OverlayOpacities = { 40, 60, 85, 100 };
        static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

        readonly string settingsPath;
        readonly Func<string, ITunnelBackend> backendFactory;
        readonly SynchronizationContext ui;
        readonly MainForm mainForm;
        readonly OverlayForm overlay;
        readonly NotifyIcon tray;
        readonly ContextMenuStrip trayMenu, overlayMenu;
        readonly HotkeyManager hotkeys;
        readonly bool headless;
        List<TunnelPref> tunnels = new List<TunnelPref>();
        OwnedIcon trayIcon, windowIcon;
        string iconKey;

        /// <param name="headless">Без трея и хоткеев (для предпросмотра интерфейса).</param>
        public WidgetApp(AppSettings settings, string settingsPath, Func<string, ITunnelBackend> backendFactory, bool headless = false)
        {
            Settings = settings;
            this.settingsPath = settingsPath;
            this.backendFactory = backendFactory;
            this.headless = headless;
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            ToolStripManager.Renderer = new DarkMenuRenderer();
            // палитру выбираем до создания окон — они берут цвета в конструкторах
            Theme.Apply(ResolveDarkTheme());
            if (!headless)
                Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            if (!WireGuardBackend.IsValidInstallDir(settings.WireGuardPath))
            {
                string detected = WireGuardBackend.DetectInstallDir();
                if (detected != null)
                    settings.WireGuardPath = detected;
            }

            Manager = new TunnelManager(backendFactory(settings.WireGuardPath), ui)
            {
                SingleConnection = settings.SingleConnection,
                VerifyHandshake = settings.VerifyHandshake,
            };
            Manager.Changed += (s, e) => OnStateChanged();
            Manager.Notification += OnNotification;

            trayMenu = CreateMenu(false);
            overlayMenu = CreateMenu(true);
            mainForm = new MainForm(this);
            overlay = new OverlayForm(this);

            if (!headless)
            {
                tray = new NotifyIcon { ContextMenuStrip = trayMenu, Text = "WireGuard Widget" };
                tray.MouseClick += (s, e) =>
                {
                    if (e.Button == MouseButtons.Left)
                        ToggleMainWindow();
                };
                hotkeys = new HotkeyManager();
                hotkeys.DigitPressed += OnHotkey;
                ApplyHotkeys();
            }

            OnStateChanged();
            if (tray != null)
                tray.Visible = true;
            Manager.Start();

            if (!headless)
            {
                if (settings.WindowVisible)
                    ShowMainWindow();
                else if (settings.OverlayEnabled)
                    overlay.ShowOverlay();

                if (settings.RunAtStartup)
                {
                    // обновляем путь к exe в задании (на случай, если папку утилиты перенесли)
                    Task.Run(() => Autostart.Apply(true));
                }
            }
        }

        public AppSettings Settings { get; private set; }
        public TunnelManager Manager { get; private set; }
        public bool IsExiting { get; private set; }
        public IReadOnlyList<int> FailedHotkeys { get; private set; } = new int[0];

        /// <summary>Конфигурации из папки WireGuard в пользовательском порядке.</summary>
        public IReadOnlyList<TunnelPref> Tunnels { get { return tunnels; } }

        /// <summary>"Ctrl+" и т.п.; null — хоткеи выключены.</summary>
        public string HotkeyPrefix
        {
            get { return string.IsNullOrEmpty(Settings.HotkeyModifiers) ? null : Settings.HotkeyModifiers + "+"; }
        }

        internal MainForm MainWindow { get { return mainForm; } }
        internal OverlayForm Overlay { get { return overlay; } }

        public TunnelPref FindTunnel(string name)
        {
            return tunnels.FirstOrDefault(t => NameComparer.Equals(t.Name, name));
        }

        #region Состояние

        void OnStateChanged()
        {
            RebuildTunnelList();

            var activeEmojis = new List<string>();
            var activeNames = new List<string>();
            foreach (var tunnel in tunnels)
            {
                if (Manager.GetState(tunnel.Name) == TunnelState.Active)
                {
                    activeEmojis.Add(tunnel.Emoji);
                    activeNames.Add(tunnel.Name);
                }
            }
            // туннели, запущенные не из папки конфигураций
            foreach (string name in Manager.RunningTunnels)
            {
                if (FindTunnel(name) == null && !activeNames.Contains(name, NameComparer))
                {
                    activeEmojis.Add("🌐");
                    activeNames.Add(name);
                }
            }

            bool busy = Manager.IsBusy;
            string tooltip = busy ? "WireGuard: подключение…"
                : activeNames.Count == 0 ? "WireGuard: нет подключения"
                : "WireGuard: " + string.Join(", ", activeNames);

            UpdateIcons(activeEmojis, busy, tooltip);
            overlay.SetContent(activeEmojis, busy, tooltip);
            mainForm.RefreshState();
        }

        /// <summary>Сливает найденные конфигурации с сохранённым порядком; новые получают иконку по умолчанию.</summary>
        void RebuildTunnelList()
        {
            bool added = false;
            foreach (string name in Manager.Configs)
            {
                if (Settings.Tunnels.Any(t => NameComparer.Equals(t.Name, name)))
                    continue;
                Settings.Tunnels.Add(new TunnelPref { Name = name, Emoji = DefaultEmojis[Settings.Tunnels.Count % DefaultEmojis.Length] });
                added = true;
            }
            var present = new HashSet<string>(Manager.Configs, NameComparer);
            tunnels = Settings.Tunnels.Where(t => present.Contains(t.Name)).ToList();
            if (added)
                SaveSettings();
        }

        void UpdateIcons(List<string> emojis, bool busy, string tooltip)
        {
            if (tray != null)
                tray.Text = tooltip.Length > 63 ? tooltip.Substring(0, 62) + "…" : tooltip;

            string key = busy ? "\u0001busy" : string.Join("\u0001", emojis);
            if (key == iconKey)
                return;
            iconKey = key;

            // многоточие «подключение…» в трее — контрастное к панели задач, в заголовке окна — к теме
            Color trayDots = Theme.TaskbarIsLight() ? Color.FromArgb(40, 40, 40) : Color.White;
            var newTrayIcon = OwnedIcon.FromBitmap(StatusGraphics.ComposeIcon(emojis, busy, SystemInformation.SmallIconSize.Width, trayDots));
            if (tray != null)
                tray.Icon = newTrayIcon.Icon;
            if (trayIcon != null)
                trayIcon.Dispose();
            trayIcon = newTrayIcon;

            var newWindowIcon = OwnedIcon.FromBitmap(StatusGraphics.ComposeIcon(emojis, busy, SystemInformation.IconSize.Width, Theme.Text));
            mainForm.Icon = newWindowIcon.Icon;
            if (windowIcon != null)
                windowIcon.Dispose();
            windowIcon = newWindowIcon;
        }

        void OnNotification(string title, string text, bool isError)
        {
            if (tray != null)
                tray.ShowBalloonTip(isError ? 10000 : 3000, title, text, isError ? ToolTipIcon.Error : ToolTipIcon.Info);
        }

        void OnHotkey(int digit)
        {
            if (digit == 0)
            {
                Manager.DisconnectAll();
                return;
            }
            if (digit > tunnels.Count)
                return;
            // повторное нажатие отключает подключённый конфиг (или отменяет подключение)
            Manager.Toggle(tunnels[digit - 1].Name);
        }

        #endregion

        #region Окна

        public void ShowMainWindow()
        {
            overlay.Hide();
            mainForm.ShowAndActivate();
            Settings.WindowVisible = true;
            Settings.OverlayEnabled = false;
            SaveSettings();
        }

        /// <summary>Скрыть окно — приложение остаётся только в трее (оверлей показывается лишь по явной команде).</summary>
        public void HideMainWindow(bool closedByUser)
        {
            mainForm.Hide();
            Settings.WindowVisible = false;
            SaveSettings();

            if (closedByUser && !Settings.TrayHintShown && tray != null)
            {
                Settings.TrayHintShown = true;
                SaveSettings();
                tray.ShowBalloonTip(4000, "WireGuard Widget", "Утилита продолжает работать в трее. Выход — через меню значка.", ToolTipIcon.Info);
            }
        }

        /// <summary>Кнопка в главном окне / пункт меню трея: переключиться на оверлей.</summary>
        public void SwitchToOverlay()
        {
            mainForm.Hide();
            Settings.WindowVisible = false;
            Settings.OverlayEnabled = true;
            overlay.ShowOverlay();
            SaveSettings();
        }

        void ToggleMainWindow()
        {
            if (mainForm.Visible && mainForm.WindowState != FormWindowState.Minimized)
                HideMainWindow(false);
            else
                ShowMainWindow();
        }

        void SetOverlayEnabled(bool enabled)
        {
            if (enabled)
            {
                SwitchToOverlay();
                return;
            }
            Settings.OverlayEnabled = false;
            overlay.Hide();
            SaveSettings();
        }

        public void SaveOverlayLocation(Point location)
        {
            Settings.OverlayX = location.X;
            Settings.OverlayY = location.Y;
            SaveSettings();
        }

        public void ShowOverlayMenu(Point screenPoint)
        {
            overlayMenu.Show(screenPoint);
        }

        #endregion

        #region Меню

        ContextMenuStrip CreateMenu(bool forOverlay)
        {
            var menu = new ContextMenuStrip
            {
                Font = Theme.UiFont,
                ImageScalingSize = new Size(Theme.S(16), Theme.S(16)),
            };
            menu.Opening += (s, e) =>
            {
                PopulateMenu(menu, forOverlay);
                e.Cancel = false; // меню изначально пустое — без этого WinForms его не покажет
            };
            return menu;
        }

        void PopulateMenu(ContextMenuStrip menu, bool forOverlay)
        {
            menu.Items.Clear();
            menu.Items.Add(new ToolStripMenuItem("Открыть окно", null, (s, e) => ShowMainWindow()) { Font = Theme.UiFontBold });
            menu.Items.Add(new ToolStripSeparator());

            int iconSize = Theme.S(16);
            for (int i = 0; i < tunnels.Count; i++)
            {
                string name = tunnels[i].Name;
                var state = Manager.GetState(name);
                string text = name;
                if (state == TunnelState.Connecting) text += "  (подключение…)";
                if (state == TunnelState.Disconnecting) text += "  (отключение…)";
                var item = new ToolStripMenuItem(text, EmojiRenderer.Shared.Get(tunnels[i].Emoji, iconSize), (s, e) => Manager.Toggle(name))
                {
                    Checked = state == TunnelState.Active,
                    ImageScaling = ToolStripItemImageScaling.None,
                    ShortcutKeyDisplayString = i < 9 && HotkeyPrefix != null ? HotkeyPrefix + (i + 1) : null,
                };
                menu.Items.Add(item);
            }
            if (tunnels.Count == 0)
                menu.Items.Add(new ToolStripMenuItem(Manager.ConfigError ?? "Нет конфигураций") { Enabled = false });

            menu.Items.Add(new ToolStripMenuItem("Отключить все", EmojiRenderer.Shared.Get(StatusGraphics.NoConnectionEmoji, iconSize),
                (s, e) => Manager.DisconnectAll())
            {
                Enabled = Manager.RunningTunnels.Any(),
                ImageScaling = ToolStripItemImageScaling.None,
                ShortcutKeyDisplayString = HotkeyPrefix != null ? HotkeyPrefix + "0" : null,
            });
            menu.Items.Add(new ToolStripSeparator());

            if (forOverlay)
            {
                var size = new ToolStripMenuItem("Размер");
                string[] sizeNames = { "Маленький", "Средний", "Большой" };
                for (int i = 0; i < OverlaySizes.Length; i++)
                {
                    int value = OverlaySizes[i];
                    size.DropDownItems.Add(new ToolStripMenuItem(sizeNames[i], null, (s, e) => SetOverlayAppearance(value, Settings.OverlayOpacity))
                    {
                        Checked = Settings.OverlaySize == value,
                    });
                }
                var opacity = new ToolStripMenuItem("Непрозрачность");
                foreach (int value in OverlayOpacities)
                {
                    int v = value;
                    opacity.DropDownItems.Add(new ToolStripMenuItem(v + " %", null, (s, e) => SetOverlayAppearance(Settings.OverlaySize, v))
                    {
                        Checked = Settings.OverlayOpacity == v,
                    });
                }
                menu.Items.Add(size);
                menu.Items.Add(opacity);
                menu.Items.Add(new ToolStripMenuItem("Скрыть оверлей", null, (s, e) => SetOverlayEnabled(false)));
            }
            else
            {
                menu.Items.Add(new ToolStripMenuItem("Оверлей", null, (s, e) => SetOverlayEnabled(!Settings.OverlayEnabled))
                {
                    Checked = Settings.OverlayEnabled,
                });
            }
            menu.Items.Add(new ToolStripMenuItem("Выход", null, (s, e) => Exit()));
        }

        void SetOverlayAppearance(int size, int opacity)
        {
            Settings.OverlaySize = size;
            Settings.OverlayOpacity = opacity;
            overlay.ApplyAppearance();
            SaveSettings();
        }

        #endregion

        #region Тема

        public void SetColorTheme(string mode)
        {
            Settings.ColorTheme = mode;
            SaveSettings();
            ApplyColorTheme();
        }

        bool ResolveDarkTheme()
        {
            switch (Settings.ColorTheme)
            {
                case "Light": return false;
                case "System": return Theme.SystemPrefersDark();
                default: return true;
            }
        }

        void ApplyColorTheme()
        {
            Theme.Apply(ResolveDarkTheme());
            mainForm.ApplyTheme();
            overlay.ApplyAppearance();
            iconKey = null;
            OnStateChanged();
        }

        /// <summary>Сменилась тема Windows или панели задач.</summary>
        void OnUserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        {
            if (e.Category != Microsoft.Win32.UserPreferenceCategory.General)
                return;
            ui.Post(_ =>
            {
                if (IsExiting)
                    return;
                if (Settings.ColorTheme == "System" && Theme.IsDark != Theme.SystemPrefersDark())
                    ApplyColorTheme();
                iconKey = null; // цвет многоточия в трее зависит от темы панели задач
                OnStateChanged();
            }, null);
        }

        #endregion

        #region Добавление конфигураций

        /// <summary>Текст ошибки для имени нового туннеля или null.</summary>
        public string ValidateNewTunnelName(string name)
        {
            string error = ConfigImport.ValidateName(name);
            if (error != null)
                return error;
            var backend = backendFactory(Settings.WireGuardPath);
            if (!backend.IsInstalled)
                return "WireGuard не найден — укажите папку в настройках.";
            if (Manager.Configs.Contains(name, NameComparer) || backend.ConfigExists(name))
                return "Конфигурация с таким именем уже есть.";
            return null;
        }

        /// <summary>Сохраняет конфигурацию в папку WireGuard. Возвращает текст ошибки или null.</summary>
        public string AddConfig(string name, string content)
        {
            try
            {
                backendFactory(Settings.WireGuardPath).AddConfig(name, content);
            }
            catch (UnauthorizedAccessException)
            {
                return "нет доступа к папке Data\\Configurations (нужны права администратора)";
            }
            catch (Exception ex)
            {
                Log.Error("Не удалось добавить конфигурацию " + name, ex);
                return ex.Message;
            }
            Manager.PollNow();
            return null;
        }

        #endregion

        #region Настройки

        public void SetSingleConnection(bool value)
        {
            Settings.SingleConnection = value;
            Manager.SingleConnection = value;
            SaveSettings();
        }

        public void SetVerifyHandshake(bool value)
        {
            Settings.VerifyHandshake = value;
            Manager.VerifyHandshake = value;
            SaveSettings();
        }

        public void SetWireGuardPath(string path)
        {
            Settings.WireGuardPath = path;
            Manager.SetBackend(backendFactory(path));
            SaveSettings();
        }

        public void SetHotkeyModifiers(string modifiers)
        {
            Settings.HotkeyModifiers = modifiers ?? "";
            ApplyHotkeys();
            SaveSettings();
            mainForm.RefreshState();
        }

        void ApplyHotkeys()
        {
            if (hotkeys != null)
                FailedHotkeys = hotkeys.Register(HotkeyManager.ParseModifiers(Settings.HotkeyModifiers));
        }

        /// <summary>Создаёт/удаляет задание автозапуска в фоне; done(null) — успех, иначе текст ошибки.</summary>
        public void SetAutostart(bool enabled, Action<string> done)
        {
            Task.Run(() => Autostart.Apply(enabled)).ContinueWith(task =>
            {
                string error = task.IsFaulted ? task.Exception.GetBaseException().Message : task.Result;
                if (error == null)
                {
                    Settings.RunAtStartup = enabled;
                    SaveSettings();
                }
                done(error);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        public void SetEmoji(string name, string emoji)
        {
            var pref = Settings.Tunnels.FirstOrDefault(t => NameComparer.Equals(t.Name, name));
            if (pref == null)
                return;
            pref.Emoji = emoji;
            SaveSettings();
            iconKey = null;
            OnStateChanged();
        }

        /// <summary>Перемещает конфигурацию в видимом списке (меняются номера хоткеев).</summary>
        public void MoveTunnel(int from, int to)
        {
            if (from < 0 || from >= tunnels.Count || to < 0 || to >= tunnels.Count || from == to)
                return;
            var visible = tunnels.ToList();
            var item = visible[from];
            visible.RemoveAt(from);
            visible.Insert(to, item);
            // скрытые (удалённые из WireGuard) записи сохраняем в конце, чтобы не терять их иконки
            var hidden = Settings.Tunnels.Where(t => !visible.Contains(t)).ToList();
            Settings.Tunnels = visible.Concat(hidden).ToList();
            SaveSettings();
            OnStateChanged();
        }

        public void SaveSettings()
        {
            if (settingsPath == null)
                return;
            try
            {
                Settings.Save(settingsPath);
            }
            catch (Exception ex)
            {
                Log.Error("Не удалось сохранить настройки", ex);
            }
        }

        #endregion

        /// <summary>Повторный запуск exe: показать окно уже работающего экземпляра.</summary>
        public void RequestShowFromOtherInstance()
        {
            ui.Post(_ => ShowMainWindow(), null);
        }

        public void Exit()
        {
            if (IsExiting)
                return;
            IsExiting = true;
            Settings.WindowVisible = mainForm.Visible;
            SaveSettings();
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            Manager.Dispose();
            if (hotkeys != null)
                hotkeys.Dispose();
            if (tray != null)
            {
                tray.Visible = false;
                tray.Dispose();
            }
            overlay.Close();
            mainForm.Close();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!IsExiting)
                {
                    Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                    Manager.Dispose();
                    if (hotkeys != null)
                        hotkeys.Dispose();
                    if (tray != null)
                        tray.Dispose();
                }
                overlay.Dispose();
                mainForm.Dispose();
                trayMenu.Dispose();
                overlayMenu.Dispose();
                if (trayIcon != null)
                    trayIcon.Dispose();
                if (windowIcon != null)
                    windowIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
