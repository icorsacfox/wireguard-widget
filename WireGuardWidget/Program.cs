using System;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using WireGuardWidget.WireGuard;

namespace WireGuardWidget
{
    internal static class Program
    {
        const string MutexName = @"Local\WireGuardWidget.Instance";
        const string ShowEventName = @"Local\WireGuardWidget.Show";

        [STAThread]
        static void Main()
        {
            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // уже запущено — просим тот экземпляр показать окно
                    try
                    {
                        using (var show = EventWaitHandle.OpenExisting(ShowEventName))
                            show.Set();
                    }
                    catch
                    {
                    }
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => Log.Error("Необработанное исключение", e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Error("Фатальная ошибка", e.ExceptionObject as Exception);
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

                if (!IsAdministrator())
                {
                    MessageBox.Show(
                        "Утилите нужны права администратора: без них нельзя прочитать конфигурации WireGuard и управлять туннелями.",
                        "WireGuard Widget", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WireGuardWidget.json");
                var settings = AppSettings.Load(settingsPath);

                using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                using (var app = new WidgetApp(settings, settingsPath, dir => new WireGuardBackend(dir)))
                {
                    var registration = ThreadPool.RegisterWaitForSingleObject(showEvent,
                        (state, timedOut) => app.RequestShowFromOtherInstance(), null, Timeout.Infinite, false);
                    Application.Run(app);
                    registration.Unregister(null);
                }
                mutex.ReleaseMutex();
            }
        }

        static bool IsAdministrator()
        {
            using (var identity = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
