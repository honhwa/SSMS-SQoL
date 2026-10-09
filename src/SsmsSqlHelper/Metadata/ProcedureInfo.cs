using System.Collections.Generic;

namespace SsmsSqlHelper.Metadata
{
    internal sealed class ProcedureInfo
    {
        public ProcedureInfo(string schema, string name)
        {
            Schema = schema;
            Name = name;
        }

        public string Schema { get; }
        public string Name { get; }
        public string QualifiedName => SqlIdentifier.Quote(Schema) + "." + SqlIdentifier.Quote(Name);
        public List<ProcedureParameterInfo> Parameters { get; } = new List<ProcedureParameterInfo>();

        public override string ToString() => Schema + "." + Name;
    }

    internal sealed class ProcedureParameterInfo
    {
        public string Name { get; set; }
        public string TypeName { get; set; }
        public string TypeSchema { get; set; }
        public bool IsOutput { get; set; }
        public bool IsReadOnly { get; set; }
        public int MaxLength { get; set; }
        public int Precision { get; set; }
        public int Scale { get; set; }

        public string DisplayType => TypeSchema == "sys"
            ? new ColumnInfo { TypeName = TypeName, MaxLength = MaxLength, Precision = Precision, Scale = Scale }.DisplayType
            : SqlIdentifier.Quote(TypeSchema) + "." + SqlIdentifier.Quote(TypeName);
    }
}
