using System;
using System.Collections.Generic;
using System.Linq;
using SsmsSqlHelper.Metadata;
using SsmsSqlHelper.Parsing;

namespace SsmsSqlHelper.Generation
{
    internal sealed class ColumnSuggestion
    {
        public ColumnSuggestion(TableInfo table, ColumnInfo column, string qualifier, string insertText, int tableOrder, bool isQualified = false)
        {
            IsQualified = isQualified;
            Table = table;
            Column = column;
            Qualifier = qualifier;
            InsertText = insertText;
            TableOrder = tableOrder;
        }

        public TableInfo Table { get; }
        public ColumnInfo Column { get; }

        /// <summary>How the table is referred to in the query (alias, else table name), already quoted; null when not shown.</summary>
        public string Qualifier { get; }

        /// <summary>What replaces the typed word: <c>Name</c> or, when the query has several tables or an alias, <c>b.Name</c>.</summary>
        public string InsertText { get; }

        /// <summary>True when <see cref="InsertText"/> starts with the alias or table name.</summary>
        public bool IsQualified { get; }

        /// <summary>Position of the table in the query, so columns stay grouped by table.</summary>
        public int TableOrder { get; }
    }

    /// <summary>Works out which columns to offer for a <see cref="ColumnContext"/>. Pure: no editor types.</summary>
    internal static class ColumnSuggester
    {
        public static List<ColumnSuggestion> Suggest(ColumnContext context, DbMetadata metadata)
        {
            switch (context.Kind)
            {
                case ColumnContextKind.Qualified:
                    return Qualified(context, metadata);
                case ColumnContextKind.InsertList:
                    return InsertList(context, metadata);
                default:
                    return Expression(context, metadata);
            }
        }

        private static List<ColumnSuggestion> Qualified(ColumnContext context, DbMetadata metadata)
        {
            var reference = FindReference(context.Qualifier, context.Tables);
            var table = reference == null ? null : metadata.FindTable(reference.Name.Text);
            if (table == null)
                return new List<ColumnSuggestion>();

            // The user already typed the qualifier, so only the column name is inserted
            return table.Columns.Select(c => new ColumnSuggestion(table, c, null, SqlIdentifier.Quote(c.Name), 0)).ToList();
        }

        private static List<ColumnSuggestion> Expression(ColumnContext context, DbMetadata metadata)
        {
            var resolved = context.Tables
                .Select(r => (Reference: r, Table: metadata.FindTable(r.Name.Text)))
                .Where(x => x.Table != null)
                .ToList();

            // Name the table as soon as the query does: with several tables to tell columns apart, and with an alias
            // so a column in WHERE reads like the ones in the select list (b.Name) even when there is only one table
            var qualify = context.Tables.Count > 1 || context.Tables.Any(r => r.Alias != null);
            var result = new List<ColumnSuggestion>();
            for (var i = 0; i < resolved.Count; i++)
            {
                var (reference, table) = resolved[i];
                var qualifier = SqlIdentifier.Quote(reference.Alias ?? table.Name);
                foreach (var column in table.Columns)
                {
                    var name = SqlIdentifier.Quote(column.Name);
                    result.Add(new ColumnSuggestion(table, column, qualifier, qualify ? qualifier + "." + name : name, i, qualify));
                }
            }
            return result;
        }

        private static List<ColumnSuggestion> InsertList(ColumnContext context, DbMetadata metadata)
        {
            var table = metadata.FindTable(context.InsertTarget.Text);
            if (table == null)
                return new List<ColumnSuggestion>();

            // Identity, computed and rowversion columns can't be inserted into
            return table.Columns.Where(c => !c.IsGenerated)
                .Select(c => new ColumnSuggestion(table, c, null, SqlIdentifier.Quote(c.Name), 0))
                .ToList();
        }

        /// <summary>Finds the table a qualifier like <c>b</c>, <c>Budgets</c> or <c>dbo.Budgets</c> stands for: an alias first, then a table name.</summary>
        internal static TableReference FindReference(string qualifier, IEnumerable<TableReference> tables)
        {
            var list = tables.ToList();
            var wanted = SqlIdentifier.Split(qualifier);

            var byAlias = list.FirstOrDefault(r => r.Alias != null && wanted.Count == 1 &&
                                                   string.Equals(r.Alias, wanted[0], StringComparison.OrdinalIgnoreCase));
            if (byAlias != null)
                return byAlias;

            // Without an alias the table name is the qualifier; "dbo.Budgets" must match schema and name, "Budgets" just the name
            return list.FirstOrDefault(r =>
            {
                if (r.Alias != null)
                    return false;

                var parts = SqlIdentifier.Split(r.Name.Text);
                if (wanted.Count > parts.Count)
                    return false;
                for (var i = 1; i <= wanted.Count; i++)
                {
                    if (!string.Equals(wanted[wanted.Count - i], parts[parts.Count - i], StringComparison.OrdinalIgnoreCase))
                        return false;
                }
                return true;
            });
        }

        /// <summary>One-line facts for the tooltip: type, nullability, key role and where a foreign key points.</summary>
        public static string Describe(ColumnSuggestion suggestion, DbMetadata metadata)
        {
            var c = suggestion.Column;
            var parts = new List<string> { c.DisplayType + (c.IsNullable ? " NULL" : " NOT NULL") };
            if (c.IsPrimaryKey)
                parts.Add("primary key");
            if (c.IsIdentity)
                parts.Add("identity");
            if (c.IsComputed)
                parts.Add("computed");
            if (c.HasDefault)
                parts.Add("has default");

            foreach (var fk in metadata.ForeignKeys.Where(f => ReferenceEquals(f.Parent, suggestion.Table)))
            {
                var index = fk.Columns.FindIndex(p => string.Equals(p.ParentColumn, c.Name, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                    parts.Add($"foreign key → {fk.Referenced.Name}({fk.Columns[index].ReferencedColumn})");
            }

            return suggestion.Table.Name + "." + c.Name + "\n" + string.Join(" · ", parts);
        }
    }
}
