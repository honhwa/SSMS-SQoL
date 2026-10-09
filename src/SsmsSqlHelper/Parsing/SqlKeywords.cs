using System.Collections.Generic;

namespace SsmsSqlHelper.Parsing
{
    /// <summary>A keyword, clause or operator that can follow what has been typed.</summary>
    internal sealed class KeywordSuggestion
    {
        public KeywordSuggestion(string text, string description, string insertText = null)
        {
            Text = text;
            Description = description;
            InsertText = insertText ?? text + " ";
        }

        /// <summary>As shown in the list: <c>GROUP BY</c>.</summary>
        public string Text { get; }

        public string Description { get; }

        /// <summary>What is written when it is picked; normally the text plus a space so typing can go on.</summary>
        public string InsertText { get; }
    }

    /// <summary>The words offered, grouped by the situation they follow. Order is the order of likelihood.</summary>
    internal static class SqlKeywords
    {
        private static KeywordSuggestion K(string text, string description, string insert = null) => new KeywordSuggestion(text, description, insert);

        private static KeywordSuggestion F(string name)
        {
            var signature = SqlFunctionSignatures.Find(name);
            return K(name + "(", signature.Syntax + " — " + signature.Description, name + "(");
        }

        // ---- single entries ----
        private static KeywordSuggestion Where => K("WHERE", "Keep only the rows that match a condition");
        private static KeywordSuggestion GroupBy => K("GROUP BY", "Combine rows with the same values");
        private static KeywordSuggestion OrderBy => K("ORDER BY", "Sort the result");
        private static KeywordSuggestion Having => K("HAVING", "Filter the groups");
        private static KeywordSuggestion Union => K("UNION", "Add the rows of another query (no duplicates)");
        private static KeywordSuggestion UnionAll => K("UNION ALL", "Add the rows of another query (keep duplicates)");
        private static KeywordSuggestion Except => K("EXCEPT", "Rows of this query that the next one does not return");
        private static KeywordSuggestion Intersect => K("INTERSECT", "Rows that both queries return");
        private static KeywordSuggestion And => K("AND", "Both conditions must hold");
        private static KeywordSuggestion Or => K("OR", "Either condition may hold");
        private static KeywordSuggestion Not => K("NOT", "Negate the condition");
        private static KeywordSuggestion Exists => K("EXISTS", "True when a subquery returns any row");
        private static KeywordSuggestion Null => K("NULL", "No value");
        private static KeywordSuggestion NotNull => K("NOT NULL", "Has a value");
        private static KeywordSuggestion On => K("ON", "The condition that links the joined table");
        private static KeywordSuggestion NoLock => K("WITH (NOLOCK)", "Read without waiting for locks (may read uncommitted data)");
        private static KeywordSuggestion From => K("FROM", "Choose the tables to read");
        private static KeywordSuggestion As => K("AS", "Give it a name");
        private static KeywordSuggestion Join => K("JOIN", "Inner join");

        // ---- lists by situation ----

        public static List<KeywordSuggestion> StatementStart() => new List<KeywordSuggestion>
        {
            K("SELECT", "Read rows"),
            K("INSERT INTO", "Add rows"),
            K("UPDATE", "Change rows"),
            K("DELETE FROM", "Remove rows"),
            K("WITH", "Start with a common table expression"),
            K("EXEC", "Run a stored procedure"),
            K("DECLARE", "Declare a variable"),
            K("SET", "Set a variable or option"),
            K("IF", "Run something only when a condition holds"),
            K("BEGIN TRANSACTION", "Start a transaction"),
            K("COMMIT TRANSACTION", "Save the transaction"),
            K("ROLLBACK TRANSACTION", "Undo the transaction"),
            K("TRUNCATE TABLE", "Empty a table"),
            K("CREATE TABLE", "Create a table"),
            K("ALTER TABLE", "Change a table"),
            K("DROP TABLE", "Delete a table"),
            K("USE", "Switch database"),
        };

        public static List<KeywordSuggestion> SelectStart()
        {
            var list = new List<KeywordSuggestion>
            {
                K("DISTINCT", "Remove duplicate rows"),
                K("TOP", "Return only the first rows"),
                K("ALL", "Keep duplicate rows (the default)"),
                K("CASE", "Choose a value by condition"),
            };
            list.AddRange(SelectFunctions());
            return list;
        }

        public static List<KeywordSuggestion> AfterDistinct()
        {
            var list = new List<KeywordSuggestion> { K("TOP", "Return only the first rows"), K("CASE", "Choose a value by condition") };
            list.AddRange(SelectFunctions());
            return list;
        }

