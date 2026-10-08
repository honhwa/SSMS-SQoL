using System.Collections.Generic;
using System.Linq;
using System.Text;
using SsmsSqlHelper.Metadata;

namespace SsmsSqlHelper.Generation
{
    /// <summary>Generated text plus where the caret should land, relative to the start of <see cref="Text"/>.</summary>
    internal sealed class GeneratedText
    {
        public GeneratedText(string text, int caretOffset)
        {
            Text = text;
            CaretOffset = caretOffset;
        }

        public string Text { get; }
        public int CaretOffset { get; }
    }

    internal static class SqlGenerator
    {
        private const string Unit = "    ";

        /// <summary>
        /// Text to append after <c>INSERT INTO table</c>: the column list and a VALUES row with a typed
        /// placeholder per column. Identity, computed and rowversion columns are left out.
        /// </summary>
        public static GeneratedText InsertBody(TableInfo table, string indent, string newLine)
        {
            var columns = table.Columns.Where(c => !c.IsGenerated).ToList();
            if (columns.Count == 0)
            {
                var text = newLine + indent + "DEFAULT VALUES";
                return new GeneratedText(text, text.Length);
            }

            var sb = new StringBuilder();
            sb.Append(newLine).Append(indent).Append('(');
            for (var i = 0; i < columns.Count; i++)
            {
                sb.Append(newLine).Append(indent).Append(Unit).Append(SqlIdentifier.Quote(columns[i].Name));
                if (i < columns.Count - 1)
                    sb.Append(',');
            }
            sb.Append(newLine).Append(indent).Append(')');
            sb.Append(newLine).Append(indent).Append("VALUES");
            sb.Append(newLine).Append(indent).Append('(');

            var values = columns.Select((c, i) => Placeholder(c) + (i < columns.Count - 1 ? "," : "")).ToList();
            var width = values.Max(v => v.Length);
            var caret = -1;
            for (var i = 0; i < columns.Count; i++)
            {
                sb.Append(newLine).Append(indent).Append(Unit);
                if (caret < 0)
                    caret = sb.Length + CaretInsidePlaceholder(values[i]);
                sb.Append(values[i].PadRight(width)).Append(" -- ").Append(Describe(columns[i], includeName: true));
            }
            sb.Append(newLine).Append(indent).Append(')');
            return new GeneratedText(sb.ToString(), caret);
        }

        /// <summary>
        /// Text to append after <c>UPDATE table</c>: a SET line per non-key column and a WHERE on the
        /// primary key. Without a primary key, WHERE is left empty so the statement can't run as-is
        /// and update every row by accident.
        /// </summary>
        public static GeneratedText UpdateBody(TableInfo table, string indent, string newLine)
        {
            var columns = table.Columns.Where(c => !c.IsGenerated && !c.IsPrimaryKey).ToList();
            if (columns.Count == 0)
                return null;

            var assignments = columns.Select((c, i) =>
                SqlIdentifier.Quote(c.Name) + " = " + Placeholder(c) + (i < columns.Count - 1 ? "," : "")).ToList();
            var width = assignments.Max(a => a.Length);

            var sb = new StringBuilder();
            var caret = -1;
            for (var i = 0; i < columns.Count; i++)
            {
                sb.Append(newLine).Append(indent).Append(i == 0 ? "SET " : Unit);
                if (caret < 0)
                {
                    var valueStart = assignments[i].IndexOf(" = ", System.StringComparison.Ordinal) + 3;
                    caret = sb.Length + valueStart + CaretInsidePlaceholder(assignments[i].Substring(valueStart));
                }
                sb.Append(assignments[i].PadRight(width)).Append(" -- ").Append(Describe(columns[i], includeName: false));
            }

            sb.Append(newLine).Append(indent).Append("WHERE ");
            var keys = table.Columns.Where(c => c.IsPrimaryKey).ToList();
            if (keys.Count > 0)
            {
                sb.Append(string.Join(" AND ", keys.Select(k => SqlIdentifier.Quote(k.Name) + " = " + Placeholder(k))));
                sb.Append(" -- ").Append(string.Join(", ", keys.Select(k => Describe(k, includeName: keys.Count > 1))));
            }
            return new GeneratedText(sb.ToString(), caret);
        }

        /// <summary>
        /// Replacement for <c>*</c>: one column per line, aligned under the first.
        /// </summary>
        /// <param name="alignment">Whitespace that lines up with the position of the star.</param>
        public static GeneratedText StarColumns(IEnumerable<(TableInfo Table, string Prefix)> sources, string alignment, string newLine)
        {
            var items = sources.SelectMany(s => s.Table.Columns.Select(c =>
                (s.Prefix == null ? "" : s.Prefix + ".") + SqlIdentifier.Quote(c.Name))).ToList();
            return StarColumnNames(items, alignment, newLine);
        }

        public static GeneratedText StarColumnNames(IEnumerable<string> columns, string alignment, string newLine)
        {
            var items = columns.ToList();
            if (items.Count == 0)
                return null;

            var text = string.Join("," + newLine + alignment, items);
            return new GeneratedText(text, text.Length);
        }

        /// <summary>A literal that type-checks for the column, e.g. N'' for nvarchar or 0 for int.</summary>
        public static string Placeholder(ColumnInfo column)
        {
            switch (column.BaseTypeName ?? column.TypeName)
            {
                case "char":
                case "varchar":
                case "text":
                    return "''";
                case "nchar":
                case "nvarchar":
                case "ntext":
                case "sysname":
                case "xml":
                    return "N''";
                case "bit":
                case "tinyint":
                case "smallint":
                case "int":
                case "bigint":
                case "decimal":
                case "numeric":
                case "money":
                case "smallmoney":
                case "float":
                case "real":
                    return "0";
                case "date":
                case "datetime":
                case "datetime2":
                case "smalldatetime":
                case "time":
                    return "GETDATE()";
                case "datetimeoffset":
                    return "SYSDATETIMEOFFSET()";
                case "uniqueidentifier":
                    return "NEWID()";
                case "binary":
                case "varbinary":
                case "image":
                    return "0x";
                default:
                    return "NULL";
            }
        }

        private static string Describe(ColumnInfo c, bool includeName)
        {
            var text = (includeName ? c.Name + " " : "") + c.DisplayType + (c.IsNullable ? " NULL" : " NOT NULL");
            return c.HasDefault ? text + " (has default)" : text;
        }

        // Put the caret between the quotes of '' / N'' so the user can type straight away
        private static int CaretInsidePlaceholder(string value) =>
            value.StartsWith("N'") ? 2 : value.StartsWith("'") ? 1 : 0;
    }
}
