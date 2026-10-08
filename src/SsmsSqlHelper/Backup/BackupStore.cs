using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SsmsSqlHelper.Backup
{
    /// <summary>What is known about a backed-up query besides its text.</summary>
    [DataContract]
    internal sealed class BackupMeta
    {
        /// <summary>The file the tab is attached to (a temp file for a new query window); null if unknown.</summary>
        [DataMember(Name = "filePath")]
        public string FilePath { get; set; }

        [DataMember(Name = "server")]
        public string Server { get; set; }

        [DataMember(Name = "database")]
        public string Database { get; set; }

        /// <summary>Local time of the last backup, <c>yyyy-MM-ddTHH:mm:ss</c>.</summary>
        [DataMember(Name = "savedAt")]
        public string SavedAt { get; set; }
    }

    /// <summary>One backed-up query left behind by a session that is no longer running.</summary>
    internal sealed class BackupEntry
    {
        public string SessionDir { get; set; }
        public string Id { get; set; }
        public BackupMeta Meta { get; set; }
        public string TextPath { get; set; }
        public string MetaPath { get; set; }
        public DateTime SavedAt { get; set; }
        public long Bytes { get; set; }

        /// <summary>The first line with something on it, for a list.</summary>
        public string Preview
        {
            get
            {
                try
                {
                    using (var reader = new StreamReader(TextPath, Encoding.UTF8))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            line = line.Trim();
                            if (line.Length > 0)
                                return line.Length > 80 ? line.Substring(0, 77) + "..." : line;
                        }
                    }
                }
                catch (IOException)
                {
                }
                return "(empty)";
            }
        }
    }

    /// <summary>
    /// A running instance holds an exclusive lock on a file in its session folder, so another instance (or the next start after
    /// a crash) can tell a live session from a dead one. The operating system drops the lock when the process ends, however it ends.
    /// </summary>
    internal sealed class SessionLock : IDisposable
    {
        private const string FileName = ".lock";
        private FileStream _stream;

        private SessionLock(FileStream stream)
        {
            _stream = stream;
        }

        public static SessionLock Acquire(string sessionDir)
        {
            Directory.CreateDirectory(sessionDir);
            return new SessionLock(new FileStream(Path.Combine(sessionDir, FileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }

        /// <summary>True if some running process (or this one) holds the session's lock.</summary>
        public static bool IsHeld(string sessionDir)
        {
            var path = Path.Combine(sessionDir, FileName);
            if (!File.Exists(path))
                return false;

            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    return false;
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;        // can't tell: leave it alone
            }
        }

        public void Dispose()
        {
            _stream?.Dispose();
            _stream = null;
        }
    }

    /// <summary>
    /// Copies of unsaved query text on disk, one folder per running session. A tab's copy is removed when the tab is saved or closed,
    /// so what is left in a session that is no longer running is exactly what a crash or an End Task took away.
    /// No Visual Studio dependencies.
    /// </summary>
    internal sealed class BackupStore : IDisposable
    {
        private readonly string _root;
        private readonly object _gate = new object();
        private SessionLock _lock;

        public BackupStore(string root)
        {
            _root = root;
        }

        /// <summary>The current session's folder, once <see cref="StartSession"/> has run.</summary>
        public string SessionDir { get; private set; }

        public void StartSession()
        {
            // The UI thread and the background writer may both be first to ask
            lock (_gate)
            {
                if (SessionDir != null)
                    return;

                var name = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Process.GetCurrentProcess().Id + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                var dir = Path.Combine(_root, name);
                _lock = SessionLock.Acquire(dir);
                SessionDir = dir;
            }
        }

        /// <summary>Writes (or replaces) the copy of one tab. The text goes first: a copy only counts once its meta file exists.</summary>
        public void Save(string id, BackupMeta meta, string text)
        {
            StartSession();
            Replace(Path.Combine(SessionDir, id + ".sql"), tmp => File.WriteAllText(tmp, text, new UTF8Encoding(false)));
            Replace(Path.Combine(SessionDir, id + ".json"), tmp =>
            {
                using (var stream = File.Create(tmp))
                    new DataContractJsonSerializer(typeof(BackupMeta)).WriteObject(stream, meta);
            });
        }

        /// <summary>Removes a tab's copy (it was saved, or closed on purpose).</summary>
        public void Remove(string id)
        {
            if (SessionDir == null)
                return;

            // The meta file goes first, so a half-removed copy never looks complete
            TryDelete(Path.Combine(SessionDir, id + ".json"));
            TryDelete(Path.Combine(SessionDir, id + ".sql"));
        }

        /// <summary>
        /// Copies left by sessions that are not running any more. Along the way: copies older than <paramref name="maxAge"/> are
        /// deleted, copies identical to the file they belong to (nothing was lost) too, and emptied session folders are cleaned up.
        /// </summary>
        public List<BackupEntry> FindOrphans(TimeSpan maxAge)
        {
            var result = new List<BackupEntry>();
            if (!Directory.Exists(_root))
                return result;

            foreach (var dir in Directory.GetDirectories(_root))
            {
                if (string.Equals(Path.GetFullPath(dir), SessionDir == null ? null : Path.GetFullPath(SessionDir), StringComparison.OrdinalIgnoreCase) ||
                    SessionLock.IsHeld(dir))
                    continue;

                var remaining = 0;
                foreach (var textPath in Directory.GetFiles(dir, "*.sql"))
                {
                    var entry = ReadEntry(dir, textPath);

                    if (DateTime.Now - entry.SavedAt > maxAge || IsIdenticalToItsFile(entry))
                    {
                        Discard(entry);
                        continue;
                    }

                    result.Add(entry);
                    remaining++;
                }

                // Discarding the last copy already removes the folder; one that still holds text we could not read is kept
                if (remaining == 0 && Directory.Exists(dir) && !Directory.GetFiles(dir, "*.sql").Any())
                    TryDeleteDirectory(dir);
            }

            return result.OrderByDescending(e => e.SavedAt).ToList();
        }

        /// <summary>Moves a copy out of the backups into <paramref name="recoveredDir"/> as a .sql file of its own, and returns its path.</summary>
        public static string Recover(BackupEntry entry, string recoveredDir)
        {
            Directory.CreateDirectory(recoveredDir);
            var path = UniquePath(recoveredDir, RecoveredFileName(entry));
            File.Copy(entry.TextPath, path);
            Discard(entry);
            return path;
        }

        public static void Discard(BackupEntry entry)
        {
            TryDelete(entry.MetaPath);
            TryDelete(entry.TextPath);

            // an emptied session folder is of no use to anyone
            var dir = entry.SessionDir;
            if (Directory.Exists(dir) && !SessionLock.IsHeld(dir) && !Directory.GetFiles(dir, "*.sql").Any())
                TryDeleteDirectory(dir);
        }

        /// <summary><c>Recovered_2026-10-08_10-14_PJM-Dev-SQL_master.sql</c>: when it was last backed up and where it was connected.</summary>
        public static string RecoveredFileName(BackupEntry entry)
        {
            var parts = new List<string> { "Recovered", entry.SavedAt.ToString("yyyy-MM-dd_HH-mm") };
            if (!string.IsNullOrWhiteSpace(entry.Meta?.Server))
                parts.Add(entry.Meta.Server);
            if (!string.IsNullOrWhiteSpace(entry.Meta?.Database))
                parts.Add(entry.Meta.Database);

            var name = string.Join("_", parts);
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            name = name.Replace(',', '_').Replace(' ', '_');
            return (name.Length > 100 ? name.Substring(0, 100) : name) + ".sql";
        }

        public void Dispose()
        {
            _lock?.Dispose();
            _lock = null;
        }

        // ---- internals ----

        // The text is what matters; a missing or damaged meta file only means we know less about where it came from
        private static BackupEntry ReadEntry(string dir, string textPath)
        {
            var id = Path.GetFileNameWithoutExtension(textPath);
            var metaPath = Path.Combine(dir, id + ".json");

            BackupMeta meta = null;
            try
            {
                if (File.Exists(metaPath))
                {
                    using (var stream = File.OpenRead(metaPath))
                        meta = (BackupMeta)new DataContractJsonSerializer(typeof(BackupMeta)).ReadObject(stream);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is SerializationException || ex is UnauthorizedAccessException)
            {
                meta = null;
            }

            try
            {
                meta = meta ?? new BackupMeta();
                var savedAt = DateTime.TryParse(meta.SavedAt, out var parsed) ? parsed : File.GetLastWriteTime(textPath);
                return new BackupEntry
                {
                    SessionDir = dir, Id = id, Meta = meta, TextPath = textPath, MetaPath = metaPath,
                    SavedAt = savedAt, Bytes = new FileInfo(textPath).Length,
                };
            }
            catch (IOException)
            {
                return new BackupEntry { SessionDir = dir, Id = id, Meta = new BackupMeta(), TextPath = textPath, MetaPath = metaPath, SavedAt = DateTime.Now };
            }
        }

        // The tab had been saved to that file and the copy says nothing different: nothing was lost
        private static bool IsIdenticalToItsFile(BackupEntry entry)
        {
            var path = entry.Meta?.FilePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;

            try
            {
                return Normalize(File.ReadAllText(path)) == Normalize(File.ReadAllText(entry.TextPath));
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd();

        private static void Replace(string target, Action<string> write)
        {
            var tmp = target + ".tmp";
            write(tmp);
            if (File.Exists(target))
                File.Replace(tmp, target, null);
            else
                File.Move(tmp, target);
        }

        private static string UniquePath(string dir, string fileName)
        {
            var path = Path.Combine(dir, fileName);
            var name = Path.GetFileNameWithoutExtension(fileName);
            for (var n = 2; File.Exists(path); n++)
                path = Path.Combine(dir, name + "_" + n + ".sql");
            return path;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // cleaned up by a later pass
            }
        }

        private static void TryDeleteDirectory(string dir)
        {
            try
            {
                foreach (var f in Directory.GetFiles(dir))
                    File.Delete(f);
                Directory.Delete(dir);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }
}
