using System;
using System.Collections.Generic;
using System.Linq;

namespace SsmsSqlHelper.Parsing
{
    /// <summary>A (possibly multi-part) object name as written in the script, e.g. <c>dbo.[Order]</c>.</summary>
    internal sealed class ObjectName
    {
        public ObjectName(int start, int end, string text)
        {
            Start = start;
            End = end;
            Text = text;
        }

        public int Start { get; }
        public int End { get; }
        public string Text { get; }
    }

    internal sealed class TableReference
    {
        public TableReference(ObjectName name, string alias, int end)
        {
            Name = name;
            Alias = alias;
            End = end;
        }

        public ObjectName Name { get; }
        /// <summary>Alias as written (brackets removed), or null.</summary>
        public string Alias { get; }
        /// <summary>Offset just past the last token of the reference, including the alias.</summary>
        public int End { get; }
    }

    internal enum TabContextKind
    {
        None,
        /// <summary><c>INSERT INTO table|</c></summary>
        InsertTable,
        /// <summary><c>UPDATE table|</c></summary>
        UpdateTable,
        /// <summary><c>SELECT *|</c> or <c>SELECT alias.*|</c></summary>
        SelectStar,
        /// <summary><c>FROM table|</c> or <c>JOIN table|</c> with no alias yet</summary>
        SourceTable,
        /// <summary><c>EXEC procedure|</c></summary>
        ExecuteProcedure,
    }

    internal sealed class TabContext
    {
        public TabContextKind Kind { get; set; }

        /// <summary>Text range to replace (for SelectStar: the star or <c>alias.*</c>; otherwise the gap after the table name).</summary>
        public int ReplaceStart { get; set; }
        public int ReplaceEnd { get; set; }

        /// <summary>Target table for Insert/Update.</summary>
        public ObjectName Table { get; set; }
        /// <summary>Start of the EXEC keyword for procedure expansion.</summary>
        public int StatementStart { get; set; }

        /// <summary>SourceTable only: the name follows JOIN (as opposed to FROM).</summary>
        public bool IsJoin { get; set; }

        /// <summary>SourceTable only: a plain JOIN with no ON yet, so an ON condition can be offered (not CROSS JOIN).</summary>
        public bool AppendOn { get; set; }

        /// <summary>Qualifier before <c>.*</c>, or null for a bare star.</summary>
        public string StarQualifier { get; set; }

        /// <summary>Tables in the FROM clause of the statement containing the star.</summary>
        public IReadOnlyList<TableReference> FromTables { get; set; }
    }

    /// <summary>Works out what the user is doing at the caret from the surrounding T-SQL tokens.</summary>
    internal static partial class SqlContext
    {
        // Words that start a new statement; used to stop scanning at statement boundaries
        private static readonly HashSet<string> StatementStarts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "CREATE", "ALTER", "DROP", "EXEC", "EXECUTE",
            "DECLARE", "SET", "IF", "WHILE", "GO", "PRINT", "USE", "TRUNCATE", "RETURN", "BEGIN", "THROW", "RAISERROR",
        };

