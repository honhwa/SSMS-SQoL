namespace SsmsSqlHelper.Metadata
{
    internal sealed class ColumnInfo
    {
        public string Name { get; set; }
        public int Ordinal { get; set; }
        public string TypeName { get; set; }
        /// <summary>Underlying system type; differs from <see cref="TypeName"/> for user-defined alias types.</summary>
        public string BaseTypeName { get; set; }
        /// <summary>Bytes, as reported by sys.columns; -1 means MAX.</summary>
        public int MaxLength { get; set; }
        public int Precision { get; set; }
        public int Scale { get; set; }
        public bool IsNullable { get; set; }
        public bool IsIdentity { get; set; }
        public bool IsComputed { get; set; }
        public bool HasDefault { get; set; }
        public bool IsPrimaryKey { get; set; }

        public bool IsRowVersion => TypeName == "timestamp" || TypeName == "rowversion";

        /// <summary>True when an INSERT must not (or cannot) supply a value for this column.</summary>
        public bool IsGenerated => IsIdentity || IsComputed || IsRowVersion;

        /// <summary>Type as written in T-SQL, e.g. nvarchar(100), decimal(18,2), varchar(MAX).</summary>
        public string DisplayType
        {
            get
            {
                switch (TypeName)
                {
                    case "nvarchar":
                    case "nchar":
                        return $"{TypeName}({(MaxLength == -1 ? "MAX" : (MaxLength / 2).ToString())})";
                    case "varchar":
                    case "char":
                    case "varbinary":
                    case "binary":
                        return $"{TypeName}({(MaxLength == -1 ? "MAX" : MaxLength.ToString())})";
                    case "decimal":
                    case "numeric":
                        return $"{TypeName}({Precision},{Scale})";
                    case "datetime2":
                    case "datetimeoffset":
                    case "time":
                        return Scale == 7 ? TypeName : $"{TypeName}({Scale})";
                    default:
                        return TypeName;
                }
            }
        }
    }
}
