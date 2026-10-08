using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SsmsSqlHelper.Metadata;

namespace SsmsSqlHelper.Generation
{
    /// <summary>Builds short table aliases from the capital letters of the table name: BudgetLines → bl.</summary>
    internal static class AliasGenerator
    {
        // Reserved in T-SQL but missing from SqlIdentifier's list of words that are common as table names
        private static readonly HashSet<string> ExtraReserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "IF", "GO", "USE", "EXEC", "DECLARE", "OUTPUT", "OPTION", "WINDOW", "APPLY", "PIVOT", "MERGE",
            "USING", "OFFSET", "FETCH", "OUT", "OVER", "TRY", "WHILE", "RETURN", "PRINT", "GOTO", "OPEN", "READ",
        };

        /// <summary>
        /// One letter per word, lower-cased. Words start at a capital letter (PascalCase), after an underscore
        /// or other separator, and at the first letter. Names with no lower-case letters (BUDGET_LINES)
        /// are split on separators only.
        /// </summary>
        public static string FromTableName(string tableName)
        {
            var hasLower = tableName.Any(char.IsLower);
            var alias = new StringBuilder();
            var atWordStart = true;

            foreach (var c in tableName)
            {
                if (!char.IsLetter(c))
                {
                    if (!char.IsDigit(c))
                        atWordStart = true;
                    continue;
                }

                if (atWordStart || (hasLower && char.IsUpper(c)))
                    alias.Append(char.ToLowerInvariant(c));
                atWordStart = false;
            }

            return alias.Length > 0 ? alias.ToString() : "t";
        }

        /// <summary>
        /// The alias for <paramref name="tableName"/> that isn't already in <paramref name="usedNames"/>
        /// (case-insensitive set) or a reserved word: bl, then bl2, bl3, ...
        /// </summary>
        public static string Unique(string tableName, ISet<string> usedNames)
        {
            var baseAlias = FromTableName(tableName);
            var candidate = baseAlias;
            for (var n = 2; IsTaken(candidate, usedNames); n++)
                candidate = baseAlias + n;
            return candidate;
        }

        private static bool IsTaken(string candidate, ISet<string> usedNames) =>
            usedNames.Contains(candidate) || SqlIdentifier.IsReservedWord(candidate) || ExtraReserved.Contains(candidate);
    }
}
