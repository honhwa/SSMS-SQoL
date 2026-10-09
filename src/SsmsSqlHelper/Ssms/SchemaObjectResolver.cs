using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SsmsSqlHelper.Metadata;

namespace SsmsSqlHelper.Ssms
{
    internal static class SchemaObjectResolver
    {
        public static async Task<SchemaObjectInfo> FindAsync(ActiveConnection connection, string name)
        {
            var parts = SqlIdentifier.Split(name.Trim());
            if (parts.Count == 0 || parts.Count > 4)
                return null;
            if (parts.Count >= 3 && parts[parts.Count - 3].Length > 0 &&
                !string.Equals(parts[parts.Count - 3], connection.Database, StringComparison.OrdinalIgnoreCase))
                return null;
            if (parts.Count == 4 && parts[0].Length > 0 &&
                !ServerNameMatcher.Matches(parts[0], connection.Server))
                return null;
            var schema = parts.Count >= 2 ? parts[parts.Count - 2] : null;
            if (schema != null && schema.Length == 0)
                schema = "dbo";

            using (var sql = connection.CreateSqlConnection())
            {
                await sql.OpenAsync().ConfigureAwait(false);
                using (var cmd = sql.CreateCommand())
                {
                    cmd.CommandText = @"SELECT TOP (2) s.name, o.name, o.type
FROM sys.objects o JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE o.name = @name AND (@schema IS NULL OR s.name = @schema)
  AND o.type IN ('U', 'V', 'P', 'PC', 'FN', 'IF', 'TF', 'FS', 'FT') AND o.is_ms_shipped = 0
ORDER BY CASE WHEN s.name = 'dbo' THEN 0 ELSE 1 END, s.name";
                    cmd.Parameters.Add(new SqlParameter("@name", parts[parts.Count - 1]));
                    cmd.Parameters.Add(new SqlParameter("@schema", (object)schema ?? DBNull.Value));
                    using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        if (!await reader.ReadAsync().ConfigureAwait(false))
                            return null;
                        var result = new SchemaObjectInfo(reader.GetString(0), reader.GetString(1), reader.GetString(2).Trim());
                        if (schema == null && result.Schema != "dbo" && await reader.ReadAsync().ConfigureAwait(false))
                            return null; // ambiguous unqualified name
                        return result;
                    }
                }
            }
        }
    }
}
