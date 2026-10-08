using System;
using System.Collections.Generic;
using System.Linq;

namespace SsmsSqlHelper.Parsing
{
    internal enum ColumnContextKind
    {
        /// <summary><c>alias.|</c>: columns of one table.</summary>
        Qualified,
        /// <summary>A column belongs here (SELECT list, WHERE, ON, ORDER BY, ...): columns of every table in the query.</summary>
        Expression,
        /// <summary><c>INSERT INTO table (|</c>: columns to insert into.</summary>
        InsertList,
    }

    internal sealed class ColumnContext
    {
        public ColumnContextKind Kind { get; set; }

        /// <summary>The word being typed (possibly empty), to be replaced by the chosen column.</summary>
        public int Start { get; set; }
        public int End { get; set; }

        /// <summary>Qualified: what stands before the dot, as written (<c>b</c>, <c>dbo.Budgets</c>).</summary>
        public string Qualifier { get; set; }

        /// <summary>Qualified / Expression: the tables of the query, in script order.</summary>
        public List<TableReference> Tables { get; set; } = new List<TableReference>();

        /// <summary>InsertList: the table being inserted into.</summary>
        public ObjectName InsertTarget { get; set; }
    }

    internal static partial class SqlContext
    {
        // Clauses in which a column name is a sensible thing to type
        private static readonly HashSet<string> ColumnClauses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "WHERE", "ON", "GROUP", "ORDER", "HAVING", "SET",
        };