        private static readonly HashSet<string> FromClauseEnds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "WHERE", "GROUP", "ORDER", "HAVING", "UNION", "EXCEPT", "INTERSECT", "OPTION", "FOR", "WINDOW", "END",
        };

        // Words that can follow a table name but are never its alias
        private static readonly HashSet<string> NotAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ON", "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "CROSS", "OUTER", "APPLY", "WITH", "AS", "PIVOT",
            "UNPIVOT", "TABLESAMPLE", "WHEN", "USING", "OUTPUT", "VALUES", "DEFAULT",
        };

        // INSERT/DELETE without INTO/FROM are left out: the list would pop up while typing "INTO"/"FROM"
        private static readonly HashSet<string> TableKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "FROM", "JOIN", "INTO", "UPDATE", "TABLE",
        };

        private static readonly HashSet<string> InsertBodyStarts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "VALUES", "SELECT", "DEFAULT", "EXEC", "EXECUTE", "OUTPUT",
        };

        /// <summary>
        /// If the caret is where a table name belongs (after FROM, JOIN, INTO, UPDATE, ...), returns the
        /// span of the name being typed (possibly empty) so a completion list can replace it.
        /// </summary>
        public static bool TryGetTableNameSpan(string text, int caret, out int start, out int end)
        {
            start = end = caret;
            var tokens = SqlTokenizer.Tokenize(text);
            if (IsInsideTrivia(tokens, caret))
                return false;

            var sig = Significant(tokens);
            var last = LastIndexBefore(sig, caret);

            // Name being typed: contiguous name parts / dots ending at (or containing) the caret
            var nameStartIdx = last + 1;
            if (last >= 0 && sig[last].End >= caret && (sig[last].IsNamePart || sig[last].IsSymbol('.')))
            {
                nameStartIdx = WalkBackName(sig, last);
                start = sig[nameStartIdx].Start;
                end = sig[last].IsNamePart ? sig[last].End : caret;
            }

            var prev = nameStartIdx - 1;
            if (prev < 0 || !sig[prev].IsNamePart || !TableKeywords.Contains(sig[prev].Text))
                return false;

            // ON UPDATE in foreign key definitions
            if (sig[prev].IsKeyword("UPDATE") && prev > 0 && sig[prev - 1].IsKeyword("ON"))
                return false;

            // Only TRUNCATE TABLE names an existing table; CREATE TABLE names a new one
            if (sig[prev].IsKeyword("TABLE") && !(prev > 0 && sig[prev - 1].IsKeyword("TRUNCATE")))
                return false;

            return true;
        }

        /// <summary>Span of a procedure name being typed immediately after EXEC or EXECUTE.</summary>
        public static bool TryGetProcedureNameSpan(string text, int caret, out int start, out int end)
        {
            start = end = caret;
            var tokens = SqlTokenizer.Tokenize(text);
            if (IsInsideTrivia(tokens, caret))
                return false;

            var sig = Significant(tokens);
            var last = LastIndexBefore(sig, caret);
            if (last >= 0 && sig[last].End == caret &&
                (sig[last].IsKeyword("EXEC") || sig[last].IsKeyword("EXECUTE")))
                return true;
            var nameStartIdx = last + 1;
            if (last >= 0 && sig[last].End >= caret && (sig[last].IsNamePart || sig[last].IsSymbol('.')))
            {
                nameStartIdx = WalkBackName(sig, last);
                start = sig[nameStartIdx].Start;
                end = sig[last].IsNamePart ? sig[last].End : caret;
            }

            var prev = nameStartIdx - 1;
            return prev >= 0 && (sig[prev].IsKeyword("EXEC") || sig[prev].IsKeyword("EXECUTE")) &&
                   text.IndexOfAny(new[] { '\r', '\n' }, sig[prev].End, caret - sig[prev].End) < 0;
        }

        /// <summary>Procedure name under the caret in an EXEC call, declaration, or bare procedure call.</summary>
        public static ObjectName GetProcedureAtCaret(string text, int caret)
        {
            if (caret < 0 || caret > text.Length)
                return null;

            var tokens = SqlTokenizer.Tokenize(text);
            if (IsInsideTrivia(tokens, caret))
                return null;

            var sig = Significant(tokens);
            var index = sig.FindIndex(t => t.IsNamePart && t.Start <= caret && caret <= t.End);
            if (index < 0)
                return null;

            var first = WalkBackName(sig, index);
            var last = WalkForwardName(sig, index);
            var keyword = first - 1;
            var isCall = keyword >= 0 && (sig[keyword].IsKeyword("EXEC") || sig[keyword].IsKeyword("EXECUTE") ||
                (sig[keyword].IsSymbol('=') && keyword >= 2 && sig[keyword - 1].Kind == TokenKind.Variable &&
                 (sig[keyword - 2].IsKeyword("EXEC") || sig[keyword - 2].IsKeyword("EXECUTE"))));
            var isDeclaration = keyword > 0 && (sig[keyword].IsKeyword("PROC") || sig[keyword].IsKeyword("PROCEDURE")) &&
                (sig[keyword - 1].IsKeyword("CREATE") || sig[keyword - 1].IsKeyword("ALTER"));
            // SQL Server allows a procedure call without EXEC when the name starts a line.
            // Metadata lookup in the editor confirms that this name really is a procedure.
            var lineStart = text.LastIndexOfAny(new[] { '\r', '\n' }, Math.Max(0, sig[first].Start - 1)) + 1;
            var isBareCall = string.IsNullOrWhiteSpace(text.Substring(lineStart, sig[first].Start - lineStart));
            if (!isCall && !isDeclaration && !isBareCall)
                return null;

            return new ObjectName(sig[first].Start, sig[last].End, JoinName(sig, first, last));
        }

        /// <summary>Potential table, view or function name under the caret; the database lookup verifies its type.</summary>
        public static ObjectName GetSchemaObjectAtCaret(string text, int caret)
        {
            if (caret < 0 || caret > text.Length)
                return null;
            var tokens = SqlTokenizer.Tokenize(text);
            if (IsInsideTrivia(tokens, caret))
                return null;
            var sig = Significant(tokens);
            var index = sig.FindIndex(t => t.IsNamePart && t.Start <= caret && caret <= t.End);
            if (index < 0)
                return null;
            var first = WalkBackName(sig, index);
            var last = WalkForwardName(sig, index);
            var previous = first > 0 ? sig[first - 1] : default(SqlToken?);
            var next = last + 1 < sig.Count ? sig[last + 1] : default(SqlToken?);
            var lineStart = text.LastIndexOfAny(new[] { '\r', '\n' }, Math.Max(0, sig[first].Start - 1)) + 1;
            var startsLine = string.IsNullOrWhiteSpace(text.Substring(lineStart, sig[first].Start - lineStart));
            var followsObjectKeyword = previous.HasValue &&
                (previous.Value.IsKeyword("FROM") || previous.Value.IsKeyword("JOIN") ||
                 previous.Value.IsKeyword("INTO") || previous.Value.IsKeyword("UPDATE") ||
                 previous.Value.IsKeyword("TABLE") || previous.Value.IsKeyword("VIEW") ||
                 previous.Value.IsKeyword("FUNCTION") || previous.Value.IsKeyword("APPLY") ||
                 previous.Value.IsKeyword("EXEC") || previous.Value.IsKeyword("EXECUTE") ||
                 previous.Value.IsKeyword("PROC") || previous.Value.IsKeyword("PROCEDURE"));
            var functionCall = next.HasValue && next.Value.IsSymbol('(') &&
                               last > first && sig[last].End == next.Value.Start;
            if (!startsLine && !followsObjectKeyword && !functionCall)
                return null;
            return new ObjectName(sig[first].Start, sig[last].End, JoinName(sig, first, last));
        }

        /// <summary>Recognises the Tab-expandable constructs immediately before the caret.</summary>
        public static TabContext GetTabContext(string text, int caret)
        {
            var tokens = SqlTokenizer.Tokenize(text);
            if (IsInsideTrivia(tokens, caret))
                return null;

            var sig = Significant(tokens);
            var last = LastIndexBefore(sig, caret);
            if (last < 0 || sig[last].End > caret)
                return null;

            if (sig[last].IsSymbol('*'))
                return GetStarContext(sig, last, caret);

            if (sig[last].IsNamePart)
                return GetTableContext(text, sig, last, caret);

            return null;
        }

        private static TabContext GetStarContext(List<SqlToken> sig, int starIdx, int caret)
        {
            var replaceStart = sig[starIdx].Start;
            string qualifier = null;
            var beforeIdx = starIdx - 1;

            if (beforeIdx >= 1 && sig[beforeIdx].IsSymbol('.') && sig[beforeIdx - 1].IsNamePart)
            {
                qualifier = Unquote(sig[beforeIdx - 1].Text);
                replaceStart = sig[beforeIdx - 1].Start;
                beforeIdx -= 2;
            }

            if (!IsSelectListItemStart(sig, beforeIdx))
                return null;

            var tables = FindFromTables(sig, starIdx + 1);
            if (tables.Count == 0)
                return null;

            return new TabContext
            {
                Kind = TabContextKind.SelectStar,
                ReplaceStart = replaceStart,
                ReplaceEnd = sig[starIdx].End,
                StarQualifier = qualifier,
                FromTables = tables,
            };
        }

        private static TabContext GetTableContext(string text, List<SqlToken> sig, int last, int caret)
        {
            var nameStartIdx = WalkBackName(sig, last);
            var prev = nameStartIdx - 1;
            if (prev < 0)
                return null;

            TabContextKind kind;
            if (sig[prev].IsKeyword("INSERT") || (sig[prev].IsKeyword("INTO") && prev > 0 && sig[prev - 1].IsKeyword("INSERT")))
                kind = TabContextKind.InsertTable;
            else if (sig[prev].IsKeyword("UPDATE") && !(prev > 0 && sig[prev - 1].IsKeyword("ON")))
                kind = TabContextKind.UpdateTable;
            else if (sig[prev].IsKeyword("JOIN") || (sig[prev].IsKeyword("FROM") && !(prev > 0 && sig[prev - 1].IsKeyword("DELETE"))))
                kind = TabContextKind.SourceTable;
            else if (sig[prev].IsKeyword("EXEC") || sig[prev].IsKeyword("EXECUTE"))
                kind = TabContextKind.ExecuteProcedure;
            else
                return null;

            // Tab on the next line (to indent) must never reach back and rewrite the line break
            if (kind == TabContextKind.SourceTable && text.IndexOfAny(new[] { '\r', '\n' }, sig[last].End, caret - sig[last].End) >= 0)
                return null;
            if (kind == TabContextKind.ExecuteProcedure && text.IndexOfAny(new[] { '\r', '\n' }, sig[last].End, caret - sig[last].End) >= 0)
                return null;

            // Don't generate when the statement already has its body
            var next = last + 1 < sig.Count ? sig[last + 1] : default(SqlToken?);
            if (next.HasValue)
            {
                var n = next.Value;
                if (n.IsSymbol('(') || n.IsSymbol('.'))
                    return null;
                if (kind == TabContextKind.InsertTable && n.IsNamePart && InsertBodyStarts.Contains(n.Text))
                    return null;
                if (kind == TabContextKind.UpdateTable && (n.IsKeyword("SET") || n.IsKeyword("FROM")))
                    return null;
                if (kind == TabContextKind.SourceTable && (n.IsKeyword("AS") || IsAlias(n)))
                    return null;
                if (kind == TabContextKind.ExecuteProcedure && !n.IsSymbol(';') && !n.IsKeyword("GO") &&
                    text.IndexOfAny(new[] { '\r', '\n' }, caret, n.Start - caret) < 0)
                    return null;
            }

            var name = new ObjectName(sig[nameStartIdx].Start, sig[last].End,
                text.Substring(sig[nameStartIdx].Start, sig[last].End - sig[nameStartIdx].Start));
            var context = new TabContext { Kind = kind, Table = name, ReplaceStart = name.End, ReplaceEnd = caret,
                StatementStart = kind == TabContextKind.ExecuteProcedure ? sig[prev].Start : 0 };

            if (kind == TabContextKind.SourceTable && sig[prev].IsKeyword("JOIN"))
            {
                context.IsJoin = true;
                context.AppendOn = IsPlainJoin(sig, prev) && !(next.HasValue && next.Value.IsKeyword("ON"));
            }
            return context;
        }

        /// <summary>JOIN that takes an ON condition: not CROSS JOIN.</summary>
        private static bool IsPlainJoin(List<SqlToken> sig, int joinIdx)
        {
            for (var i = joinIdx - 1; i >= 0 && i >= joinIdx - 2; i--)
            {
                if (sig[i].IsKeyword("CROSS"))
                    return false;
                // INNER / LEFT / RIGHT / FULL / OUTER / LOOP / HASH / MERGE / REMOTE hints precede JOIN
                if (!(sig[i].Kind == TokenKind.Word && (JoinModifiers.Contains(sig[i].Text))))
                    break;
            }
            return true;
        }

        private static readonly HashSet<string> JoinModifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "INNER", "LEFT", "RIGHT", "FULL", "OUTER", "LOOP", "HASH", "MERGE", "REMOTE", "CROSS",
        };

        // Words that begin a query scope when walking backwards from the caret
        private static readonly HashSet<string> ScopeStarts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE",
        };

        /// <summary>
        /// Names already taken in the FROM clause of the query containing <paramref name="position"/>: aliases,
        /// plus table names that have no alias. The table starting at <paramref name="excludeNameStart"/> is skipped.
        /// Subqueries and neighbouring statements are separate scopes, so an alias reused elsewhere in the script doesn't count.
        /// </summary>
        public static HashSet<string> GetNamesInScope(string text, int position, int excludeNameStart)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in GetScopeTables(text, position))
            {
                if (r.Name.Start == excludeNameStart)
                    continue;
                var parts = Metadata.SqlIdentifier.Split(r.Name.Text);
                names.Add(r.Alias ?? parts[parts.Count - 1]);
            }
            return names;
        }

        /// <summary>All tables of the FROM clause of the query containing <paramref name="position"/>, in script order.</summary>
        public static List<TableReference> GetScopeTables(string text, int position) =>
            GetScopeTables(Significant(SqlTokenizer.Tokenize(text)), position);

        private static List<TableReference> GetScopeTables(List<SqlToken> sig, int position)
        {
            var start = FindScopeStart(sig, position, out _, out var open);
            return FindFromTables(WithoutBrackets(sig, open), start);
        }

        /// <summary>
        /// Drops the given opening brackets and their closing partners (if typed yet). They are function calls and value lists
        /// around the caret (<c>COUNT(|) FROM t</c>), which would otherwise hide the FROM clause behind them.
        /// </summary>
        private static List<SqlToken> WithoutBrackets(List<SqlToken> sig, List<int> openBrackets)
        {
            if (openBrackets.Count == 0)
                return sig;

            var remove = new HashSet<int>(openBrackets);
            foreach (var open in openBrackets)
            {
                var depth = 0;
                for (var i = open; i < sig.Count; i++)
                {
                    if (sig[i].IsSymbol('('))
                    {
                        depth++;
                    }
                    else if (sig[i].IsSymbol(')') && --depth == 0)
                    {
                        remove.Add(i);
                        break;
                    }
                }
            }

            return sig.Where((t, i) => !remove.Contains(i)).ToList();
        }

        /// <summary>
        /// Index of the first token of the query containing <paramref name="position"/>: just after its SELECT / UPDATE / DELETE / ...
        /// keyword, a separating semicolon, or the parenthesis that opens a subquery.
        /// </summary>
        /// <param name="keyword">The statement keyword that starts the scope (upper case), or null.</param>
        /// <param name="openBrackets">Unmatched opening brackets between the scope start and the position (function calls, value lists).</param>
        private static int FindScopeStart(List<SqlToken> sig, int position, out string keyword, out List<int> openBrackets)
        {
            keyword = null;
            openBrackets = new List<int>();
            var depth = 0;
            for (var i = LastIndexBefore(sig, position); i >= 0; i--)
            {
                var t = sig[i];
                if (t.IsSymbol(')'))
                {
                    depth++;
                }
                else if (t.IsSymbol('('))
                {
                    // An unmatched bracket is a function call or value list (IN (1, 2), SUM(...)) that belongs to the
                    // enclosing query; a subquery always has its own SELECT, which ends the walk before we get here
                    if (depth > 0)
                        depth--;
                    else
                        openBrackets.Add(i);
                }
                else if (depth == 0 && t.IsSymbol(';'))
                {
                    return i + 1;
                }
                else if (depth == 0 && t.Kind == TokenKind.Word && ScopeStarts.Contains(t.Text))
                {
                    keyword = t.Text.ToUpperInvariant();
                    return i + 1;
                }
            }
            return 0;
        }

        /// <summary>
        /// If a table name starting at <paramref name="nameStart"/> would follow a plain JOIN, returns the tables
        /// to its left that its ON condition can refer to (possibly empty); otherwise null.
        /// </summary>
        public static List<TableReference> GetJoinScope(string text, int nameStart)
        {
            var sig = Significant(SqlTokenizer.Tokenize(text));
            var prev = LastIndexBefore(sig, nameStart);
            if (prev < 0 || !sig[prev].IsKeyword("JOIN") || !IsPlainJoin(sig, prev))
                return null;

            return GetScopeTables(sig, nameStart).Where(r => r.Name.Start < nameStart).ToList();
        }

        /// <summary>
        /// If the caret is where a join condition starts (<c>JOIN t alias ON |</c>), returns the span being typed,
        /// the joined table, and the tables to its left that the condition can refer to.
        /// </summary>
        public static bool TryGetJoinOn(string text, int caret, out int start, out int end,
            out TableReference target, out List<TableReference> earlier)
        {
            start = end = caret;
            target = null;
            earlier = null;

            var tokens = SqlTokenizer.Tokenize(text);
            if (IsInsideTrivia(tokens, caret))
                return false;

            var sig = Significant(tokens);
            var last = LastIndexBefore(sig, caret);

            var wordStartIdx = last + 1;
            if (last >= 0 && sig[last].End >= caret && (sig[last].IsNamePart || sig[last].IsSymbol('.')))
            {
                wordStartIdx = WalkBackName(sig, last);
                start = sig[wordStartIdx].Start;
                end = sig[last].IsNamePart ? sig[last].End : caret;
            }

            var on = wordStartIdx - 1;
            if (on < 1 || !sig[on].IsKeyword("ON"))
                return false;

            // The reference right before ON must itself directly follow a JOIN
            var refEnd = sig[on - 1].End;
            var scope = GetScopeTables(sig, caret);
            var joined = scope.FirstOrDefault(r => r.End == refEnd);
            if (joined == null)
                return false;

            var joinedStart = joined.Name.Start;
            var nameIdx = sig.FindIndex(t => t.Start == joinedStart);
            if (nameIdx < 1 || !sig[nameIdx - 1].IsKeyword("JOIN") || !IsPlainJoin(sig, nameIdx - 1))
                return false;

            target = joined;
            earlier = scope.Where(r => r.Name.Start < joinedStart).ToList();
            return true;
        }

        /// <summary>True if the token at <paramref name="idx"/> is where a select-list item may begin.</summary>
        private static bool IsSelectListItemStart(List<SqlToken> sig, int idx)
        {
            if (idx < 0)
                return false;

            var t = sig[idx];
            if (t.IsKeyword("SELECT") || t.IsKeyword("DISTINCT") || t.IsKeyword("ALL") || t.IsSymbol(','))
                return true;

            // SELECT TOP 10 * / TOP (10) * / TOP 10 PERCENT * / TOP 10 WITH TIES *
            for (var i = idx; i >= 0 && i >= idx - 5; i--)
            {
                if (sig[i].IsKeyword("TOP"))
                    return true;
                if (!(sig[i].Kind == TokenKind.Number || sig[i].Kind == TokenKind.Variable || sig[i].IsSymbol('(') ||
                      sig[i].IsSymbol(')') || sig[i].IsKeyword("PERCENT") || sig[i].IsKeyword("WITH") || sig[i].IsKeyword("TIES")))
                    return false;
            }
            return false;
        }

        /// <summary>Collects the tables in the FROM clause that follows <paramref name="from"/>, at the same nesting level.</summary>
        internal static List<TableReference> FindFromTables(List<SqlToken> sig, int from)
        {
            var result = new List<TableReference>();
            var depth = 0;
            var inFrom = false;
            var expectTable = false;

            for (var j = from; j < sig.Count; j++)
            {
                var t = sig[j];

                if (t.IsSymbol('('))
                {
                    if (depth == 0 && expectTable)
                    {
                        // Derived table: skip it and its alias
                        j = SkipBalanced(sig, j);
                        j = SkipAlias(sig, j + 1) - 1;
                        expectTable = false;
                        continue;
                    }
                    depth++;
                    continue;
                }
                if (t.IsSymbol(')'))
                {
                    if (depth == 0)
                        break;
                    depth--;
                    continue;
                }
                if (depth > 0)
                    continue;
                if (t.IsSymbol(';'))
                    break;

                if (t.IsSymbol(','))
                {
                    if (inFrom)
                        expectTable = true;
                    continue;
                }

                if (t.Kind != TokenKind.Word && !(t.Kind == TokenKind.QuotedIdentifier && expectTable))
                    continue;

                if (!inFrom)
                {
                    if (t.IsKeyword("FROM"))
                    {
                        inFrom = true;
                        expectTable = true;
                    }
                    else if (StatementStarts.Contains(t.Text))
                    {
                        break;
                    }
                    continue;
                }

                if (t.IsKeyword("JOIN"))
                {
                    expectTable = true;
                    continue;
                }
                if (t.IsKeyword("WITH") && j + 1 < sig.Count && sig[j + 1].IsSymbol('('))
                {
                    j = SkipBalanced(sig, j + 1); // table hint
                    continue;
                }
                if (t.Kind == TokenKind.Word && (FromClauseEnds.Contains(t.Text) || StatementStarts.Contains(t.Text) || t.IsKeyword("FROM")))
                    break;

                if (expectTable)
                {
                    var nameEnd = WalkForwardName(sig, j);
                    var name = new ObjectName(sig[j].Start, sig[nameEnd].End, JoinName(sig, j, nameEnd));
                    var aliasIdx = nameEnd + 1;
                    string alias = null;
                    if (aliasIdx < sig.Count && sig[aliasIdx].IsKeyword("AS"))
                        aliasIdx++;
                    if (aliasIdx < sig.Count && IsAlias(sig[aliasIdx]))
                    {
                        alias = Unquote(sig[aliasIdx].Text);
                        nameEnd = aliasIdx;
                    }

                    // A function in FROM (e.g. OPENJSON(...)) isn't a table
                    if (!(nameEnd + 1 < sig.Count && sig[nameEnd + 1].IsSymbol('(') && alias == null))
                        result.Add(new TableReference(name, alias, sig[nameEnd].End));
                    j = nameEnd;
                    expectTable = false;
                }
            }
            return result;
        }

        private static bool IsAlias(SqlToken t) =>
            t.Kind == TokenKind.QuotedIdentifier ||
            (t.Kind == TokenKind.Word && !NotAliases.Contains(t.Text) && !FromClauseEnds.Contains(t.Text) && !StatementStarts.Contains(t.Text));

        private static int SkipAlias(List<SqlToken> sig, int j)
        {
            if (j < sig.Count && sig[j].IsKeyword("AS"))
                j++;
            if (j < sig.Count && IsAlias(sig[j]))
                j++;
            return j;
        }

        private static int SkipBalanced(List<SqlToken> sig, int open)
        {
            var depth = 0;
            for (var j = open; j < sig.Count; j++)
            {
                if (sig[j].IsSymbol('(')) depth++;
                else if (sig[j].IsSymbol(')') && --depth == 0) return j;
            }
            return sig.Count - 1;
        }

        /// <summary>From the last token of a name, walks back over contiguous <c>part.part</c> tokens.</summary>
        private static int WalkBackName(List<SqlToken> sig, int last)
        {
            var i = last;
            while (i > 0 && sig[i - 1].End == sig[i].Start &&
                   (sig[i - 1].IsSymbol('.') || (sig[i - 1].IsNamePart && sig[i].IsSymbol('.'))))
            {
                i--;
            }
            return i;
        }

        private static int WalkForwardName(List<SqlToken> sig, int first)
        {
            var i = first;
            while (i + 1 < sig.Count && sig[i + 1].Start == sig[i].End &&
                   (sig[i + 1].IsSymbol('.') || (sig[i + 1].IsNamePart && sig[i].IsSymbol('.'))))
            {
                i++;
            }
            return i;
        }

        private static string JoinName(List<SqlToken> sig, int first, int last) =>
            string.Concat(sig.Skip(first).Take(last - first + 1).Select(t => t.Text));

        private static string Unquote(string part) => Metadata.SqlIdentifier.Split(part)[0];

        private static bool IsInsideTrivia(List<SqlToken> tokens, int caret)
        {
            foreach (var t in tokens)
            {
                if (t.Start >= caret)
                    break;
                if (caret > t.Start && caret <= t.End && (t.Kind == TokenKind.Comment || t.Kind == TokenKind.String))
                {
                    // A closed string/block comment ending exactly at the caret is outside it
                    if (caret == t.End && IsClosed(t))
                        return false;
                    return true;
                }
            }
            return false;
        }

        private static bool IsClosed(SqlToken t)
        {
            if (t.Kind == TokenKind.String)
                return t.Text.Length >= 2 && t.Text[t.Text.Length - 1] == '\'' && t.Text.TrimStart('N', 'n').Length >= 2;
            return t.Text.StartsWith("/*", StringComparison.Ordinal) && t.Text.EndsWith("*/", StringComparison.Ordinal) && t.Text.Length >= 4;
        }

        private static List<SqlToken> Significant(List<SqlToken> tokens) => tokens.Where(t => !t.IsTrivia).ToList();

        private static int LastIndexBefore(List<SqlToken> sig, int caret)
        {
            for (var i = sig.Count - 1; i >= 0; i--)
            {
                if (sig[i].Start < caret)
                    return i;
            }
            return -1;
        }
    }
}
