using System;
using System.IO;
using System.Text;

namespace WireGuardWidget
{
    /// <summary>Минимальный журнал ошибок рядом с exe (на случай, если что-то пойдёт не так).</summary>
    internal static class Log
    {
        const long MaxSize = 256 * 1024;
        static readonly object Sync = new object();
        static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WireGuardWidget.log");

        public static void Error(string message, Exception ex = null)
        {
            Write("ERROR", ex == null ? message : message + ": " + ex);
        }

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        static void Write(string level, string message)
        {
            try
            {
                lock (Sync)
                {
                    var info = new FileInfo(FilePath);
                    if (info.Exists && info.Length > MaxSize)
                        File.Delete(FilePath);
                    File.AppendAllText(FilePath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + message + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch
            {
                // журнал не должен ронять приложение
            }
        }
    }
}