        // Where a clause ends when walking backwards from the caret
        private static readonly HashSet<string> ClauseKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "FROM", "JOIN", "ON", "WHERE", "GROUP", "ORDER", "HAVING", "SET", "VALUES", "INTO", "UPDATE",
            "DELETE", "INSERT", "MERGE", "USING", "WITH", "UNION", "EXCEPT", "INTERSECT", "OUTPUT", "OPTION", "EXEC", "EXECUTE", "DECLARE",
        };

        // Words after which an operand starts
        private static readonly HashSet<string> OperandKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "DISTINCT", "ALL", "WHERE", "HAVING", "AND", "OR", "NOT", "ON", "BY", "SET", "WHEN", "THEN", "ELSE", "CASE",
            "IN", "LIKE", "BETWEEN", "IS", "EXISTS",
        };

        private const string OperatorCharacters = "=<>+-*/%&|^!~";

        /// <summary>
        /// If a column name belongs at the caret, describes where (which tables to take columns from, and what is already typed).
        /// </summary>
        public static bool TryGetColumnContext(string text, int caret, out ColumnContext context)
        {
            context = null;

            var tokens = SqlTokenizer.Tokenize(text);
            if (IsInsideTrivia(tokens, caret))
                return false;

            var sig = Significant(tokens);
            var last = LastIndexBefore(sig, caret);

            // The word being typed: a name part that touches the caret, or nothing yet
            var start = caret;
            var end = caret;
            var wordIdx = last + 1;
            if (last >= 0 && sig[last].End >= caret && sig[last].IsNamePart)
            {
                start = sig[last].Start;
                end = sig[last].End;
                wordIdx = last;
            }

            // A qualifier directly before the word: "b." / "dbo.Budgets."
            string qualifier = null;
            var dotIdx = wordIdx - 1;
            var wordStartOffset = wordIdx < sig.Count && wordIdx >= 0 && wordIdx <= last ? sig[wordIdx].Start : caret;
            if (dotIdx >= 1 && sig[dotIdx].IsSymbol('.') && sig[dotIdx].End == wordStartOffset && sig[dotIdx - 1].IsNamePart && sig[dotIdx - 1].End == sig[dotIdx].Start)
            {
                var qualStart = WalkBackName(sig, dotIdx - 1);
                qualifier = text.Substring(sig[qualStart].Start, sig[dotIdx - 1].End - sig[qualStart].Start);
                wordIdx = qualStart + 0; // the clause check below looks before the whole qualified name
            }

            // Table names (FROM x.|) and join conditions have their own lists
            if (TryGetTableNameSpan(text, caret, out _, out _))
                return false;

            var scopeTables = GetColumnScope(sig, wordIdx);

            if (qualifier != null)
            {
                if (scopeTables.Count == 0)
                    return false;

                context = new ColumnContext { Kind = ColumnContextKind.Qualified, Start = start, End = end, Qualifier = qualifier, Tables = scopeTables };
                return true;
            }

            // INSERT INTO table (a, |b
            if (TryGetInsertTarget(sig, wordIdx, text, out var target))
            {
                context = new ColumnContext { Kind = ColumnContextKind.InsertList, Start = start, End = end, InsertTarget = target };
                return true;
            }

            if (scopeTables.Count == 0 || !IsColumnPosition(sig, wordIdx))
                return false;

            context = new ColumnContext { Kind = ColumnContextKind.Expression, Start = start, End = end, Tables = scopeTables };
            return true;
        }

        /// <summary>
        /// Tables whose columns are visible at <paramref name="tokenIdx"/>: the FROM clause of the query it is in, plus the
        /// target of an UPDATE (which has no FROM of its own unless it joins).
        /// </summary>
        private static List<TableReference> GetColumnScope(List<SqlToken> allTokens, int tokenIdx)
        {
            var position = tokenIdx < allTokens.Count && tokenIdx >= 0 ? allTokens[tokenIdx].Start : (allTokens.Count > 0 ? allTokens[allTokens.Count - 1].End : 0);
            var start = FindScopeStart(allTokens, position, out var keyword, out var open);
            var sig = WithoutBrackets(allTokens, open);

            if (keyword != "UPDATE" || start >= sig.Count || !sig[start].IsNamePart)
                return FindFromTables(sig, start);

            // UPDATE target [alias] SET ... [FROM ...]
            var nameEnd = WalkForwardName(sig, start);
            var aliasIdx = nameEnd + 1;
            string alias = null;
            if (aliasIdx < sig.Count && sig[aliasIdx].IsKeyword("AS"))
                aliasIdx++;
            if (aliasIdx < sig.Count && IsAlias(sig[aliasIdx]) && !sig[aliasIdx].IsKeyword("SET"))
            {
                alias = Unquote(sig[aliasIdx].Text);
                nameEnd = aliasIdx;
            }

            var target = new TableReference(
                new ObjectName(sig[start].Start, sig[WalkForwardName(sig, start)].End, JoinName(sig, start, WalkForwardName(sig, start))), alias, sig[nameEnd].End);

            // The FROM of "UPDATE ... SET ... FROM ..." sits after the SET list
            var depth = 0;
            var fromStart = sig.Count;
            for (var i = nameEnd + 1; i < sig.Count; i++)
            {
                if (sig[i].IsSymbol('(')) depth++;
                else if (sig[i].IsSymbol(')')) { if (--depth < 0) break; }
                else if (depth == 0 && sig[i].IsSymbol(';')) break;
                else if (depth == 0 && sig[i].IsKeyword("FROM")) { fromStart = i; break; }
            }

            var result = new List<TableReference>();
            var joined = fromStart < sig.Count ? FindFromTables(sig, fromStart) : new List<TableReference>();

            // "UPDATE b SET ... FROM Budgets b" names the alias, which the FROM clause already provides
            var targetName = Unquote(target.Name.Text.Split('.').Last());
            var targetIsAliasOfFrom = joined.Any(r => string.Equals(r.Alias, targetName, StringComparison.OrdinalIgnoreCase));
            if (!targetIsAliasOfFrom)
                result.Add(target);
            result.AddRange(joined);
            return result;
        }

        /// <summary>True where an operand may start: after SELECT, an operator, a comma, an opening bracket, AND, ORDER BY, ...</summary>
        private static bool IsColumnPosition(List<SqlToken> sig, int wordIdx)
        {
            var prev = wordIdx - 1;
            if (prev < 0)
                return false;

            var clause = ClauseAt(sig, prev);
            if (clause == null || !ColumnClauses.Contains(clause))
                return false;

            var t = sig[prev];
            if (t.Kind == TokenKind.Symbol)
                return t.IsSymbol(',') || t.IsSymbol('(') || (t.Text.Length == 1 && OperatorCharacters.IndexOf(t.Text[0]) >= 0);

            if (t.Kind == TokenKind.Word && OperandKeywords.Contains(t.Text))
                return true;

            // SELECT TOP 10 |
            return clause.Equals("SELECT", StringComparison.OrdinalIgnoreCase) && IsSelectListItemStart(sig, prev);
        }

        /// <summary>The clause keyword (SELECT, WHERE, ...) governing the token at <paramref name="idx"/>, looking back past balanced brackets.</summary>
        private static string ClauseAt(List<SqlToken> sig, int idx)
        {
            var depth = 0;
            for (var i = idx; i >= 0; i--)
            {
                var t = sig[i];
                if (t.IsSymbol(')'))
                {
                    depth++;
                }
                else if (t.IsSymbol('('))
                {
                    if (depth > 0)
                        depth--;
                }
                else if (depth == 0 && t.Kind == TokenKind.Word && ClauseKeywords.Contains(t.Text))
                {
                    return t.Text.ToUpperInvariant();
                }
            }
            return null;
        }

        /// <summary>The table of <c>INSERT [INTO] table (a, |</c>.</summary>
        private static bool TryGetInsertTarget(List<SqlToken> sig, int wordIdx, string text, out ObjectName target)
        {
            target = null;
            var j = wordIdx - 1;
            if (j < 0 || !(sig[j].IsSymbol('(') || sig[j].IsSymbol(',')))
                return false;

            // Back over "a, b," to the opening bracket
            while (j >= 0 && !sig[j].IsSymbol('('))
            {
                if (!(sig[j].IsNamePart || sig[j].IsSymbol(',') || sig[j].IsSymbol('.')))
                    return false;
                j--;
            }
            if (j < 1 || !sig[j - 1].IsNamePart)
                return false;

            var nameStart = WalkBackName(sig, j - 1);
            var before = nameStart - 1;
            var afterInto = before >= 1 && sig[before].IsKeyword("INTO") && sig[before - 1].IsKeyword("INSERT");
            var afterInsert = before >= 0 && sig[before].IsKeyword("INSERT");
            if (!afterInto && !afterInsert)
                return false;

            target = new ObjectName(sig[nameStart].Start, sig[j - 1].End, text.Substring(sig[nameStart].Start, sig[j - 1].End - sig[nameStart].Start));
            return true;
        }
    }
}
