using System;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using SsmsSqlHelper.Diagnostics;

namespace SsmsSqlHelper.Settings
{
    /// <summary>User settings in %AppData%\SsmsSqlHelper\settings.json; picks up edits made while SSMS is running.</summary>
    internal sealed class SettingsStore
    {
        public static SettingsStore Instance { get; } = new SettingsStore();

        public static string FilePath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SsmsSqlHelper", "settings.json");

        private readonly object _lock = new object();
        private UserSettings _current = new UserSettings();
        private DateTime _loadedWriteTime;

        public event EventHandler Changed;

        public UserSettings Current
        {
            get
            {
                lock (_lock)
                {
                    ReloadIfChanged();
                    return _current;
                }
            }
        }

        public void Save(UserSettings settings)
        {
            lock (_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, settings.ToJson(), new UTF8Encoding(false));
                _current = settings;
                _loadedWriteTime = File.GetLastWriteTimeUtc(FilePath);
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void ReloadIfChanged()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return;

                var writeTime = File.GetLastWriteTimeUtc(FilePath);
                if (writeTime == _loadedWriteTime)
                    return;

                _loadedWriteTime = writeTime;
                _current = UserSettings.Parse(File.ReadAllText(FilePath));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SerializationException)
            {
                // A broken settings file just means defaults / the last good values
                Log.Error("Failed to load settings; keeping previous values", ex);
            }
        }
    }
}
