namespace SsmsSqlHelper.Metadata
{
    internal sealed class SchemaObjectInfo
    {
        public SchemaObjectInfo(string schema, string name, string type)
        {
            Schema = schema;
            Name = name;
            Type = type;
        }

        public string Schema { get; }
        public string Name { get; }
        public string Type { get; }
        public string QualifiedName => SqlIdentifier.Quote(Schema) + "." + SqlIdentifier.Quote(Name);
        public string ExplorerType => Type == "U" ? "Table" : Type == "V" ? "View" : IsProcedure ? "StoredProcedure" : "UserDefinedFunction";
        public string ScriptType => Type == "V" ? "VIEW" : IsProcedure ? "PROCEDURE" : "FUNCTION";
        public bool IsTable => Type == "U";
        public bool IsProcedure => Type == "P" || Type == "PC";
        public bool IsSqlModule => Type == "P" || Type == "V" || Type == "FN" || Type == "IF" || Type == "TF";
        public override string ToString() => Schema + "." + Name;
    }
}
