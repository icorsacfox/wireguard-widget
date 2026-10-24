using System.Collections.Generic;

namespace WireGuardWidget.WireGuard
{
    /// <summary>Состояние службы Windows; значения совпадают с SERVICE_* из Win32.</summary>
    internal enum ServiceState
    {
        NotInstalled = 0,
        Stopped = 1,
        StartPending = 2,
        StopPending = 3,
        Running = 4,
        ContinuePending = 5,
        PausePending = 6,
        Paused = 7,
    }

    internal struct ServiceStatus
    {
        public ServiceState State;
        public int Win32ExitCode;
        public int ServiceSpecificExitCode;
    }

    /// <summary>Всё, что утилита делает с WireGuard. Выделено в интерфейс, чтобы UI можно было проверять без WireGuard.</summary>
    internal interface ITunnelBackend
    {
        string InstallDir { get; }
        bool IsInstalled { get; }

        /// <summary>Имена конфигураций из Data\Configurations. Бросает исключения доступа/отсутствия папки.</summary>
        IList<string> ListConfigs();

        bool ConfigExists(string tunnelName);

        /// <summary>Сохраняет конфигурацию в Data\Configurations (бросает исключение при ошибке).</summary>
        void AddConfig(string tunnelName, string content);

        /// <summary>Все службы туннелей WireGuard: имя туннеля → состояние.</summary>
        Dictionary<string, ServiceState> QueryTunnelServices();

        ServiceStatus GetStatus(string tunnelName);

        CommandResult Install(string tunnelName);
        CommandResult Uninstall(string tunnelName);

        /// <summary>Время последних handshake (unix-время, 0 — не было) по пирам; null — проверить невозможно.</summary>
        long[] GetLatestHandshakes(string tunnelName);

        /// <summary>AllowedIPs всех пиров; null — получить невозможно.</summary>
        string[] GetAllowedIps(string tunnelName);
    }
}
