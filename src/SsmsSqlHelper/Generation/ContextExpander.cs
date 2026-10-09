using System;
using System.Collections.Generic;
using System.Linq;
using SsmsSqlHelper.Metadata;
using SsmsSqlHelper.Parsing;

namespace SsmsSqlHelper.Generation
{
    internal sealed class TextEdit
    {
        public TextEdit(int start, int length, string newText, int caretOffset, bool offerJoinConditions = false)
        {
            Start = start;
            Length = length;
            NewText = newText;
            CaretOffset = caretOffset;
            OfferJoinConditions = offerJoinConditions;
        }

        public int Start { get; }
        public int Length { get; }
        public string NewText { get; }
        /// <summary>Caret position after the edit, relative to <see cref="Start"/>.</summary>
        public int CaretOffset { get; }

        /// <summary>The edit ends at an empty <c>ON </c> with several possible conditions: show the list.</summary>
        public bool OfferJoinConditions { get; }
    }

    /// <summary>
    /// Tab expansions that need the database schema: INSERT column lists, UPDATE SET lists and SELECT * expansion.
    /// Pure text-in/edit-out so it can be unit tested.
    /// </summary>
    internal static class ContextExpander
    {
        internal sealed class StarColumn
        {
            public StarColumn(TableInfo table, string prefix, ColumnInfo column)
            {
                Table = table;
                Prefix = prefix;
                Column = column;
            }

            public TableInfo Table { get; }
            public string Prefix { get; }
            public ColumnInfo Column { get; }
            public string SqlName => (Prefix == null ? "" : Prefix + ".") + SqlIdentifier.Quote(Column.Name);
        }

        /// <param name="addAliases">False when the user turned off automatic aliases (and the join conditions that come with them).</param>
        public static TextEdit TryExpand(string text, int caret, string newLine, DbMetadata metadata, bool addAliases = true)
        {
            if (metadata == null)
                return null;

            var context = SqlContext.GetTabContext(text, caret);
            if (context == null)
                return null;

            switch (context.Kind)
            {
                case TabContextKind.InsertTable:
                case TabContextKind.UpdateTable:
                    return ExpandTableStatement(text, context, newLine, metadata);
                case TabContextKind.SelectStar:
                    return ExpandStar(text, context, newLine, metadata);
                case TabContextKind.SourceTable:
                    return addAliases ? AddAlias(text, context, metadata) : null;
                case TabContextKind.ExecuteProcedure:
                    return ExpandProcedure(text, context, newLine, metadata);
                default:
                    return null;
            }
        }

        private static TextEdit ExpandProcedure(string text, TabContext context, string newLine, DbMetadata metadata)
        {
            var procedure = metadata.FindProcedure(context.Table.Text);
            if (procedure == null || procedure.Parameters.Count == 0)
                return null;

            var indent = LineIndent(text, context.StatementStart);
            var original = text.Substring(context.StatementStart, context.Table.End - context.StatementStart);
            var generated = SqlGenerator.ExecuteBody(procedure, original, indent, newLine);
            if (generated == null)
                return null;

            // OUTPUT parameters need declarations before EXEC; otherwise edit only the gap after its name.
            if (procedure.Parameters.Any(p => p.IsOutput || p.IsReadOnly))
                return new TextEdit(context.StatementStart, context.ReplaceEnd - context.StatementStart,
                    generated.Text, generated.CaretOffset);

            var suffix = generated.Text.Substring(original.Length);
            return new TextEdit(context.ReplaceStart, context.ReplaceEnd - context.ReplaceStart,
                suffix, generated.CaretOffset - original.Length);
        }

        private static TextEdit ExpandTableStatement(string text, TabContext context, string newLine, DbMetadata metadata)
        {
            var table = metadata.FindTable(context.Table.Text);
            if (table == null)
                return null;

            var indent = LineIndent(text, context.Table.Start);
            var generated = context.Kind == TabContextKind.InsertTable
                ? SqlGenerator.InsertBody(table, indent, newLine)
                : SqlGenerator.UpdateBody(table, indent, newLine);
            if (generated == null)
                return null;

            return new TextEdit(context.ReplaceStart, context.ReplaceEnd - context.ReplaceStart, generated.Text, generated.CaretOffset);
        }

