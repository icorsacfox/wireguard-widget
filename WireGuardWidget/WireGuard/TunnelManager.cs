using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace WireGuardWidget.WireGuard
{
    internal enum TunnelState { Inactive, Active, Connecting, Disconnecting }

    internal enum StatusKind { Progress, Success, Error }

    /// <summary>
    /// Состояние туннелей и операции подключения/отключения.
    /// Публичные члены вызываются из UI-потока; операции выполняются по одной в фоне,
    /// а результаты возвращаются в UI-поток через SynchronizationContext.
    /// </summary>
    internal sealed class TunnelManager : IDisposable
    {
        const int MaxAttempts = 3;
        const int PollIntervalMs = 2000;
        static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

        enum OpKind { Connect, Disconnect, DisconnectAll }

        sealed class Operation
        {
            public OpKind Kind;
            public string Name;
            public readonly CancellationTokenSource Cts = new CancellationTokenSource();
        }

        readonly SynchronizationContext ui;
        readonly object sync = new object();
        readonly List<Operation> ops = new List<Operation>();
        readonly SemaphoreSlim opLock = new SemaphoreSlim(1, 1);
        readonly Timer timer;
        volatile ITunnelBackend backend;
        volatile bool singleConnection = true;
        volatile bool verifyHandshake = true;
        volatile bool disposed;
        int pollBusy, pollRequested;

        // Снимок состояния — читается и меняется только в UI-потоке.
        List<string> configs = new List<string>();
        Dictionary<string, ServiceState> services = new Dictionary<string, ServiceState>(NameComparer);
        string configError;

        public TunnelManager(ITunnelBackend backend, SynchronizationContext ui)
        {
            this.backend = backend;
            this.ui = ui;
            timer = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public event EventHandler Changed;

        /// <summary>Уведомление для трея: заголовок, текст, это ошибка.</summary>
        public event Action<string, string, bool> Notification;

        public bool SingleConnection { get { return singleConnection; } set { singleConnection = value; } }
        public bool VerifyHandshake { get { return verifyHandshake; } set { verifyHandshake = value; } }

        public IReadOnlyList<string> Configs { get { return configs; } }
        public string ConfigError { get { return configError; } }

        public string StatusText { get; private set; }
        public StatusKind StatusKind { get; private set; }
        public DateTime StatusTime { get; private set; }

        /// <summary>Туннели, служба которых запущена или запускается (в т.ч. без конфигурации в папке).</summary>
        public IEnumerable<string> RunningTunnels
        {
            get
            {
                return services
                    .Where(kv => kv.Value == ServiceState.Running || kv.Value == ServiceState.StartPending)
                    .Select(kv => kv.Key)
                    .ToList();
            }
        }

        public bool IsBusy
        {
            get
            {
                lock (sync)
                {
                    if (ops.Count > 0)
                        return true;
                }
                return services.Values.Any(s => s == ServiceState.StartPending || s == ServiceState.StopPending);
            }
        }

        public void Start()
        {
            timer.Change(0, PollIntervalMs);
        }

        public void SetBackend(ITunnelBackend newBackend)
        {
            backend = newBackend;
            PollNow();
        }

        public TunnelState GetState(string name)
        {
            ServiceState service = ServiceOf(name);
            lock (sync)
            {
                for (int i = ops.Count - 1; i >= 0; i--)
                {
                    var op = ops[i];
                    if (op.Cts.IsCancellationRequested)
                        continue;
                    if (op.Kind == OpKind.DisconnectAll)
                    {
                        if (IsUp(service))
                            return TunnelState.Disconnecting;
                        continue;
                    }
                    if (NameComparer.Equals(op.Name, name))
                        return op.Kind == OpKind.Connect ? TunnelState.Connecting : TunnelState.Disconnecting;
                }
            }
            switch (service)
            {
                case ServiceState.Running:
                    return TunnelState.Active;
                case ServiceState.StartPending:
                case ServiceState.ContinuePending:
                    return TunnelState.Connecting;
                case ServiceState.StopPending:
                    return TunnelState.Disconnecting;
                default:
                    return TunnelState.Inactive;
            }
        }

        public void Connect(string name) { Enqueue(OpKind.Connect, name); }
        public void Disconnect(string name) { Enqueue(OpKind.Disconnect, name); }
        public void DisconnectAll() { Enqueue(OpKind.DisconnectAll, null); }

        public void Toggle(string name)
        {
            switch (GetState(name))
            {
                case TunnelState.Disconnecting:
                    return;
                case TunnelState.Active:
                case TunnelState.Connecting: // для подключающегося туннеля — это «отмена»
                    Disconnect(name);
                    return;
                default:
                    Connect(name);
                    return;
            }
        }

        ServiceState ServiceOf(string name)
        {
            ServiceState state;
            return name != null && services.TryGetValue(name, out state) ? state : ServiceState.NotInstalled;
        }

        static bool IsUp(ServiceState state)
        {
            return state != ServiceState.NotInstalled && state != ServiceState.Stopped;
        }

        #region Операции

        void Enqueue(OpKind kind, string name)
        {
            if (disposed)
                return;
            var op = new Operation { Kind = kind, Name = name };
            var superseded = new List<Operation>();
            lock (sync)
            {
                superseded.AddRange(ops.Where(other => Supersedes(op, other)));
                ops.Add(op);
            }
            foreach (var other in superseded)
            {
                try { other.Cts.Cancel(); } catch (ObjectDisposedException) { }
            }
            RaiseChanged();
            Task.Run(() => Execute(op));
        }

        /// <summary>Новая операция отменяет старую, если та стала бессмысленной.</summary>
        bool Supersedes(Operation next, Operation old)
        {
            if (next.Kind == OpKind.DisconnectAll)
                return true;
            if (NameComparer.Equals(next.Name, old.Name))
                return true;
            // в режиме «одно подключение» последнее нажатие побеждает
            return singleConnection && next.Kind == OpKind.Connect && old.Kind == OpKind.Connect;
        }

        async Task Execute(Operation op)
        {
            var b = backend;
            bool entered = false;
            try
            {
                await opLock.WaitAsync().ConfigureAwait(false);
                entered = true;
                var ct = op.Cts.Token;
                ct.ThrowIfCancellationRequested();
                switch (op.Kind)
                {
                    case OpKind.Connect:
                        await ConnectCore(b, op.Name, ct).ConfigureAwait(false);
                        break;
                    case OpKind.Disconnect:
                        await DisconnectCore(b, new List<string> { op.Name }, ct).ConfigureAwait(false);
                        break;
                    case OpKind.DisconnectAll:
                        await DisconnectCore(b, null, ct).ConfigureAwait(false);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log.Error("Операция " + op.Kind + " " + op.Name, ex);
                Report(StatusKind.Error, ex.Message, "WireGuard Widget: ошибка");
            }
            finally
            {
                if (entered)
                    opLock.Release();
            }

            // Свежий снимок служб публикуем вместе с завершением операции, чтобы состояние не «мигало».
            var snapshot = SafeQuery(b);
            Post(() =>
            {
                lock (sync)
                    ops.Remove(op);
                op.Cts.Dispose();
                if (snapshot != null && b == backend)
                    services = snapshot;
                RaiseChanged();
            });
        }

        async Task ConnectCore(ITunnelBackend b, string name, CancellationToken ct)
        {
            if (singleConnection)
            {
                var others = (SafeQuery(b) ?? new Dictionary<string, ServiceState>())
                    .Where(kv => IsUp(kv.Value) && !NameComparer.Equals(kv.Key, name))
                    .Select(kv => kv.Key)
                    .ToList();
                if (others.Count > 0)
                {
                    Report(StatusKind.Progress, "Отключение: " + string.Join(", ", others) + "…");
                    foreach (string other in others)
                    {
                        ct.ThrowIfCancellationRequested();
                        b.Uninstall(other);
                    }
                    PollNow();
                    await WaitUntil(() => others.All(o => !IsUp(b.GetStatus(o).State)), 15000, ct).ConfigureAwait(false);
                }
            }

            string lastError = null;
            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                Report(StatusKind.Progress, attempt == 1
                    ? "Подключение: " + name + "…"
                    : "Подключение: " + name + " (попытка " + attempt + " из " + MaxAttempts + ")…");

                if (attempt > 1)
                {
                    // начисто: удаляем службу, ждём, ставим заново
                    b.Uninstall(name);
                    PollNow();
                    await WaitUntil(() => !IsUp(b.GetStatus(name).State), 10000, ct).ConfigureAwait(false);
                    await Sleep(1000, ct).ConfigureAwait(false);
                }

                var state = b.GetStatus(name).State;
                if (state != ServiceState.Running && state != ServiceState.StartPending)
                {
                    var install = b.Install(name);
                    PollNow();
                    if (!install.Success)
                    {
                        state = b.GetStatus(name).State;
                        if (state != ServiceState.Running && state != ServiceState.StartPending)
                        {
                            lastError = install.Message;
                            await Sleep(1000, ct).ConfigureAwait(false);
                            continue;
                        }
                    }
                }

                string runError = await WaitForRunning(b, name, 20000, ct).ConfigureAwait(false);
                if (runError != null)
                {
                    lastError = runError;
                    continue;
                }

                // служба может упасть сразу после старта (ошибка конфигурации, DNS и т.п.)
                await Sleep(1500, ct).ConfigureAwait(false);
                var settled = b.GetStatus(name);
                if (settled.State != ServiceState.Running)
                {
                    lastError = "служба туннеля остановилась сразу после запуска" + ExitCodeText(settled);
                    continue;
                }

                if (verifyHandshake)
                {
                    Report(StatusKind.Progress, "Проверка соединения: " + name + "…");
                    bool? handshake = await WaitForHandshake(b, name, 15000, ct).ConfigureAwait(false);
                    if (handshake == false)
                    {
                        lastError = "сервер не ответил (нет handshake)";
                        continue;
                    }
                }

                Report(StatusKind.Success, "Подключено: " + name);
                return;
            }

            Report(StatusKind.Error,
                "Не удалось подключиться к «" + name + "» за " + MaxAttempts + " попытки: " + lastError,
                "WireGuard: ошибка подключения");
        }

        async Task DisconnectCore(ITunnelBackend b, List<string> names, CancellationToken ct)
        {
            var targets = names ?? (SafeQuery(b) ?? new Dictionary<string, ServiceState>())
                .Where(kv => IsUp(kv.Value))
                .Select(kv => kv.Key)
                .ToList();
            if (targets.Count == 0)
                return;

            string label = string.Join(", ", targets);
            Report(StatusKind.Progress, "Отключение: " + label + "…");
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                foreach (string name in targets)
                {
                    if (attempt == 1 || IsUp(b.GetStatus(name).State))
                        b.Uninstall(name);
                }
                PollNow();
                if (await WaitUntil(() => targets.All(n => !IsUp(b.GetStatus(n).State)), 10000, ct).ConfigureAwait(false))
                {
                    Report(StatusKind.Success, "Отключено: " + label);
                    return;
                }
            }
            Report(StatusKind.Error, "Не удалось отключить: " + label, "WireGuard: ошибка отключения");
        }

        static async Task<string> WaitForRunning(ITunnelBackend b, string name, int timeoutMs, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            while (true)
            {
                var status = b.GetStatus(name);
                if (status.State == ServiceState.Running)
                    return null;
                bool down = status.State == ServiceState.Stopped || status.State == ServiceState.NotInstalled;
                if (down && sw.ElapsedMilliseconds > 1500)
                    return "служба туннеля не запустилась" + ExitCodeText(status);
                if (sw.ElapsedMilliseconds > timeoutMs)
                    return "служба туннеля не запустилась за " + timeoutMs / 1000 + " с";
                await Sleep(300, ct).ConfigureAwait(false);
            }
        }

        /// <summary>true — handshake был, false — не было за отведённое время, null — проверить нельзя.</summary>
        static async Task<bool?> WaitForHandshake(ITunnelBackend b, string name, int timeoutMs, CancellationToken ct)
        {
            // Handshake начинается только когда в туннель идёт трафик — «пинаем» адрес из AllowedIPs.
            IPAddress probe = PickProbeAddress(b.GetAllowedIps(name));
            var sw = Stopwatch.StartNew();
            long lastProbe = long.MinValue / 2;
            while (true)
            {
                long[] handshakes = b.GetLatestHandshakes(name);
                if (handshakes == null)
                    return null;
                if (handshakes.Any(t => t > 0))
                    return true;
                if (sw.ElapsedMilliseconds > timeoutMs)
                    return false;
                if (probe != null && sw.ElapsedMilliseconds - lastProbe >= 2000)
                {
                    lastProbe = sw.ElapsedMilliseconds;
                    SendProbe(probe);
                }
                await Sleep(500, ct).ConfigureAwait(false);
            }
        }

        static void SendProbe(IPAddress address)
        {
            try
            {
                var ping = new Ping();
                ping.SendPingAsync(address, 1000).ContinueWith(_ => ping.Dispose());
            }
            catch
            {
                // проба — лишь способ вызвать handshake, её результат не важен
            }
        }

        internal static IPAddress PickProbeAddress(IEnumerable<string> allowedIps)
        {
            if (allowedIps == null)
                return null;
            IPAddress v6 = null;
            foreach (string cidr in allowedIps)
            {
                string[] parts = cidr.Trim().Split('/');
                IPAddress address;
                if (!IPAddress.TryParse(parts[0], out address))
                    continue;
                bool isV4 = address.AddressFamily == AddressFamily.InterNetwork;
                int prefix;
                if (parts.Length < 2 || !int.TryParse(parts[1], out prefix))
                    prefix = isV4 ? 32 : 128;

                if (isV4)
                {
                    uint network = ToUInt32(address);
                    uint mask = prefix <= 0 ? 0u : uint.MaxValue << (32 - Math.Min(prefix, 32));
                    foreach (string wellKnown in new[] { "1.1.1.1", "8.8.8.8" })
                    {
                        var candidate = IPAddress.Parse(wellKnown);
                        if ((ToUInt32(candidate) & mask) == (network & mask))
                            return candidate;
                    }
                    if (prefix >= 31)
                        return address;
                    return FromUInt32((network & mask) + 1);
                }
                if (v6 == null && address.AddressFamily == AddressFamily.InterNetworkV6)
                    v6 = prefix == 0 ? IPAddress.Parse("2606:4700:4700::1111") : address;
            }
            return v6;
        }

        static uint ToUInt32(IPAddress address)
        {
            byte[] b = address.GetAddressBytes();
            return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        }

        static IPAddress FromUInt32(uint value)
        {
            return new IPAddress(new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value });
        }

        static string ExitCodeText(ServiceStatus status)
        {
            const int ErrorServiceSpecific = 1066;
            if (status.Win32ExitCode == ErrorServiceSpecific && status.ServiceSpecificExitCode != 0)
                return " (код ошибки WireGuard: " + status.ServiceSpecificExitCode + ")";
            if (status.Win32ExitCode != 0)
                return " (код ошибки: " + status.Win32ExitCode + ")";
            return "";
        }

        static async Task<bool> WaitUntil(Func<bool> condition, int timeoutMs, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            while (true)
            {
                if (condition())
                    return true;
                if (sw.ElapsedMilliseconds >= timeoutMs)
                    return false;
                await Sleep(300, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Пауза с проверкой отмены. Токен не передаётся в Task.Delay намеренно: иначе отмена из UI-потока
        /// продолжила бы операцию синхронно прямо в UI-потоке.
        /// </summary>
        static async Task Sleep(int ms, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                long left = ms - sw.ElapsedMilliseconds;
                if (left <= 0)
                    return;
                await Task.Delay((int)Math.Min(left, 150)).ConfigureAwait(false);
            }
        }

        #endregion

        #region Опрос состояния

        public void PollNow()
        {
            if (!disposed)
                ThreadPool.QueueUserWorkItem(_ => Poll());
        }

        void Poll()
        {
            Interlocked.Exchange(ref pollRequested, 1);
            if (Interlocked.CompareExchange(ref pollBusy, 1, 0) != 0)
                return;
            try
            {
                while (!disposed && Interlocked.Exchange(ref pollRequested, 0) == 1)
                {
                    var b = backend;
                    var snapshot = SafeQuery(b) ?? new Dictionary<string, ServiceState>(NameComparer);
                    List<string> names;
                    string error;
                    ReadConfigs(b, out names, out error);
                    Post(() => Apply(b, snapshot, names, error));
                }
            }
            finally
            {
                Interlocked.Exchange(ref pollBusy, 0);
            }
        }

        static Dictionary<string, ServiceState> SafeQuery(ITunnelBackend b)
        {
            try
            {
                return b.QueryTunnelServices();
            }
            catch (Exception ex)
            {
                Log.Error("Не удалось получить список служб", ex);
                return null;
            }
        }

        static void ReadConfigs(ITunnelBackend b, out List<string> names, out string error)
        {
            names = new List<string>();
            error = null;
            if (!b.IsInstalled)
            {
                error = "WireGuard не найден. Укажите папку установки в настройках (⚙).";
                return;
            }
            try
            {
                names = b.ListConfigs().ToList();
                if (names.Count == 0)
                    error = "В папке Data\\Configurations нет конфигураций. Добавьте туннель в WireGuard.";
            }
            catch (UnauthorizedAccessException)
            {
                error = "Нет доступа к Data\\Configurations. Запустите утилиту от имени администратора.";
            }
            catch (System.IO.DirectoryNotFoundException)
            {
                error = "Папка Data\\Configurations не найдена. Добавьте туннель в WireGuard.";
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
        }

        void Apply(ITunnelBackend b, Dictionary<string, ServiceState> snapshot, List<string> names, string error)
        {
            if (disposed || b != backend)
                return;
            bool changed = !SameServices(services, snapshot)
                || !configs.SequenceEqual(names, StringComparer.Ordinal)
                || configError != error;
            services = snapshot;
            configs = names;
            configError = error;
            if (changed)
                RaiseChanged();
        }

        static bool SameServices(Dictionary<string, ServiceState> a, Dictionary<string, ServiceState> b)
        {
            if (a.Count != b.Count)
                return false;
            foreach (var kv in a)
            {
                ServiceState other;
                if (!b.TryGetValue(kv.Key, out other) || other != kv.Value)
                    return false;
            }
            return true;
        }

        #endregion

        void Report(StatusKind kind, string text, string notifyTitle = null)
        {
            Post(() =>
            {
                StatusText = text;
                StatusKind = kind;
                StatusTime = DateTime.Now;
                RaiseChanged();
                if (notifyTitle != null && Notification != null)
                    Notification(notifyTitle, text, kind == StatusKind.Error);
            });
            if (kind == StatusKind.Error)
                Log.Info(text);
        }

        void Post(Action action)
        {
            if (!disposed)
                ui.Post(_ => { if (!disposed) action(); }, null);
        }

        void RaiseChanged()
        {
            var handler = Changed;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            timer.Dispose();
            lock (sync)
            {
                foreach (var op in ops)
                {
                    try { op.Cts.Cancel(); } catch (ObjectDisposedException) { }
                }
            }
        }
    }
}
