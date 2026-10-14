using System.Collections.Generic;
using System.Linq;

namespace SsmsSqlHelper.Metadata
{
    internal sealed class TableFunctionInfo
    {
        public TableFunctionInfo(string schema, string name, string type, string description)
        {
            Schema = schema;
            Name = name;
            Type = type;
            Description = description;
        }

        public string Schema { get; }
        public string Name { get; }
        public string Type { get; }
        public string Description { get; }
        public string QualifiedName => SqlIdentifier.Quote(Schema) + "." + SqlIdentifier.Quote(Name);
        public List<ProcedureParameterInfo> Parameters { get; } = new List<ProcedureParameterInfo>();
        public string Signature => QualifiedName + "(" + string.Join(", ", Parameters.Select(p => p.Name + " " + p.DisplayType)) + ")";
        public string Kind => Type == "IF" ? "Inline table-valued function" :
            Type == "FT" ? "CLR table-valued function" : "Table-valued function";
    }
}
