using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using Microsoft.Win32;

namespace WireGuardWidget.WireGuard
{
    /// <summary>
    /// Работа с установленным WireGuard for Windows:
    /// туннель = служба "WireGuardTunnel$имя", которую создаёт/удаляет wireguard.exe
    /// (/installtunnelservice, /uninstalltunnelservice) — так же, как это делает его собственный GUI.
    /// </summary>
    internal sealed class WireGuardBackend : ITunnelBackend
    {
        public const string ServicePrefix = "WireGuardTunnel$";
        const string EncryptedSuffix = ".conf.dpapi";
        const string PlainSuffix = ".conf";

        public WireGuardBackend(string installDir)
        {
            InstallDir = installDir;
        }

        public string InstallDir { get; private set; }
        public bool IsInstalled { get { return IsValidInstallDir(InstallDir); } }

        string WireGuardExe { get { return Path.Combine(InstallDir, "wireguard.exe"); } }
        string WgExe { get { return Path.Combine(InstallDir, "wg.exe"); } }
        string ConfigDir { get { return Path.Combine(InstallDir, "Data", "Configurations"); } }

        public static bool IsValidInstallDir(string dir)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, "wireguard.exe"));
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>Ищет установленный WireGuard: стандартная папка, затем путь из службы WireGuardManager.</summary>
        public static string DetectInstallDir()
        {
            var candidates = new List<string>();
            foreach (var variable in new[] { "ProgramW6432", "ProgramFiles", "ProgramFiles(x86)" })
            {
                string root = Environment.GetEnvironmentVariable(variable);
                if (!string.IsNullOrEmpty(root))
                    candidates.Add(Path.Combine(root, "WireGuard"));
            }
            candidates.Add(DirFromServiceImagePath("WireGuardManager"));
            return candidates.FirstOrDefault(IsValidInstallDir);
        }

        static string DirFromServiceImagePath(string serviceName)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + serviceName))
                {
                    string imagePath = key == null ? null : key.GetValue("ImagePath") as string;
                    if (string.IsNullOrWhiteSpace(imagePath))
                        return null;
                    imagePath = imagePath.Trim();
                    string exe = imagePath.StartsWith("\"")
                        ? imagePath.Substring(1, Math.Max(0, imagePath.IndexOf('"', 1) - 1))
                        : imagePath.Split(' ')[0];
                    return Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(exe));
                }
            }
            catch
            {
                return null;
            }
        }

        public IList<string> ListConfigs()
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.EnumerateFiles(ConfigDir))
            {
                string name = TunnelNameFromFile(Path.GetFileName(file));
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
            }
            return names.ToList();
        }

        static string TunnelNameFromFile(string fileName)
        {
            if (fileName.EndsWith(EncryptedSuffix, StringComparison.OrdinalIgnoreCase))
                return fileName.Substring(0, fileName.Length - EncryptedSuffix.Length);
            if (fileName.EndsWith(PlainSuffix, StringComparison.OrdinalIgnoreCase))
                return fileName.Substring(0, fileName.Length - PlainSuffix.Length);
            return null;
        }

        public bool ConfigExists(string tunnelName)
        {
            return FindConfigFile(tunnelName) != null;
        }

        /// <summary>
        /// Кладёт конфигурацию открытым текстом как "имя.conf": служба WireGuardManager сама зашифрует её
        /// в "имя.conf.dpapi" (шифровать самим нельзя — ключ DPAPI принадлежит учётной записи SYSTEM).
        /// </summary>
        public void AddConfig(string tunnelName, string content)
        {
            Directory.CreateDirectory(ConfigDir);
            string target = Path.Combine(ConfigDir, tunnelName + PlainSuffix);
            // пишем во временный файл с другим расширением, чтобы WireGuard не прочитал его недописанным
            string temp = target + ".tmp";
            File.WriteAllText(temp, content, new System.Text.UTF8Encoding(false));
            try
            {
                File.Move(temp, target);
            }
            catch
            {
                File.Delete(temp);
                throw;
            }
        }

        string FindConfigFile(string tunnelName)
        {
            string encrypted = Path.Combine(ConfigDir, tunnelName + EncryptedSuffix);
            if (File.Exists(encrypted))
                return encrypted;
            string plain = Path.Combine(ConfigDir, tunnelName + PlainSuffix);
            return File.Exists(plain) ? plain : null;
        }

        public Dictionary<string, ServiceState> QueryTunnelServices()
        {
            var result = new Dictionary<string, ServiceState>(StringComparer.OrdinalIgnoreCase);
            ServiceController[] services = ServiceController.GetServices();
            try
            {
                foreach (var service in services)
                {
                    string serviceName = service.ServiceName;
                    if (serviceName.StartsWith(ServicePrefix, StringComparison.OrdinalIgnoreCase))
                        result[serviceName.Substring(ServicePrefix.Length)] = (ServiceState)(int)service.Status;
                }
            }
            finally
            {
                foreach (var service in services)
                    service.Dispose();
            }
            return result;
        }

        public ServiceStatus GetStatus(string tunnelName)
        {
            var status = new ServiceStatus { State = ServiceState.NotInstalled };
            IntPtr scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero)
                return status;
            try
            {
                IntPtr service = OpenService(scm, ServicePrefix + tunnelName, SERVICE_QUERY_STATUS);
                if (service == IntPtr.Zero)
                    return status;
                try
                {
                    SERVICE_STATUS raw;
                    if (QueryServiceStatus(service, out raw))
                    {
                        status.State = (ServiceState)raw.dwCurrentState;
                        status.Win32ExitCode = raw.dwWin32ExitCode;
                        status.ServiceSpecificExitCode = raw.dwServiceSpecificExitCode;
                    }
                }
                finally
                {
                    CloseServiceHandle(service);
                }
            }
            finally
            {
                CloseServiceHandle(scm);
            }
            return status;
        }

        public CommandResult Install(string tunnelName)
        {
            if (!IsInstalled)
                return CommandResult.Fail("wireguard.exe не найден");
            string file = FindConfigFile(tunnelName);
            if (file == null)
                return CommandResult.Fail("файл конфигурации не найден");
            return ProcessRunner.Run(WireGuardExe, "/installtunnelservice \"" + file + "\"", 30000);
        }

        public CommandResult Uninstall(string tunnelName)
        {
            if (!IsInstalled)
                return CommandResult.Fail("wireguard.exe не найден");
            return ProcessRunner.Run(WireGuardExe, "/uninstalltunnelservice \"" + tunnelName + "\"", 30000);
        }

        public long[] GetLatestHandshakes(string tunnelName)
        {
            string[] lines = RunWg("show \"" + tunnelName + "\" latest-handshakes");
            if (lines == null)
                return null;
            var result = new List<long>();
            foreach (string line in lines)
            {
                string[] parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                long value;
                if (parts.Length >= 2 && long.TryParse(parts[1], out value))
                    result.Add(value);
            }
            return result.ToArray();
        }

        public string[] GetAllowedIps(string tunnelName)
        {
            string[] lines = RunWg("show \"" + tunnelName + "\" allowed-ips");
            if (lines == null)
                return null;
            return lines
                .SelectMany(line => line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries).Skip(1))
                .Where(item => item.Contains("/"))
                .ToArray();
        }

        string[] RunWg(string arguments)
        {
            if (!File.Exists(WgExe))
                return null;
            var result = ProcessRunner.Run(WgExe, arguments, 5000);
            if (!result.Success)
                return null;
            return result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        #region Win32

        const uint SC_MANAGER_CONNECT = 0x0001;
        const uint SERVICE_QUERY_STATUS = 0x0004;

        [StructLayout(LayoutKind.Sequential)]
        struct SERVICE_STATUS
        {
            public int dwServiceType;
            public int dwCurrentState;
            public int dwControlsAccepted;
            public int dwWin32ExitCode;
            public int dwServiceSpecificExitCode;
            public int dwCheckPoint;
            public int dwWaitHint;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr OpenSCManager(string machineName, string databaseName, uint desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr OpenService(IntPtr scManager, string serviceName, uint desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool QueryServiceStatus(IntPtr service, out SERVICE_STATUS status);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool CloseServiceHandle(IntPtr handle);

        #endregion
    }
}
