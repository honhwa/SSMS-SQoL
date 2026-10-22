using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SsmsSqlHelper.Diagnostics;

namespace SsmsSqlHelper.Metadata
{
    internal static partial class MetadataLoader
    {
        private const string SynonymQuery = @"
SELECT s.name, sn.name, sn.base_object_name
FROM sys.synonyms sn
JOIN sys.schemas s ON s.schema_id = sn.schema_id
WHERE sn.is_ms_shipped = 0
ORDER BY s.name, sn.name;";

        private static async Task LoadSynonymsAsync(SqlConnection sql, List<TableInfo> tables, CancellationToken cancellationToken)
        {
            // Optional remote metadata must not exhaust the budget for the database's own tables.
            using (var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                budget.CancelAfter(TimeSpan.FromSeconds(30));
                try
                {
                    var synonyms = new List<SynonymInfo>();
                    using (var cmd = new SqlCommand(SynonymQuery, sql) { CommandTimeout = 10 })
                    using (var reader = await cmd.ExecuteReaderAsync(budget.Token).ConfigureAwait(false))
                        while (await reader.ReadAsync(budget.Token).ConfigureAwait(false))
                            synonyms.Add(new SynonymInfo(reader.GetString(0), reader.GetString(1), reader.GetString(2)));

                    var local = tables.ToLookup(t => TargetKey(t.Schema, t.Name), StringComparer.OrdinalIgnoreCase);
                    foreach (var synonym in synonyms.Where(s => s.IsLocal(sql.Database)))
                    {
                        var target = local[TargetKey(synonym.TargetSchema, synonym.TargetName)].FirstOrDefault();
                        if (target != null)
                            tables.Add(synonym.Bind(target));
                    }

                    foreach (var group in synonyms.Where(s => !s.IsLocal(sql.Database))
                        .GroupBy(s => s.CatalogPrefix, StringComparer.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var batch = group.ToList();
                            // Two parameters per synonym, comfortably below SQL Server's 2100-parameter limit.
                            for (var offset = 0; offset < batch.Count; offset += 400)
                            {
                                var current = batch.Skip(offset).Take(400).ToList();
                                using (var cmd = new SqlCommand { Connection = sql, CommandTimeout = 10 })
                                {
                                    var predicates = new List<string>();
                                    for (var i = 0; i < current.Count; i++)
                                    {
                                        predicates.Add($"(s.name = @schema{i} AND o.name = @name{i})");
                                        cmd.Parameters.AddWithValue("@schema" + i, current[i].TargetSchema);
                                        cmd.Parameters.AddWithValue("@name" + i, current[i].TargetName);
                                    }
                                    // Only catalog identifiers are interpolated, quoted from parsed identifier parts.
                                    // Object names remain parameters; table data is never read.
                                    cmd.CommandText = Query.Replace("sys.", group.Key + "sys.")
                                        .Replace("ORDER BY s.name, o.name, c.column_id;",
                                            "AND (" + string.Join(" OR ", predicates) + ") ORDER BY s.name, o.name, c.column_id;");
                                    var targets = (await ReadTablesAsync(cmd, budget.Token).ConfigureAwait(false))
                                        .ToLookup(t => TargetKey(t.Schema, t.Name), StringComparer.OrdinalIgnoreCase);
                                    foreach (var synonym in current)
                                    {
                                        var target = targets[TargetKey(synonym.TargetSchema, synonym.TargetName)].FirstOrDefault();
                                        if (target != null)
                                            tables.Add(synonym.Bind(target));
                                    }
                                }
                            }
                        }
                        catch (SqlException ex)
                        {
                            Log.Error($"Could not read synonym targets from {group.Key}; keeping other metadata", ex);
                        }
                    }
                    if (synonyms.Count > 0)
                        Log.Info($"Loaded {tables.Count(t => t.IsSynonym)} table/view synonyms out of {synonyms.Count} synonyms");
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    Log.Info("Synonym metadata budget reached; keeping columns already loaded");
                }
                catch (SqlException ex)
                {
                    Log.Error("Could not read synonyms; keeping other metadata", ex);
                }
            }
        }

        private static string TargetKey(string schema, string name) => schema + "\u0001" + name;
    }
}