        private static TextEdit AddAlias(string text, TabContext context, DbMetadata metadata)
        {
            var table = metadata.FindTable(context.Table.Text);
            if (table == null)
                return null;

            var used = SqlContext.GetNamesInScope(text, context.Table.Start, context.Table.Start);
            var alias = AliasGenerator.Unique(table.Name, used);
            var insert = " " + alias;
            var offerList = false;

            if (context.AppendOn)
            {
                var candidates = JoinSuggester.Suggest(metadata, table, SqlIdentifier.Quote(alias), JoinTablesBefore(text, context.Table.Start, metadata));

                // A single declared foreign key is written straight away; guesses from column names never are
                // (they would show up next to it and turn every join into a list). Several keys, or only guesses,
                // are shown as a list to choose from.
                var foreignKeys = candidates.Where(c => c.IsForeignKey).ToList();
                if (foreignKeys.Count == 1)
                {
                    insert += " ON " + foreignKeys[0].Condition;
                }
                else if (candidates.Count > 0)
                {
                    insert += " ON ";
                    offerList = true;
                }
            }

            return new TextEdit(context.ReplaceStart, context.ReplaceEnd - context.ReplaceStart, insert, insert.Length, offerList);
        }

        /// <summary>Tables to the left of the one starting at <paramref name="nameStart"/> that exist in the database, with how to qualify their columns.</summary>
        internal static List<JoinScopeTable> JoinTablesBefore(string text, int nameStart, DbMetadata metadata) =>
            ResolveScope(SqlContext.GetScopeTables(text, nameStart).Where(r => r.Name.Start < nameStart), metadata);

        /// <summary>Resolves references to database tables; derived tables, CTEs and unknown names are dropped.</summary>
        internal static List<JoinScopeTable> ResolveScope(IEnumerable<TableReference> references, DbMetadata metadata) =>
            references
                .Select(r => (Reference: r, Table: metadata.FindTable(r.Name.Text)))
                .Where(x => x.Table != null)
                .Select(x => new JoinScopeTable(x.Table, QualifierOf(x.Reference, x.Table)))
                .ToList();

        internal static string QualifierOf(TableReference reference, TableInfo table) =>
            SqlIdentifier.Quote(reference.Alias ?? table.Name);

        /// <summary>Returns the columns that SELECT * would expand to, in source and ordinal order.</summary>
        public static IReadOnlyList<StarColumn> GetStarColumns(TabContext context, DbMetadata metadata)
        {
            if (context == null || context.Kind != TabContextKind.SelectStar || metadata == null)
                return null;

            var refs = context.FromTables;
            if (context.StarQualifier != null)
            {
                var match = refs.FirstOrDefault(r => string.Equals(r.Alias, context.StarQualifier, StringComparison.OrdinalIgnoreCase))
                            ?? refs.FirstOrDefault(r => r.Alias == null && string.Equals(LastPart(r.Name.Text), context.StarQualifier, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                    return null;
                refs = new[] { match };
            }

            var usePrefix = context.StarQualifier != null || refs.Count > 1 || refs.Any(r => r.Alias != null);
            var columns = new List<StarColumn>();
            foreach (var r in refs)
            {
                // Expanding with a table missing would silently drop its columns, so give up instead
                var table = metadata.FindTable(r.Name.Text);
                if (table == null)
                    return null;

                string prefix = null;
                if (usePrefix)
                    prefix = context.StarQualifier ?? (r.Alias != null ? SqlIdentifier.Quote(r.Alias) : SqlIdentifier.Quote(table.Name));
                columns.AddRange(table.Columns.Select(column => new StarColumn(table, prefix, column)));
            }

            return columns.Count > 0 ? columns : null;
        }

        /// <summary>Builds the replacement for the columns selected in the picker.</summary>
        public static TextEdit ExpandSelectedStar(string text, TabContext context, string newLine,
            IReadOnlyList<StarColumn> selected)
        {
            if (context == null || context.Kind != TabContextKind.SelectStar || selected == null || selected.Count == 0)
                return null;

            var generated = SqlGenerator.StarColumnNames(selected.Select(c => c.SqlName), Alignment(text, context.ReplaceStart), newLine);
            if (generated == null)
                return null;

            return new TextEdit(context.ReplaceStart, context.ReplaceEnd - context.ReplaceStart, generated.Text, generated.CaretOffset);
        }

        private static TextEdit ExpandStar(string text, TabContext context, string newLine, DbMetadata metadata) =>
            ExpandSelectedStar(text, context, newLine, GetStarColumns(context, metadata));

        private static string LastPart(string name)
        {
            var parts = SqlIdentifier.Split(name);
            return parts[parts.Count - 1];
        }

        private static int LineStart(string text, int position)
        {
            var i = Math.Min(position, text.Length);
            while (i > 0 && text[i - 1] != '\n' && text[i - 1] != '\r')
                i--;
            return i;
        }

        private static string LineIndent(string text, int position)
        {
            var start = LineStart(text, position);
            var i = start;
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t'))
                i++;
            return text.Substring(start, i - start);
        }

        /// <summary>Whitespace reaching the same column as <paramref name="position"/>; tabs are kept so alignment survives.</summary>
        private static string Alignment(string text, int position)
        {
            var start = LineStart(text, position);
            var chars = text.Substring(start, position - start).Select(c => c == '\t' ? '\t' : ' ').ToArray();
            return new string(chars);
        }
    }
}
