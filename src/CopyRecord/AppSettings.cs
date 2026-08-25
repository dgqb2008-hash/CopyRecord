using System;
using System.IO;
using System.Runtime.Serialization;
using Microsoft.Win32;

namespace CopyRecord
{
    [DataContract]
    internal sealed class AppSettings
    {
        [DataMember] public bool StartWithWindows { get; set; }
        [DataMember] public string HotkeyModifiers { get; set; }
        [DataMember] public string HotkeyKey { get; set; }
        [DataMember] public int MaximumItems { get; set; }
        [DataMember] public int ImageRetentionDays { get; set; }
        [DataMember] public int ImageMaximumMegabytes { get; set; }
        [DataMember] public bool IgnoreSensitiveText { get; set; }
        [DataMember] public string ExcludedApplications { get; set; }
        [DataMember] public bool FirstRunCompleted { get; set; }

        internal static AppSettings Defaults()
        {
            return new AppSettings
            {
                StartWithWindows = false,
                HotkeyModifiers = "Ctrl+Shift",
                HotkeyKey = "V",
                MaximumItems = 5000,
                ImageRetentionDays = 0,
                ImageMaximumMegabytes = 500,
                IgnoreSensitiveText = false,
                ExcludedApplications = string.Empty
            };
        }

        internal void Normalize()
        {
            if (HotkeyModifiers != "Ctrl+Shift" && HotkeyModifiers != "Ctrl+Alt" && HotkeyModifiers != "Alt+Shift")
                HotkeyModifiers = "Ctrl+Shift";
            HotkeyKey = string.IsNullOrWhiteSpace(HotkeyKey) ? "V" : HotkeyKey.Trim().Substring(0, 1).ToUpperInvariant();
            if (!char.IsLetterOrDigit(HotkeyKey[0])) HotkeyKey = "V";
            MaximumItems = Math.Max(50, Math.Min(50000, MaximumItems));
            ImageRetentionDays = Math.Max(0, Math.Min(3650, ImageRetentionDays));
            ImageMaximumMegabytes = Math.Max(10, Math.Min(10240, ImageMaximumMegabytes));
            ExcludedApplications = ExcludedApplications == null ? string.Empty : ExcludedApplications.Trim();
        }
    }

    internal sealed class SettingsStore
    {
        private readonly string _directory;
        private readonly string _path;
        private readonly DataContractSerializer _serializer = new DataContractSerializer(typeof(AppSettings));

        internal string DirectoryPath { get { return _directory; } }

        internal SettingsStore()
        {
            string overrideDirectory = Environment.GetEnvironmentVariable("COPYRECORD_DATA_DIR");
            _directory = string.IsNullOrWhiteSpace(overrideDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CopyRecord")
                : overrideDirectory;
            _path = Path.Combine(_directory, "settings.xml");
        }

        internal AppSettings Load()
        {
            try
            {
                if (File.Exists(_path))
                {
                    using (FileStream stream = File.OpenRead(_path))
                    {
                        AppSettings settings = (AppSettings)_serializer.ReadObject(stream);
                        settings.Normalize();
                        return settings;
                    }
                }
            }
            catch { }
            return AppSettings.Defaults();
        }

        internal void Save(AppSettings settings)
        {
            settings.Normalize();
            Directory.CreateDirectory(_directory);
            string temporary = _path + ".tmp";
            using (FileStream stream = File.Create(temporary)) _serializer.WriteObject(stream, settings);
            if (File.Exists(_path)) File.Delete(_path);
            File.Move(temporary, _path);
            ApplyStartup(settings.StartWithWindows);
        }

        private static void ApplyStartup(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (enabled)
                        key.SetValue("CopyRecord", "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\"");
                    else
                        key.DeleteValue("CopyRecord", false);
                }
            }
            catch { }
        }
    }
}
