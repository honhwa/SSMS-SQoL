using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;
using SsmsSqlHelper.Diagnostics;
using SsmsSqlHelper.Ssms;

namespace SsmsSqlHelper.Metadata
{
    /// <summary>
    /// Caches one <see cref="DbMetadata"/> per server + database + login. Loads run in the background;
    /// editor code reads the cache synchronously and never waits on the network.
    /// </summary>
    internal sealed class MetadataService
    {
        public static MetadataService Instance { get; } = new MetadataService();

        /// <summary>How often a query window gaining focus may trigger a schema check.</summary>
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

        private readonly ConcurrentDictionary<string, Task<DbMetadata>> _cache = new ConcurrentDictionary<string, Task<DbMetadata>>();
        private readonly ConcurrentDictionary<string, DateTime> _lastCheck = new ConcurrentDictionary<string, DateTime>();
        private readonly ConcurrentDictionary<string, byte> _checking = new ConcurrentDictionary<string, byte>();

        /// <summary>Raised (on a background thread) after a freshness check found a schema change and reloaded the metadata.</summary>
        public event Action<DbMetadata> SchemaReloaded;

        /// <summary>Returns cached metadata, or null if it isn't loaded yet (and starts loading it).</summary>
        public DbMetadata TryGet(ActiveConnection connection)
        {
            if (connection == null)
                return null;

            var task = GetOrLoadAsync(connection);
#pragma warning disable VSTHRD002 // Only read after the task has completed; never blocks
            return task.Status == TaskStatus.RanToCompletion ? task.Result : null;
#pragma warning restore VSTHRD002
        }

        public Task<DbMetadata> GetOrLoadAsync(ActiveConnection connection) =>
            _cache.GetOrAdd(connection.CacheKey, _ => LoadAsync(connection));

        public Task<DbMetadata> RefreshAsync(ActiveConnection connection)
        {
            var task = LoadAsync(connection);
            _cache[connection.CacheKey] = task;
            return task;
        }

        /// <summary>
        /// Compares the database's schema fingerprint with the one the cache was built from and reloads on a difference,
        /// so tables created or columns added while SSMS is open show up without a manual refresh.
        /// Cheap (one small query), at most once per <see cref="CheckInterval"/> unless <paramref name="force"/> is set, and never blocks the caller.
        /// </summary>
        public void RequestFreshnessCheck(ActiveConnection connection, bool force = false)
        {
            if (connection == null)
                return;

            var key = connection.CacheKey;
            if (!_cache.TryGetValue(key, out var task) || task.Status != TaskStatus.RanToCompletion)
                return;

            var now = DateTime.UtcNow;
            if (!force && _lastCheck.TryGetValue(key, out var last) && now - last < CheckInterval)
                return;
            if (!_checking.TryAdd(key, 0))
                return;

            _lastCheck[key] = now;
#pragma warning disable VSTHRD002 // Completed task, checked above
            var loaded = task.Result;
#pragma warning restore VSTHRD002
            CheckAsync(connection, loaded).Forget();
        }

        private async Task CheckAsync(ActiveConnection connection, DbMetadata loaded)
        {
            await TaskScheduler.Default;
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    var stamp = await MetadataLoader.ReadStampAsync(connection, cts.Token).ConfigureAwait(false);
                    if (stamp == null || loaded.SchemaStamp == null || stamp == loaded.SchemaStamp)
                        return;

                    Log.Info($"Schema of {connection} changed ({loaded.SchemaStamp} -> {stamp}); reloading metadata");
                    var fresh = await RefreshAsync(connection).ConfigureAwait(false);
                    SchemaReloaded?.Invoke(fresh);
                }
            }
            catch (Exception ex)
            {
                // Offline or no permission: keep using what we have, and try again at the next check
                Log.Error($"Schema check failed for {connection}", ex);
            }
            finally
            {
                _checking.TryRemove(connection.CacheKey, out _);
            }
        }

        private async Task<DbMetadata> LoadAsync(ActiveConnection connection)
        {
            // Leave the caller's thread (usually the UI thread) before touching the network
            await TaskScheduler.Default;
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90)))
                {
                    var metadata = await MetadataLoader.LoadAsync(connection, cts.Token).ConfigureAwait(false);
                    Log.Info($"Loaded metadata for {connection}: {metadata.Tables.Count} tables/views, " +
                             $"{metadata.ForeignKeys.Count} foreign keys, {metadata.ColumnCount} columns in {metadata.LoadDuration.TotalMilliseconds:N0} ms");
                    return metadata;
                }
            }
            catch (Exception ex)
            {
                // Drop the failed entry so the next request retries instead of caching the failure
                _cache.TryRemove(connection.CacheKey, out _);
                Log.Error($"Failed to load metadata for {connection}", ex);
                throw;
            }
        }
    }
}
