using System.Collections.Generic;

namespace SsmsSqlHelper.Metadata
{
    internal sealed class ForeignKeyInfo
    {
        public ForeignKeyInfo(string name, TableInfo parent, TableInfo referenced)
        {
            Name = name;
            Parent = parent;
            Referenced = referenced;
        }

        public string Name { get; }

        /// <summary>The table that holds the foreign key column(s), e.g. BudgetLines.</summary>
        public TableInfo Parent { get; }

        /// <summary>The table the key points at, e.g. Budgets.</summary>
        public TableInfo Referenced { get; }

        /// <summary>Column pairs in key order; more than one for a composite key.</summary>
        public List<(string ParentColumn, string ReferencedColumn)> Columns { get; } = new List<(string, string)>();
    }
}
