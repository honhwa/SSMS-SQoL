using System.Collections.Generic;

namespace SsmsSqlHelper.Metadata
{
    internal sealed class TableInfo
    {
        public TableInfo(string schema, string name, bool isView)
        {
            Schema = schema;
            Name = name;
            IsView = isView;
        }

        public string Schema { get; }
        public string Name { get; }
        public bool IsView { get; }
        public List<ColumnInfo> Columns { get; } = new List<ColumnInfo>();

        public string QualifiedName => $"{SqlIdentifier.Quote(Schema)}.{SqlIdentifier.Quote(Name)}";

        public override string ToString() => $"{Schema}.{Name}";
    }
}