        public static List<KeywordSuggestion> SelectFunctions() => new List<KeywordSuggestion>
        {
            K("GETDATE()", "Current date and time", "GETDATE()"),
            K("GETUTCDATE()", "Current UTC date and time", "GETUTCDATE()"),
            K("SYSDATETIME()", "Current date and time with higher precision", "SYSDATETIME()"),
            K("NEWID()", "New uniqueidentifier", "NEWID()"),
            F("ISNULL"), F("COALESCE"), F("NULLIF"), F("IIF"),
            F("CAST"), F("TRY_CAST"), F("CONVERT"), F("TRY_CONVERT"),
            F("COUNT"), F("SUM"), F("AVG"), F("DATEADD"), F("DATEDIFF"),
            F("SUBSTRING"), F("LEN"), F("CONCAT"), F("ROUND"),
        };

        public static List<KeywordSuggestion> AfterSelectItem() => new List<KeywordSuggestion> { From, As };

        public static List<KeywordSuggestion> ToFrom() => new List<KeywordSuggestion> { From };

        /// <summary>After a table in FROM of a SELECT: filter, join, group, sort, combine.</summary>
        public static List<KeywordSuggestion> AfterTable() => new List<KeywordSuggestion>
        {
            Where,
            K("INNER JOIN", "Rows that match in both tables"),
            K("LEFT JOIN", "All rows of the left table, matches from the right"),
            K("RIGHT JOIN", "All rows of the right table, matches from the left"),
            K("FULL JOIN", "All rows of both tables"),
            K("CROSS JOIN", "Every combination of rows"),
            K("CROSS APPLY", "Run a function or subquery for each row"),
            K("OUTER APPLY", "Like CROSS APPLY but keep rows without a result"),
            GroupBy, OrderBy, Union, UnionAll, Except, Intersect, NoLock,
        };

        /// <summary>After a table in the FROM of an UPDATE or DELETE: filter or join.</summary>
        public static List<KeywordSuggestion> AfterTableOfChange() => new List<KeywordSuggestion>
        {
            Where,
            K("INNER JOIN", "Rows that match in both tables"),
            K("LEFT JOIN", "All rows of the left table, matches from the right"),
            K("RIGHT JOIN", "All rows of the right table, matches from the left"),
            K("FULL JOIN", "All rows of both tables"),
            K("CROSS JOIN", "Every combination of rows"),
        };

        public static List<KeywordSuggestion> AfterDeleteTable() => new List<KeywordSuggestion> { Where, K("OUTPUT", "Return the deleted rows") };

        public static List<KeywordSuggestion> AfterJoinTable() => new List<KeywordSuggestion> { On, NoLock };

        /// <summary>After a finished ON condition: more conditions, the next join, or the next clause.</summary>
        public static List<KeywordSuggestion> AfterOnCondition()
        {
            var list = new List<KeywordSuggestion>
            {
                And, Or,
                K("INNER JOIN", "Rows that match in both tables"),
                K("LEFT JOIN", "All rows of the left table, matches from the right"),
                K("RIGHT JOIN", "All rows of the right table, matches from the left"),
                K("FULL JOIN", "All rows of both tables"),
                K("CROSS JOIN", "Every combination of rows"),
                Where, GroupBy, OrderBy, Union, UnionAll,
            };
            return list;
        }

        public static List<KeywordSuggestion> AfterWhereCondition() => new List<KeywordSuggestion> { And, Or, GroupBy, OrderBy, Union, UnionAll, Except, Intersect };

        public static List<KeywordSuggestion> AfterHavingCondition() => new List<KeywordSuggestion> { And, Or, OrderBy, Union, UnionAll, Except, Intersect };

        public static List<KeywordSuggestion> AfterGroupItems() => new List<KeywordSuggestion> { Having, OrderBy, Union, UnionAll, Except, Intersect };

        public static List<KeywordSuggestion> AfterOrderItem() => new List<KeywordSuggestion>
        {
            K("ASC", "Smallest first"), K("DESC", "Largest first"), K("OFFSET", "Skip rows (for paging)", "OFFSET 0 ROWS"),
        };

        public static List<KeywordSuggestion> AfterAscDesc() => new List<KeywordSuggestion> { K("OFFSET", "Skip rows (for paging)", "OFFSET 0 ROWS") };

        public static List<KeywordSuggestion> ToBy() => new List<KeywordSuggestion> { K("BY", "") };

        public static List<KeywordSuggestion> ToRows() => new List<KeywordSuggestion> { K("ROWS", "") };

        public static List<KeywordSuggestion> AfterOffset() => new List<KeywordSuggestion> { K("FETCH NEXT", "Take a page of rows", "FETCH NEXT 10 ROWS ONLY") };

