using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using SsmsSqlHelper.Diagnostics;

namespace SsmsSqlHelper.Snippets
{
    /// <summary>
    /// Snippets live in %AppData%\SsmsSqlHelper\snippets.json so each user can edit them, either in the
    /// editor window or by hand. The file is seeded from the embedded defaults on first use and reloaded
    /// whenever it changes.
    /// </summary>
    internal sealed class SnippetStore
    {
        public static SnippetStore Instance { get; } = new SnippetStore();

        public static string FilePath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SsmsSqlHelper", "snippets.json");

        private readonly object _lock = new object();
        private List<Snippet> _all = new List<Snippet>();
        private Dictionary<string, Snippet> _byShortcut = new Dictionary<string, Snippet>(StringComparer.OrdinalIgnoreCase);
        private DateTime _loadedWriteTime;

        public Snippet Find(string shortcut)
        {
            lock (_lock)
            {
                ReloadIfChanged();
                return _byShortcut.TryGetValue(shortcut, out var snippet) ? snippet : null;
            }
        }

        /// <summary>The usable snippets in file order (blank shortcuts dropped, later duplicates win, as with <see cref="Find"/>).</summary>
        public IReadOnlyList<Snippet> All
        {
            get
            {
                lock (_lock)
                {
                    ReloadIfChanged();
                    return _all;
                }
            }
        }

        public void EnsureFileExists()
        {
            if (File.Exists(FilePath))
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, ReadDefaultsText(), new UTF8Encoding(false));
            Log.Info("Created " + FilePath);
        }

        /// <summary>Reads the file as the editor should see it: every entry, in order. Throws if the file can't be read or parsed.</summary>
        public List<Snippet> ReadFileForEditing()
        {
            EnsureFileExists();
            return SnippetFileFormat.Parse(File.ReadAllText(FilePath));
        }

        public static List<Snippet> ReadDefaults() => SnippetFileFormat.Parse(ReadDefaultsText());

        public void Save(IEnumerable<Snippet> snippets)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, SnippetFileFormat.Write(snippets), new UTF8Encoding(false));
            lock (_lock)
                _loadedWriteTime = default; // force a reload on next use
        }

        private void ReloadIfChanged()
        {
            try
            {
                EnsureFileExists();
                var writeTime = File.GetLastWriteTimeUtc(FilePath);
                if (writeTime == _loadedWriteTime)
                    return;

                var all = SnippetFileFormat.Parse(File.ReadAllText(FilePath));
                var byShortcut = new Dictionary<string, Snippet>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in all.Where(s => !string.IsNullOrWhiteSpace(s.Shortcut)))
                    byShortcut[s.Shortcut] = s;

                _byShortcut = byShortcut;
                _all = all.Where(s => !string.IsNullOrWhiteSpace(s.Shortcut) && byShortcut[s.Shortcut] == s).ToList();
                _loadedWriteTime = writeTime;
                Log.Info($"Loaded {_all.Count} snippets from {FilePath}");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SerializationException)
            {
                // Keep the last good set so a typo in the JSON doesn't disable every snippet
                Log.Error("Failed to load snippets; keeping previous set", ex);
                _loadedWriteTime = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : default;
            }
        }

        private static string ReadDefaultsText()
        {
            using (var stream = typeof(SnippetStore).Assembly.GetManifestResourceStream("SsmsSqlHelper.Snippets.DefaultSnippets.json"))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                return reader.ReadToEnd();
        }
    }
}
