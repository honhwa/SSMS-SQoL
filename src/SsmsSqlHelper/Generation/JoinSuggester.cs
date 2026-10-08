using System;
using System.Collections.Generic;
using System.Linq;
using SsmsSqlHelper.Metadata;

namespace SsmsSqlHelper.Generation
{
    /// <summary>A table already in the FROM clause, and how its columns are qualified (alias, or the table name).</summary>
    internal sealed class JoinScopeTable
    {
        public JoinScopeTable(TableInfo table, string qualifier)
        {
            Table = table;
            Qualifier = qualifier;
        }

        public TableInfo Table { get; }
        public string Qualifier { get; }
    }

    internal sealed class JoinCandidate
    {
        public JoinCandidate(string condition, bool isForeignKey, string source, string detail)
        {
            Condition = condition;
            IsForeignKey = isForeignKey;
            Source = source;
            Detail = detail;
        }

        /// <summary>e.g. <c>bl.BudgetId = b.Id</c></summary>
        public string Condition { get; }

        /// <summary>True for a declared FOREIGN KEY; false when guessed from column names.</summary>
        public bool IsForeignKey { get; }

        /// <summary>Short label: the constraint name, or "name match".</summary>
        public string Source { get; }

        /// <summary>One-line explanation, e.g. <c>BudgetLines(BudgetId) → Budgets(Id)</c>.</summary>
        public string Detail { get; }
    }

    /// <summary>
    /// Works out how a table can be joined to the tables before it. Declared foreign keys come first; databases that
    /// don't declare them (most columns here) fall back to the <c>XxxId</c> → <c>Xxx(s).Id</c> naming convention.
    /// </summary>
    internal static class JoinSuggester
    {
        private sealed class Guess
        {
            public string ChildColumn;
            public string ParentColumn;
            /// <summary>1 = the whole stem names the table (BudgetId → Budgets); 2 = only its last words do (TempBudgetId → Budgets).</summary>
            public int Quality;
        }

        /// <summary>
        /// Join conditions between <paramref name="target"/> (the table being joined) and each of <paramref name="others"/>,
        /// target columns on the left. Foreign keys first, then name guesses best-first.
        /// </summary>
        public static List<JoinCandidate> Suggest(DbMetadata metadata, TableInfo target, string targetQualifier, IEnumerable<JoinScopeTable> others)
        {
            var foreignKeys = new List<JoinCandidate>();
            var guesses = new List<(JoinCandidate Candidate, int Quality)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var otherList = others.ToList();

            // Declared keys win, so collect them for all tables first and only then add guesses that don't repeat them
            foreach (var other in otherList)
            {
                foreach (var fk in metadata.ForeignKeysBetween(target, other.Table))
                {
                    if (ReferenceEquals(fk.Parent, target) && ReferenceEquals(fk.Referenced, other.Table))
                        AddForeignKey(foreignKeys, seen, fk, targetQualifier, other.Qualifier, targetIsChild: true);
                    if (ReferenceEquals(fk.Referenced, target) && ReferenceEquals(fk.Parent, other.Table))
                        AddForeignKey(foreignKeys, seen, fk, targetQualifier, other.Qualifier, targetIsChild: false);
                }
            }

            foreach (var other in otherList)
            {
                foreach (var g in GuessJoins(target, other.Table))
                    AddGuess(guesses, seen, g, target, targetQualifier, other, targetIsChild: true);
                foreach (var g in GuessJoins(other.Table, target))
                    AddGuess(guesses, seen, g, target, targetQualifier, other, targetIsChild: false);
            }

            var result = new List<JoinCandidate>(foreignKeys);
            result.AddRange(guesses.OrderBy(g => g.Quality).Select(g => g.Candidate));
            return result;
        }

        /// <summary>True if the two tables are linked by a foreign key or by column naming, in either direction.</summary>
        public static bool AreRelated(DbMetadata metadata, TableInfo a, TableInfo b) =>
            metadata.ForeignKeysBetween(a, b).Any() || GuessJoins(a, b).Any() || GuessJoins(b, a).Any();

        private static void AddForeignKey(List<JoinCandidate> list, HashSet<string> seen, ForeignKeyInfo fk,
            string targetQualifier, string otherQualifier, bool targetIsChild)
        {
            var parts = fk.Columns.Select(c => targetIsChild
                ? Equal(targetQualifier, c.ParentColumn, otherQualifier, c.ReferencedColumn)
                : Equal(targetQualifier, c.ReferencedColumn, otherQualifier, c.ParentColumn));
            var condition = string.Join(" AND ", parts);
            if (!seen.Add(condition))
                return;

            list.Add(new JoinCandidate(condition, true, fk.Name, $"{Describe(fk.Parent, fk.Columns.Select(c => c.ParentColumn))} → {Describe(fk.Referenced, fk.Columns.Select(c => c.ReferencedColumn))}"));
        }

