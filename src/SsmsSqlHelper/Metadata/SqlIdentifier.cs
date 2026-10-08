using System.Collections.Generic;
using System.Text;

namespace SsmsSqlHelper.Metadata
{
    internal static class SqlIdentifier
    {
        /// <summary>Brackets the name only when needed, so common names stay readable.</summary>
        public static string Quote(string name)
        {
            if (IsRegular(name))
                return name;
            return "[" + name.Replace("]", "]]") + "]";
        }

        /// <summary>
        /// Splits a possibly qualified name like <c>dbo.Customer</c>, <c>[my schema].[Order]</c>
        /// or <c>"x"."y"</c> into its unquoted parts.
        /// </summary>
        public static IReadOnlyList<string> Split(string name)
        {
            var parts = new List<string>();
            var current = new StringBuilder();
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (c == '[' || c == '"')
                {
                    var close = c == '[' ? ']' : '"';
                    i++;
                    while (i < name.Length)
                    {
                        if (name[i] == close)
                        {
                            if (i + 1 < name.Length && name[i + 1] == close)
                            {
                                current.Append(close);
                                i += 2;
                                continue;
                            }
                            break;
                        }
                        current.Append(name[i++]);
                    }
                }
                else if (c == '.')
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
                else if (!char.IsWhiteSpace(c))
                {
                    current.Append(c);
                }
            }
            parts.Add(current.ToString());
            return parts;
        }

        public static bool IsReservedWord(string name) => ReservedWords.Contains(name.ToUpperInvariant());

        private static bool IsRegular(string name)
        {
            if (string.IsNullOrEmpty(name) || !(char.IsLetter(name[0]) || name[0] == '_'))
                return false;
            foreach (var c in name)
            {
                if (!(char.IsLetterOrDigit(c) || c == '_'))
                    return false;
            }
            return !ReservedWords.Contains(name.ToUpperInvariant());
        }

        // Common reserved words that show up as table/column names in practice
        private static readonly HashSet<string> ReservedWords = new HashSet<string>
        {
            "ADD", "ALL", "AND", "AS", "ASC", "BETWEEN", "BY", "CASE", "CHECK", "COLUMN", "CONSTRAINT",
            "CREATE", "CROSS", "CURRENT", "DATABASE", "DEFAULT", "DELETE", "DESC", "DISTINCT", "DROP",
            "ELSE", "END", "EXISTS", "FILE", "FOR", "FOREIGN", "FROM", "FULL", "FUNCTION", "GROUP",
            "HAVING", "IN", "INDEX", "INNER", "INSERT", "INTO", "IS", "JOIN", "KEY", "LEFT", "LIKE",
            "NOT", "NULL", "OF", "ON", "OR", "ORDER", "OUTER", "PERCENT", "PLAN", "PRIMARY", "PROCEDURE",
            "PUBLIC", "REFERENCES", "RIGHT", "RULE", "SCHEMA", "SELECT", "SET", "TABLE", "THEN", "TO",
            "TOP", "TRAN", "TRANSACTION", "TRIGGER", "UNION", "UNIQUE", "UPDATE", "USER", "VALUES",
            "VIEW", "WHEN", "WHERE", "WITH"
        };
    }
}
