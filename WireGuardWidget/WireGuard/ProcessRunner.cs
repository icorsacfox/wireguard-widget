using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace WireGuardWidget.WireGuard
{
    internal struct CommandResult
    {
        public int ExitCode;
        public string Output;
        public string Error;

        public bool Success { get { return ExitCode == 0; } }

        /// <summary>Короткое человекочитаемое сообщение об ошибке.</summary>
        public string Message
        {
            get
            {
                string text = !string.IsNullOrWhiteSpace(Error) ? Error : Output;
                if (string.IsNullOrWhiteSpace(text))
                    return "код выхода " + ExitCode;
                // Go-шный log.Fatal добавляет префикс "2026/10/18 12:00:00 "
                text = GoLogPrefix.Replace(text.Trim(), "");
                return text.Length > 300 ? text.Substring(0, 300) + "…" : text;
            }
        }

        static readonly Regex GoLogPrefix = new Regex(@"(?m)^\d{4}/\d{2}/\d{2} \d{2}:\d{2}:\d{2} ");

        public static CommandResult Fail(string message)
        {
            return new CommandResult { ExitCode = -1, Output = "", Error = message };
        }
    }

    internal static class ProcessRunner
    {
        public static CommandResult Run(string exe, string arguments, int timeoutMs, Encoding encoding = null)
        {
            var psi = new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                // wireguard.exe показывает MessageBox при ошибке, если stderr не перенаправлен
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = encoding ?? Encoding.UTF8,
                StandardErrorEncoding = encoding ?? Encoding.UTF8,
            };
            try
            {
                using (var process = Process.Start(psi))
                {
                    var stdout = process.StandardOutput.ReadToEndAsync();
                    var stderr = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(timeoutMs))
                    {
                        try { process.Kill(); } catch { }
                        return CommandResult.Fail("команда не завершилась за " + timeoutMs / 1000 + " с");
                    }
                    process.WaitForExit();
                    return new CommandResult { ExitCode = process.ExitCode, Output = stdout.Result, Error = stderr.Result };
                }
            }
            catch (Exception ex)
            {
                return CommandResult.Fail(ex.Message);
            }
        }
    }
}
