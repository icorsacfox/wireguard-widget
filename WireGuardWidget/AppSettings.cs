using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace WireGuardWidget
{
    [DataContract]
    internal sealed class TunnelPref
    {
        [DataMember(Order = 1)] public string Name { get; set; }
        [DataMember(Order = 2)] public string Emoji { get; set; }
    }

    /// <summary>Настройки утилиты; хранятся в JSON рядом с exe.</summary>
    [DataContract]
    internal sealed class AppSettings
    {
        static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(AppSettings));

        /// <summary>2 — размер иконки оверлея увеличен вдвое.</summary>
        const int CurrentVersion = 2;
        public const int MinOverlaySize = 16, MaxOverlaySize = 128;

        [DataMember(Order = 1)] public string WireGuardPath { get; set; }
        [DataMember(Order = 2)] public bool SingleConnection { get; set; }
        [DataMember(Order = 3)] public bool VerifyHandshake { get; set; }
        /// <summary>"Ctrl", "Ctrl+Alt", ... или пустая строка — хоткеи выключены.</summary>
        [DataMember(Order = 4)] public string HotkeyModifiers { get; set; }
        [DataMember(Order = 5)] public bool RunAtStartup { get; set; }
        /// <summary>Сейчас показан оверлей (восстанавливается при запуске).</summary>
        [DataMember(Order = 6)] public bool OverlayEnabled { get; set; }
        [DataMember(Order = 7)] public bool WindowVisible { get; set; }
        [DataMember(Order = 8)] public bool SettingsExpanded { get; set; }
        [DataMember(Order = 9)] public int? WindowX { get; set; }
        [DataMember(Order = 10)] public int? WindowY { get; set; }
        [DataMember(Order = 11)] public int? OverlayX { get; set; }
        [DataMember(Order = 12)] public int? OverlayY { get; set; }
        /// <summary>Размер эмодзи в оверлее, в логических пикселях.</summary>
        [DataMember(Order = 13)] public int OverlaySize { get; set; }
        /// <summary>Непрозрачность оверлея, %.</summary>
        [DataMember(Order = 14)] public int OverlayOpacity { get; set; }
        [DataMember(Order = 15)] public bool TrayHintShown { get; set; }
        /// <summary>Порядок (он же номер хоткея) и иконки конфигураций.</summary>
        [DataMember(Order = 16)] public List<TunnelPref> Tunnels { get; set; }
        [DataMember(Order = 17)] public int SettingsVersion { get; set; }
        /// <summary>"Dark", "Light" или "System" (как в Windows).</summary>
        [DataMember(Order = 18)] public string ColorTheme { get; set; }

        public AppSettings()
        {
            SetDefaults();
            SettingsVersion = CurrentVersion;
        }

        // DataContract-сериализатор не вызывает конструктор, поэтому значения по умолчанию задаются здесь.
        [OnDeserializing]
        void OnDeserializing(StreamingContext context)
        {
            SetDefaults();
        }

        void SetDefaults()
        {
            SingleConnection = true;
            VerifyHandshake = true;
            HotkeyModifiers = "Ctrl";
            WindowVisible = true;
            OverlaySize = 52;
            OverlayOpacity = 85;
            ColorTheme = "Dark";
            Tunnels = new List<TunnelPref>();
            // в файлах первой версии этого поля нет
            SettingsVersion = 1;
        }

        void Normalize()
        {
            if (Tunnels == null) Tunnels = new List<TunnelPref>();
            Tunnels.RemoveAll(t => t == null || string.IsNullOrWhiteSpace(t.Name));
            if (HotkeyModifiers == null) HotkeyModifiers = "Ctrl";
            if (ColorTheme != "Light" && ColorTheme != "System") ColorTheme = "Dark";
            if (SettingsVersion < 2)
                OverlaySize *= 2;
            SettingsVersion = CurrentVersion;
            OverlaySize = Math.Max(MinOverlaySize, Math.Min(MaxOverlaySize, OverlaySize));
            OverlayOpacity = Math.Max(20, Math.Min(100, OverlayOpacity));
        }

        public static AppSettings Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    using (var stream = File.OpenRead(path))
                    {
                        var settings = (AppSettings)Serializer.ReadObject(stream);
                        settings.Normalize();
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Не удалось прочитать настройки, используются значения по умолчанию", ex);
                try { File.Copy(path, path + ".bad", true); } catch { }
            }
            var defaults = new AppSettings();
            defaults.Normalize();
            return defaults;
        }

        public void Save(string path)
        {
            string tmp = path + ".tmp";
            using (var stream = File.Create(tmp))
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, new UTF8Encoding(false), false, true, "  "))
            {
                Serializer.WriteObject(writer, this);
                writer.Flush();
            }
            if (!File.Exists(path))
            {
                File.Move(tmp, path);
                return;
            }
            try
            {
                File.Replace(tmp, path, null);
            }
            catch (IOException)
            {
                // некоторые файловые системы не поддерживают Replace
                File.Delete(path);
                File.Move(tmp, path);
            }
        }
    }
}
