using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SsmsSqlHelper.Diagnostics;

namespace SsmsSqlHelper.Backup
{
    /// <summary>
    /// The one place unsaved-tab copies are written from. Disk work runs in the background and strictly in the order it was
    /// asked for, so a copy that was removed (the tab was saved) is never brought back by a write that was still waiting.
    /// </summary>
    internal sealed class BackupService
    {
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SsmsSqlHelper");

        public static string BackupsFolder { get; } = Path.Combine(AppDataFolder, "Backups");

        /// <summary>Where recovered queries are put: ordinary .sql files that belong to the user from then on.</summary>
        public static string RecoveredFolder { get; } = Path.Combine(AppDataFolder, "Recovered");

        /// <summary>Copies older than this are given up on and deleted when they are looked for.</summary>
        public static readonly TimeSpan MaxAge = TimeSpan.FromDays(14);

        // Declared after the folders above: static initializers run in textual order, and the constructor needs BackupsFolder
        public static BackupService Instance { get; } = new BackupService();

        private readonly BackupStore _store = new BackupStore(BackupsFolder);
        private readonly object _queueLock = new object();
        private Task _tail = Task.CompletedTask;

        /// <summary>Claims this session's folder (and its lock), so other instances know it is alive. Safe to call again.</summary>
        public void Start()
        {
            try
            {
                _store.StartSession();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Error("Could not create the backup folder; unsaved tabs will not be backed up", ex);
            }
        }

        public void Save(string id, BackupMeta meta, string text) => Enqueue(() => _store.Save(id, meta, text), "save a backup");

        public void Remove(string id) => Enqueue(() => _store.Remove(id), "remove a backup");

        /// <summary>Copies left behind by sessions that are gone. Also cleans up old and pointless ones.</summary>
        public List<BackupEntry> FindOrphans()
        {
            Start();
            try
            {
                lock (_queueLock)
                    return _store.FindOrphans(MaxAge);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Error("Could not look for unsaved tabs of earlier sessions", ex);
                return new List<BackupEntry>();
            }
        }

        private void Enqueue(Action work, string what)
        {
            lock (_queueLock)
            {
                _tail = _tail.ContinueWith(_ =>
                {
                    try
                    {
                        work();
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        // A full disk or a locked file must never get in the way of typing
                        Log.Error("Could not " + what, ex);
                    }
                }, TaskScheduler.Default);
            }
        }
    }
}
