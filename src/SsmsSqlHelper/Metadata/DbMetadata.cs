using System;
using System.Collections.Generic;
using System.Linq;

namespace SsmsSqlHelper.Metadata
{
    /// <summary>Immutable snapshot of tables, views, columns and foreign keys for one database.</summary>
    internal sealed class DbMetadata
    {
        private static readonly IReadOnlyList<ForeignKeyInfo> NoForeignKeys = new ForeignKeyInfo[0];

        private readonly Dictionary<string, TableInfo> _byQualifiedName;
        private readonly ILookup<string, TableInfo> _byName;
        private readonly ILookup<TableInfo, ForeignKeyInfo> _fkByParent;
        private readonly Dictionary<string, ProcedureInfo> _proceduresByQualifiedName;
        private readonly ILookup<string, ProcedureInfo> _proceduresByName;

        public DbMetadata(string server, string database, IReadOnlyList<TableInfo> tables, DateTime loadedAt, TimeSpan loadDuration,
            IReadOnlyList<ForeignKeyInfo> foreignKeys = null, string schemaStamp = null,
            IReadOnlyList<ProcedureInfo> procedures = null)
        {
            SchemaStamp = schemaStamp;
            Server = server;
            Database = database;
            Tables = tables;
            LoadedAt = loadedAt;
            LoadDuration = loadDuration;
            ForeignKeys = foreignKeys ?? NoForeignKeys;
            Procedures = procedures ?? new ProcedureInfo[0];
            _byQualifiedName = tables.ToDictionary(t => Key(t.Schema, t.Name), StringComparer.OrdinalIgnoreCase);
            _byName = tables.ToLookup(t => t.Name, StringComparer.OrdinalIgnoreCase);
            _fkByParent = ForeignKeys.ToLookup(f => f.Parent);
            _proceduresByQualifiedName = Procedures.ToDictionary(p => Key(p.Schema, p.Name), StringComparer.OrdinalIgnoreCase);
            _proceduresByName = Procedures.ToLookup(p => p.Name, StringComparer.OrdinalIgnoreCase);
        }

        public string Server { get; }
        public string Database { get; }
        public IReadOnlyList<TableInfo> Tables { get; }
        public IReadOnlyList<ForeignKeyInfo> ForeignKeys { get; }
        public IReadOnlyList<ProcedureInfo> Procedures { get; }
        /// <summary>Fingerprint of the schema when this snapshot was read (see <see cref="MetadataLoader.ReadStampAsync"/>); null if unknown.</summary>
        public string SchemaStamp { get; }
        public DateTime LoadedAt { get; }
        public TimeSpan LoadDuration { get; }

        public int ColumnCount => Tables.Sum(t => t.Columns.Count);

        /// <summary>Foreign keys running from <paramref name="a"/> to <paramref name="b"/> or the other way round.</summary>
        public IEnumerable<ForeignKeyInfo> ForeignKeysBetween(TableInfo a, TableInfo b)
        {
            var forward = _fkByParent[a].Where(f => f.Referenced == b);
            return ReferenceEquals(a, b) ? forward : forward.Concat(_fkByParent[b].Where(f => f.Referenced == a));
        }

        /// <summary>
        /// Resolves <c>Customer</c>, <c>dbo.Customer</c>, <c>[dbo].[Customer]</c> or <c>db.dbo.Customer</c>.
        /// An unqualified name prefers the dbo schema, then any schema if the name is unique.
        /// </summary>
        public TableInfo FindTable(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var parts = SqlIdentifier.Split(name.Trim());
            var table = parts[parts.Count - 1];
            if (parts.Count >= 2)
            {
                var schema = parts[parts.Count - 2];
                return _byQualifiedName.TryGetValue(Key(schema.Length == 0 ? "dbo" : schema, table), out var t) ? t : null;
            }

            if (_byQualifiedName.TryGetValue(Key("dbo", table), out var dbo))
                return dbo;

            var matches = _byName[table].Take(2).ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        public ProcedureInfo FindProcedure(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var parts = SqlIdentifier.Split(name.Trim());
            var procedure = parts[parts.Count - 1];
            if (parts.Count >= 3 && parts[parts.Count - 3].Length > 0 &&
                !string.Equals(parts[parts.Count - 3], Database, StringComparison.OrdinalIgnoreCase))
                return null;
            if (parts.Count >= 4 && parts[parts.Count - 4].Length > 0 &&
                !string.Equals(parts[parts.Count - 4], Server, StringComparison.OrdinalIgnoreCase))
                return null;
            if (parts.Count >= 2)
            {
                var schema = parts[parts.Count - 2];
                return _proceduresByQualifiedName.TryGetValue(Key(schema.Length == 0 ? "dbo" : schema, procedure), out var p) ? p : null;
            }

            if (_proceduresByQualifiedName.TryGetValue(Key("dbo", procedure), out var dbo))
                return dbo;

            var matches = _proceduresByName[procedure].Take(2).ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        private static string Key(string schema, string name) => schema + "\u0001" + name;
    }
}
