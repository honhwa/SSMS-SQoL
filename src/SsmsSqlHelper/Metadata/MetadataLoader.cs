using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SsmsSqlHelper.Diagnostics;
using SsmsSqlHelper.Ssms;

namespace SsmsSqlHelper.Metadata
{
    /// <summary>Reads table/view/column metadata over a dedicated connection (never the query window's own).</summary>
    internal static class MetadataLoader
    {
        private const string Query = @"
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
SELECT s.name, o.name, o.type, c.column_id, c.name, t.name,
       c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity, c.is_computed,
       CAST(CASE WHEN c.default_object_id <> 0 THEN 1 ELSE 0 END AS bit),
       CAST(CASE WHEN pk.column_id IS NULL THEN 0 ELSE 1 END AS bit),
       TYPE_NAME(c.system_type_id)
FROM sys.objects o
JOIN sys.schemas s ON s.schema_id = o.schema_id
JOIN sys.columns c ON c.object_id = o.object_id
JOIN sys.types t ON t.user_type_id = c.user_type_id
LEFT JOIN (
    SELECT ic.object_id, ic.column_id
    FROM sys.indexes i
    JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
    WHERE i.is_primary_key = 1
) pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
WHERE o.type IN ('U', 'V') AND o.is_ms_shipped = 0
ORDER BY s.name, o.name, c.column_id;";

        private const string ForeignKeyQuery = @"
SELECT fk.object_id, fk.name, ps.name, pt.name, rs.name, rt.name, pc.name, rc.name
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.tables pt ON pt.object_id = fk.parent_object_id
JOIN sys.schemas ps ON ps.schema_id = pt.schema_id
JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
ORDER BY fk.object_id, fkc.constraint_column_id;";

        // Changes whenever a table, view, key or foreign key is created, altered or dropped
        private const string StampQuery = @"
SELECT CAST(COUNT(*) AS varchar(20)) + '|' + ISNULL(CONVERT(varchar(30), MAX(modify_date), 126), '')
FROM sys.objects WHERE type IN ('U', 'V', 'F', 'PK') AND is_ms_shipped = 0;";

        /// <summary>A cheap fingerprint of the schema: compare with <see cref="DbMetadata.SchemaStamp"/> to see if a reload is needed.</summary>
        public static async Task<string> ReadStampAsync(ActiveConnection connection, CancellationToken cancellationToken)
        {
            using (var sql = connection.CreateSqlConnection())
            {
                await sql.OpenAsync(cancellationToken).ConfigureAwait(false);
                return await ReadStampAsync(sql, cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task<string> ReadStampAsync(SqlConnection sql, CancellationToken cancellationToken)
        {
            using (var cmd = new SqlCommand(StampQuery, sql) { CommandTimeout = 30 })
                return (await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) as string;
        }

        public static async Task<DbMetadata> LoadAsync(ActiveConnection connection, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            var tables = new List<TableInfo>();
            List<ForeignKeyInfo> foreignKeys = null;
            string stamp;

            using (var sql = connection.CreateSqlConnection())
            {
                await sql.OpenAsync(cancellationToken).ConfigureAwait(false);

                // Read before the tables: a change made while loading then shows up as a difference at the next check
                stamp = await ReadStampAsync(sql, cancellationToken).ConfigureAwait(false);
                using (var cmd = new SqlCommand(Query, sql) { CommandTimeout = 60 })
                using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    TableInfo current = null;
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var schema = reader.GetString(0);
                        var name = reader.GetString(1);
                        if (current == null || current.Name != name || current.Schema != schema)
                        {
                            current = new TableInfo(schema, name, reader.GetString(2).Trim() == "V");
                            tables.Add(current);
                        }

                        current.Columns.Add(new ColumnInfo
                        {
                            Ordinal = reader.GetInt32(3),
                            Name = reader.GetString(4),
                            TypeName = reader.GetString(5),
                            MaxLength = reader.GetInt16(6),
                            Precision = reader.GetByte(7),
                            Scale = reader.GetByte(8),
                            IsNullable = reader.GetBoolean(9),
                            IsIdentity = reader.GetBoolean(10),
                            IsComputed = reader.GetBoolean(11),
                            HasDefault = reader.GetBoolean(12),
                            IsPrimaryKey = reader.GetBoolean(13),
                            BaseTypeName = reader.GetValue(14) as string ?? reader.GetString(5),
                        });
                    }
                }

                // Foreign keys only improve join suggestions; never fail the whole load over them
                try
                {
                    foreignKeys = await LoadForeignKeysAsync(sql, tables, cancellationToken).ConfigureAwait(false);
                }
                catch (SqlException ex)
                {
                    Log.Error("Could not read foreign keys; join suggestions will rely on column names only", ex);
                }
            }

            return new DbMetadata(connection.Server, connection.Database, tables, DateTime.Now, stopwatch.Elapsed, foreignKeys, stamp);
        }

        private static async Task<List<ForeignKeyInfo>> LoadForeignKeysAsync(SqlConnection sql, List<TableInfo> tables, CancellationToken cancellationToken)
        {
            var byName = tables.ToDictionary(t => t.Schema + "\u0001" + t.Name);
            var result = new List<ForeignKeyInfo>();

            using (var cmd = new SqlCommand(ForeignKeyQuery, sql) { CommandTimeout = 60 })
            using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                var currentId = -1;
                ForeignKeyInfo current = null;
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var id = reader.GetInt32(0);
                    if (id != currentId)
                    {
                        currentId = id;
                        current = null;

                        // Skip keys whose tables we didn't load (e.g. filtered out as system objects)
                        if (byName.TryGetValue(reader.GetString(2) + "\u0001" + reader.GetString(3), out var parent) &&
                            byName.TryGetValue(reader.GetString(4) + "\u0001" + reader.GetString(5), out var referenced))
                        {
                            current = new ForeignKeyInfo(reader.GetString(1), parent, referenced);
                            result.Add(current);
                        }
                    }

                    current?.Columns.Add((reader.GetString(6), reader.GetString(7)));
                }
            }
            return result;
        }
    }
}
