using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SsmsSqlHelper.Diagnostics;
using SsmsSqlHelper.Ssms;

namespace SsmsSqlHelper.Metadata
{
    /// <summary>Loads accessible database names for USE completion without blocking the editor.</summary>
    internal sealed class DatabaseNameService
    {
        private sealed class Entry
        {
            public Entry(Func<Task<IReadOnlyList<string>>> load)
            {
                Created = DateTime.UtcNow;
                Task = System.Threading.Tasks.Task.Run(load);
            }

            public DateTime Created { get; }
            public Task<IReadOnlyList<string>> Task { get; }
        }

        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(2);
        private readonly ConcurrentDictionary<string, Entry> _cache = new ConcurrentDictionary<string, Entry>();

        public static DatabaseNameService Instance { get; } = new DatabaseNameService();

        public Task<IReadOnlyList<string>> GetOrLoadAsync(ActiveConnection connection)
        {
            var key = (connection.Server + "|" + connection.UserName + "|" + connection.IsSqlAuth).ToUpperInvariant();
            var entry = _cache.GetOrAdd(key, _ => new Entry(() => LoadAsync(connection, key)));
            if (DateTime.UtcNow - entry.Created >= CacheLifetime)
            {
                var replacement = new Entry(() => LoadAsync(connection, key));
                _cache.TryUpdate(key, replacement, entry);
                entry = _cache[key];
            }
            return entry.Task;
        }

        private async Task<IReadOnlyList<string>> LoadAsync(ActiveConnection connection, string key)
        {
            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                using (var sql = connection.CreateSqlConnection())
                {
                    await sql.OpenAsync(timeout.Token).ConfigureAwait(false);
                    using (var command = new SqlCommand(
                        "SELECT name FROM sys.databases WHERE state = 0 AND HAS_DBACCESS(name) = 1 ORDER BY name", sql)
                        { CommandTimeout = 15 })
                    using (var reader = await command.ExecuteReaderAsync(timeout.Token).ConfigureAwait(false))
                    {
                        var names = new List<string>();
                        while (await reader.ReadAsync(timeout.Token).ConfigureAwait(false))
                            names.Add(reader.GetString(0));
                        if (!string.IsNullOrEmpty(connection.Database) &&
                            !names.Exists(name => string.Equals(name, connection.Database, StringComparison.OrdinalIgnoreCase)))
                            names.Add(connection.Database);
                        names.Sort(StringComparer.OrdinalIgnoreCase);
                        return names;
                    }
                }
            }
            catch (Exception ex)
            {
                _cache.TryRemove(key, out _);
                Log.Error("Could not load database names for USE completion", ex);
                return string.IsNullOrEmpty(connection.Database)
                    ? (IReadOnlyList<string>)Array.Empty<string>()
                    : new[] { connection.Database };
            }
        }
    }
}