        public static List<KeywordSuggestion> ConditionStart() => new List<KeywordSuggestion> { Not, Exists };

        public static List<KeywordSuggestion> AfterNotAtStart() => new List<KeywordSuggestion> { Exists };

        /// <summary>After a value in a condition, before any comparison.</summary>
        public static List<KeywordSuggestion> Operators() => new List<KeywordSuggestion>
        {
            K("=", "Equal to"), K("<>", "Not equal to"), K(">", "Greater than"), K("<", "Less than"),
            K(">=", "Greater than or equal to"), K("<=", "Less than or equal to"),
            K("LIKE", "Matches a pattern (% and _)"), K("NOT LIKE", "Does not match a pattern"),
            K("IN", "Is one of a list or subquery", "IN ("), K("NOT IN", "Is none of a list or subquery", "NOT IN ("),
            K("IS NULL", "Has no value"), K("IS NOT NULL", "Has a value"), K("BETWEEN", "Within a range"),
        };

        public static List<KeywordSuggestion> AfterIs() => new List<KeywordSuggestion> { Null, NotNull };

        public static List<KeywordSuggestion> AfterIsNot() => new List<KeywordSuggestion> { Null };

        public static List<KeywordSuggestion> AfterNot() => new List<KeywordSuggestion>
        {
            K("LIKE", "Does not match a pattern"), K("IN", "Is none of a list or subquery", "IN ("), K("BETWEEN", "Outside a range"),
        };

        public static List<KeywordSuggestion> ToAnd() => new List<KeywordSuggestion> { And };

        public static List<KeywordSuggestion> AfterOuter() => new List<KeywordSuggestion> { K("JOIN", "Outer join"), K("APPLY", "Apply a function or subquery per row") };

        public static List<KeywordSuggestion> ToJoin(string modifier)
        {
            switch (modifier.ToUpperInvariant())
            {
                case "LEFT":
                case "RIGHT":
                case "FULL":
                    return new List<KeywordSuggestion> { Join, K("OUTER JOIN", "Outer join") };
                case "CROSS":
                    return new List<KeywordSuggestion> { Join, K("APPLY", "Run a function or subquery for each row") };
                case "OUTER":
                    return new List<KeywordSuggestion> { K("APPLY", "Run a function or subquery for each row"), Join };
                default:
                    return new List<KeywordSuggestion> { Join };
            }
        }

        public static List<KeywordSuggestion> AfterSetValue() => new List<KeywordSuggestion> { Where, From };

        public static List<KeywordSuggestion> ToEquals() => new List<KeywordSuggestion> { K("=", "Assign this value") };

        public static List<KeywordSuggestion> ToSet() => new List<KeywordSuggestion> { K("SET", "Choose the columns to change") };

        public static List<KeywordSuggestion> ToInto() => new List<KeywordSuggestion> { K("INTO", "The table to add rows to") };

        public static List<KeywordSuggestion> ToDeleteFrom() => new List<KeywordSuggestion> { K("FROM", "The table to remove rows from") };

        public static List<KeywordSuggestion> AfterInsertTable() => new List<KeywordSuggestion>
        {
            K("VALUES", "Give the values to insert", "VALUES ("), K("SELECT", "Insert the rows of a query"),
            K("DEFAULT VALUES", "Insert a row of defaults"), K("EXEC", "Insert what a procedure returns"),
        };

        public static List<KeywordSuggestion> AfterInsertColumns() => new List<KeywordSuggestion>
        {
            K("VALUES", "Give the values to insert", "VALUES ("), K("SELECT", "Insert the rows of a query"), K("EXEC", "Insert what a procedure returns"),
        };

        public static List<KeywordSuggestion> AfterSetOperator() => new List<KeywordSuggestion> { K("ALL", "Keep duplicate rows"), K("SELECT", "The query to combine with") };

        public static List<KeywordSuggestion> ToSelect() => new List<KeywordSuggestion> { K("SELECT", "The query to combine with") };

        // ---- CASE ----

        public static List<KeywordSuggestion> ToWhen() => new List<KeywordSuggestion> { K("WHEN", "A condition and the value for it") };

        public static List<KeywordSuggestion> AfterWhenCondition() => new List<KeywordSuggestion> { K("THEN", "The value when the condition holds"), And, Or };

        public static List<KeywordSuggestion> AfterThenValue() => new List<KeywordSuggestion>
        {
            K("WHEN", "Another condition"), K("ELSE", "The value when nothing matched"), K("END", "Close the CASE", "END"),
        };

        public static List<KeywordSuggestion> ToEnd() => new List<KeywordSuggestion> { K("END", "Close the CASE", "END") };
    }
}
