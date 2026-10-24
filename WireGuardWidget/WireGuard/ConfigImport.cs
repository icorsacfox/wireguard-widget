using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace WireGuardWidget.WireGuard
{
    /// <summary>Проверка .conf-файла и имени туннеля перед добавлением в WireGuard.</summary>
    internal static class ConfigImport
    {
        const int MaxNameLength = 32;
        const long MaxFileSize = 256 * 1024;

        // те же правила, что у WireGuard for Windows (conf/name.go)
        static readonly Regex AllowedName = new Regex("^[a-zA-Z0-9_=+.-]{1," + MaxNameLength + "}$");
        static readonly Regex ForbiddenChars = new Regex("[^a-zA-Z0-9_=+.-]");
        static readonly string[] ReservedNames =
            new[] { "CON", "PRN", "AUX", "NUL" }
            .Concat(Enumerable.Range(1, 9).Select(i => "COM" + i))
            .Concat(Enumerable.Range(1, 9).Select(i => "LPT" + i))
            .ToArray();

        /// <summary>Читает файл; возвращает текст ошибки или null.</summary>
        public static string ReadConfig(string path, out string content)
        {
            content = null;
            try
            {
                if (new FileInfo(path).Length > MaxFileSize)
                    return "Файл слишком большой для конфигурации WireGuard.";
                // кодировка определяется по BOM (UTF-8/UTF-16), иначе UTF-8
                content = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                return "Не удалось прочитать файл: " + ex.Message;
            }
            bool hasInterface = Regex.IsMatch(content, @"^\s*\[Interface\]", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            bool hasKey = Regex.IsMatch(content, @"^\s*PrivateKey\s*=", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (!hasInterface || !hasKey)
                return "Файл не похож на конфигурацию WireGuard: нет секции [Interface] с PrivateKey.";
            return null;
        }

        /// <summary>Имя туннеля из имени файла, приведённое к допустимому виду.</summary>
        public static string SuggestName(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path) ?? "";
            name = ForbiddenChars.Replace(name, "_").Trim('_');
            if (name.Length > MaxNameLength)
                name = name.Substring(0, MaxNameLength);
            return name.Length == 0 ? "tunnel" : name;
        }

        /// <summary>Текст ошибки или null, если имя допустимо (существование не проверяется).</summary>
        public static string ValidateName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "Введите имя туннеля.";
            if (name.Length > MaxNameLength)
                return "Имя не длиннее " + MaxNameLength + " символов.";
            if (!AllowedName.IsMatch(name))
                return "Только латинские буквы, цифры и символы _ = + . -";
            if (ReservedNames.Contains(name.ToUpperInvariant()))
                return "Это имя зарезервировано Windows.";
            return null;
        }
    }
}
