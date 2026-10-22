using System;

namespace SsmsSqlHelper.Metadata
{
    /// <summary>A synonym's local name and the safely quoted catalog containing its target.</summary>
    internal sealed class SynonymInfo
    {
        public SynonymInfo(string schema, string name, string baseObjectName)
        {
            Schema = schema;
            Name = name;
            BaseObjectName = baseObjectName;
            var parts = SqlIdentifier.Split(baseObjectName);
            TargetName = parts[parts.Count - 1];
            TargetSchema = parts.Count >= 2 && parts[parts.Count - 2].Length > 0 ? parts[parts.Count - 2] : "dbo";
            Database = parts.Count >= 3 ? parts[parts.Count - 3] : "";
            Server = parts.Count >= 4 ? parts[parts.Count - 4] : "";
        }

        public string Schema { get; }
        public string Name { get; }
        public string BaseObjectName { get; }
        public string TargetSchema { get; }
        public string TargetName { get; }
        public string Server { get; }
        public string Database { get; }

        public bool IsLocal(string currentDatabase) => Server.Length == 0 &&
            (Database.Length == 0 || string.Equals(Database, currentDatabase, StringComparison.OrdinalIgnoreCase));

        public string CatalogPrefix => Server.Length > 0
            ? Quote(Server) + "." + (Database.Length == 0 ? "" : Quote(Database)) + "."
            : Database.Length == 0 ? "" : Quote(Database) + ".";

        public TableInfo Bind(TableInfo target)
        {
            var table = new TableInfo(Schema, Name, target.IsView, BaseObjectName);
            table.Columns.AddRange(target.Columns);
            return table;
        }

        private static string Quote(string part) => "[" + part.Replace("]", "]]") + "]";
    }
}