        private static void AddGuess(List<(JoinCandidate, int)> list, HashSet<string> seen, Guess g, TableInfo target,
            string targetQualifier, JoinScopeTable other, bool targetIsChild)
        {
            var condition = targetIsChild
                ? Equal(targetQualifier, g.ChildColumn, other.Qualifier, g.ParentColumn)
                : Equal(targetQualifier, g.ParentColumn, other.Qualifier, g.ChildColumn);
            if (!seen.Add(condition))
                return;

            var child = targetIsChild ? target : other.Table;
            var parent = targetIsChild ? other.Table : target;
            list.Add((new JoinCandidate(condition, false, "name match",
                $"{Describe(child, new[] { g.ChildColumn })} → {Describe(parent, new[] { g.ParentColumn })} (guessed from column name)"), g.Quality));
        }

        private static string Equal(string leftQualifier, string leftColumn, string rightQualifier, string rightColumn) =>
            $"{leftQualifier}.{SqlIdentifier.Quote(leftColumn)} = {rightQualifier}.{SqlIdentifier.Quote(rightColumn)}";

        private static string Describe(TableInfo table, IEnumerable<string> columns) => $"{table.Name}({string.Join(", ", columns)})";

        /// <summary>
        /// Columns of <paramref name="child"/> named like <c>XxxId</c> / <c>xxx_id</c> whose stem names <paramref name="parent"/>
        /// (Xxx, Xxxs, Xxxes or Xxxies), where the parent has a single-column primary key of the same type.
        /// Leading words of the stem may be dropped: TempEditBudgetId still finds Budgets.
        /// </summary>
        private static List<Guess> GuessJoins(TableInfo child, TableInfo parent)
        {
            var result = new List<Guess>();
            var key = SingleKey(parent);
            if (key == null)
                return result;

            foreach (var column in child.Columns)
            {
                var stem = StemOf(column.Name);
                if (stem == null || column.BaseTypeName != key.BaseTypeName)
                    continue;

                // A table's own key column isn't a reference to another row of itself
                if (ReferenceEquals(child, parent) && column.IsPrimaryKey)
                    continue;

                var quality = MatchQuality(stem, parent.Name);
                if (quality > 0)
                    result.Add(new Guess { ChildColumn = column.Name, ParentColumn = key.Name, Quality = quality });
            }
            return result;
        }

        private static ColumnInfo SingleKey(TableInfo table)
        {
            ColumnInfo key = null;
            foreach (var c in table.Columns.Where(c => c.IsPrimaryKey))
            {
                if (key != null)
                    return null;
                key = c;
            }
            return key ?? table.Columns.FirstOrDefault(c => string.Equals(c.Name, "Id", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>"BudgetId" → "Budget", "budget_id" → "budget"; null when the name isn't a reference-style name.</summary>
        private static string StemOf(string columnName)
        {
            if (columnName.Length > 3 && columnName.EndsWith("_id", StringComparison.OrdinalIgnoreCase))
                return columnName.Substring(0, columnName.Length - 3);
            // Capital I so plain "Id" and words like "Paid" don't count
            if (columnName.Length > 2 && columnName.EndsWith("Id", StringComparison.Ordinal))
                return columnName.Substring(0, columnName.Length - 2);
            return null;
        }

        private static int MatchQuality(string stem, string tableName)
        {
            var words = SplitWords(stem);
            for (var skip = 0; skip < words.Count; skip++)
            {
                var candidate = string.Concat(words.Skip(skip));
                if (candidate.Length < 3)
                    break;
                if (NamesTable(candidate, tableName))
                    return skip == 0 ? 1 : 2;
            }
            return 0;
        }

        private static bool NamesTable(string stem, string tableName)
        {
            if (string.Equals(tableName, stem, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tableName, stem + "s", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tableName, stem + "es", StringComparison.OrdinalIgnoreCase))
                return true;

            return stem.EndsWith("y", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(tableName, stem.Substring(0, stem.Length - 1) + "ies", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Splits on underscores and before capital letters: "TempEditBudget" → Temp, Edit, Budget.</summary>
        private static List<string> SplitWords(string stem)
        {
            var words = new List<string>();
            var start = 0;
            for (var i = 1; i < stem.Length; i++)
            {
                if (stem[i] == '_')
                {
                    if (i > start) words.Add(stem.Substring(start, i - start));
                    start = i + 1;
                }
                else if (char.IsUpper(stem[i]) && char.IsLower(stem[i - 1]))
                {
                    words.Add(stem.Substring(start, i - start));
                    start = i;
                }
            }
            if (start < stem.Length)
                words.Add(stem.Substring(start));
            return words;
        }
    }
}
