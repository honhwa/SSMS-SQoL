using System;
using System.Collections.Generic;
using System.Linq;

namespace SsmsSqlHelper.Parsing
{
    internal sealed class KeywordContext
    {
        /// <summary>The word being typed (possibly empty), to be replaced by the chosen keyword.</summary>
        public int Start { get; set; }
        public int End { get; set; }

        public List<KeywordSuggestion> Suggestions { get; set; }

        /// <summary>Nothing of the statement is written yet, so these are the words that start one.</summary>
        public bool IsStatementStart { get; set; }
    }

    internal static partial class SqlContext
    {
        // Words that open a new clause of the statement being analysed
        private static readonly HashSet<string> KeywordClauseWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "FROM", "JOIN", "APPLY", "ON", "WHERE", "GROUP", "HAVING", "ORDER", "SET", "VALUES", "INTO", "OFFSET",
            "UNION", "EXCEPT", "INTERSECT",
        };

        private static readonly HashSet<string> JoinModifiers2 = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "INNER", "LEFT", "RIGHT", "FULL", "OUTER", "CROSS",
        };

        // Words that are not themselves a value, so something that needs a value cannot end on them
        private static readonly HashSet<string> NotValueWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AND", "OR", "NOT", "IS", "IN", "LIKE", "BETWEEN", "EXISTS", "CASE", "WHEN", "THEN", "ELSE", "SELECT", "FROM", "WHERE",
            "ON", "BY", "AS", "DISTINCT", "ALL", "TOP", "SET", "JOIN", "GROUP", "ORDER", "HAVING", "UNION", "EXCEPT", "INTERSECT",
            "INNER", "LEFT", "RIGHT", "FULL", "OUTER", "CROSS", "APPLY", "INTO", "VALUES", "OFFSET",
        };

        private enum ConditionState
        {
            Start,          // nothing yet, or just after AND / OR
            NotAtStart,     // NOT and nothing else
            NeedValue,      // ends on an operator: a value must follow
            Partial,        // a value with no comparison yet
            Complete,       // a finished comparison
            AfterIs,
            AfterIsNot,
            AfterNot,       // value NOT
            NeedBetweenAnd, // BETWEEN a
        }

        /// <summary>
        /// If keywords make sense at the caret (after a table, in a condition, at the start of a statement, ...), the word being
        /// typed and what could follow. False inside strings and comments, inside brackets, after a dot, and where a name is expected.
        /// </summary>
        public static bool TryGetKeywordContext(string text, int caret, out KeywordContext context)
        {
            context = null;

            var tokens = SqlTokenizer.Tokenize(text);
            if (IsInsideTrivia(tokens, caret))
                return false;

            var sig = Significant(tokens);
            var last = LastIndexBefore(sig, caret);

            var start = caret;
            var end = caret;
            var wordIdx = last + 1;
            if (last >= 0 && sig[last].End >= caret)
            {
                if (sig[last].IsSymbol('.'))
                    return false;
                if (sig[last].Kind == TokenKind.Word)
                {
                    if (sig[last].Text.StartsWith("#", StringComparison.Ordinal))
                        return false;
                    start = sig[last].Start;
                    end = sig[last].End;
                    wordIdx = last;
                }
                // any other token (a bracket, an operator, a number): the caret is just after it, the word is empty
            }

            // a.Name| : a column, not a keyword
            if (wordIdx > 0 && wordIdx < sig.Count && wordIdx <= last && sig[wordIdx - 1].IsSymbol('.') && sig[wordIdx - 1].End == sig[wordIdx].Start)
                return false;

            var prev = wordIdx - 1;
            var scopeStart = FindScopeStart(sig, start, out var keyword, out var open);
            if (open.Count > 0)
                return false;           // inside a function call or a list: not for keywords

            // GO on a line of its own ends the batch
            for (var i = prev; i >= scopeStart; i--)
            {
                if (sig[i].IsKeyword("GO") && IsAloneOnLine(text, sig[i]))
                {
                    scopeStart = i + 1;
                    if (scopeStart <= prev && sig[scopeStart].Kind == TokenKind.Number)
                        scopeStart++;                   // GO 5 repeats the batch
                    keyword = null;
                    break;
                }
            }

            List<KeywordSuggestion> suggestions;
            var statementStart = scopeStart > prev && keyword == null;
            if (scopeStart > prev)
            {
                // Nothing of the statement is written yet: after SELECT / UPDATE / DELETE / INSERT only the keyword itself is there
                suggestions = keyword == null ? SqlKeywords.StatementStart() : AfterStatementKeyword(keyword);
            }
            else
            {
                if (keyword == null)
                    return false;       // DECLARE, EXEC, a CTE: not understood, so no guesses

                suggestions = AnalyzeStatement(sig, scopeStart, prev, keyword);
                if (suggestions == null)
                    return false;
            }

            if (suggestions.Count == 0)
                return false;

            context = new KeywordContext { Start = start, End = end, Suggestions = suggestions, IsStatementStart = statementStart };
            return true;
        }

        // Words that are always keywords, used to see in which case the script is written
        private static readonly HashSet<string> CaseProbeWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "FROM", "WHERE", "AND", "OR", "JOIN", "ON", "BY", "GROUP", "ORDER", "SET", "UPDATE", "DELETE", "INSERT", "INTO",
            "VALUES", "INNER", "LEFT", "HAVING", "UNION", "AS", "NOT", "NULL", "IS", "IN", "LIKE",
        };

        /// <summary>
        /// True if the script before the caret writes its keywords in lower case (<c>select ... from</c>), judged by the nearest
        /// keyword, so suggestions can follow the style that is already there.
        /// </summary>
        public static bool PrefersLowercaseKeywords(string text, int caret)
        {
            var sig = Significant(SqlTokenizer.Tokenize(text));
            for (var i = LastIndexBefore(sig, caret); i >= 0; i--)
            {
                var t = sig[i];
                if (t.End >= caret || t.Kind != TokenKind.Word || t.Text.Length < 2 || !CaseProbeWords.Contains(t.Text))
                    continue;
                return t.Text == t.Text.ToLowerInvariant();
            }
            return false;
        }

        private static List<KeywordSuggestion> AfterStatementKeyword(string keyword)
        {
            switch (keyword)
            {
                case "SELECT": return SqlKeywords.SelectStart();
                case "INSERT": return SqlKeywords.ToInto();
                case "DELETE": return SqlKeywords.ToDeleteFrom();
                default: return new List<KeywordSuggestion>();      // UPDATE, MERGE: a table name comes next
            }
        }

        private static bool IsAloneOnLine(string text, SqlToken token)
        {
            var from = token.Start;
            while (from > 0 && text[from - 1] != '\n' && text[from - 1] != '\r')
                from--;
            var to = token.End;
            while (to < text.Length && text[to] != '\n' && text[to] != '\r')
                to++;
            var rest = text.Substring(token.End, to - token.End).Trim();
            return text.Substring(from, token.Start - from).Trim().Length == 0 && (rest.Length == 0 || rest.All(char.IsDigit));
        }

        // ---- reading the statement up to the caret ----

        private static List<KeywordSuggestion> AnalyzeStatement(List<SqlToken> sig, int from, int to, string keyword)
        {
            var clause = keyword;
            var items = new List<SqlToken>();
            var crossJoin = false;
            var depth = 0;
            var directDelete = keyword == "DELETE" && sig[from].IsKeyword("FROM");

            for (var i = from; i <= to; i++)
            {
                var t = sig[i];
                if (t.IsSymbol('('))
                {
                    depth++;
                    continue;
                }
                if (t.IsSymbol(')'))
                {
                    // a whole bracketed group counts as one value
                    if (depth > 0 && --depth == 0)
                        items.Add(t);
                    continue;
                }
                if (depth > 0)
                    continue;

                if (t.Kind == TokenKind.Word && KeywordClauseWords.Contains(t.Text))
                {
                    var word = t.Text.ToUpperInvariant();

                    // SET outside UPDATE is a SET statement
                    if (word == "SET" && keyword != "UPDATE")
                    {
                        items.Add(t);
                        continue;
                    }

                    if (word == "JOIN" || word == "APPLY")
                    {
                        crossJoin = word == "APPLY";
                        // INNER / LEFT / CROSS ... right before JOIN belong to the join, not to the clause before it
                        while (items.Count > 0 && items[items.Count - 1].Kind == TokenKind.Word && JoinModifiers2.Contains(items[items.Count - 1].Text))
                        {
                            if (items[items.Count - 1].IsKeyword("CROSS"))
                                crossJoin = true;
                            items.RemoveAt(items.Count - 1);
                        }
                    }

                    clause = word == "APPLY" ? "JOIN" : word;
                    items = new List<SqlToken>();
                    continue;
                }

                items.Add(t);
            }

            if (depth > 0)
                return null;

            return Evaluate(keyword, clause, items, crossJoin, directDelete);
        }

        // ---- what follows, by clause ----

        private static List<KeywordSuggestion> Evaluate(string keyword, string clause, List<SqlToken> items, bool crossJoin, bool directDelete)
        {
            var none = new List<KeywordSuggestion>();
            var last = items.Count > 0 ? items[items.Count - 1] : default(SqlToken);
            var hasLast = items.Count > 0;

            // INNER / LEFT / CROSS ... typed, JOIN not yet
            if (hasLast && last.Kind == TokenKind.Word && JoinModifiers2.Contains(last.Text) && (clause == "FROM" || clause == "JOIN" || clause == "ON"))
                return SqlKeywords.ToJoin(last.Text);

            // CASE ... inside the clause comes first
            var inCase = EvaluateCase(items);
            if (inCase != null)
                return inCase;

            switch (clause)
            {
                case "SELECT":
                    return EvaluateSelectList(items);

                case "FROM":
                    if (!hasLast || last.IsSymbol(',') || last.IsKeyword("AS"))
                        return none;
                    if (keyword == "DELETE" && directDelete)
                        return SqlKeywords.AfterDeleteTable();
                    return keyword == "SELECT" ? SqlKeywords.AfterTable() : SqlKeywords.AfterTableOfChange();

                case "JOIN":
                    if (!hasLast || last.IsSymbol(',') || last.IsKeyword("AS"))
                        return none;
                    return crossJoin ? SqlKeywords.AfterTable() : SqlKeywords.AfterJoinTable();

                case "ON":
                    return FromCondition(items, SqlKeywords.AfterOnCondition());

                case "WHERE":
                    return FromCondition(items, SqlKeywords.AfterWhereCondition());

                case "HAVING":
                    return FromCondition(items, SqlKeywords.AfterHavingCondition());

                case "GROUP":
                    return EvaluateList(items, SqlKeywords.AfterGroupItems(), null);

                case "ORDER":
                    return EvaluateList(items, SqlKeywords.AfterOrderItem(), SqlKeywords.AfterAscDesc());

                case "OFFSET":
                    if (items.Count == 1 && IsValueToken(last))
                        return SqlKeywords.ToRows();
                    if (items.Count >= 2 && (last.IsKeyword("ROWS") || last.IsKeyword("ROW")))
                        return SqlKeywords.AfterOffset();
                    return none;

                case "SET":
                    return EvaluateSet(items);

                case "UPDATE":
                    return hasLast && IsValueToken(last) ? SqlKeywords.ToSet() : none;

                case "DELETE":
                    return !hasLast || IsValueToken(last) ? SqlKeywords.ToDeleteFrom() : none;

                case "INSERT":
                    return SqlKeywords.ToInto();

                case "INTO":
                    if (!hasLast)
                        return none;
                    if (keyword == "SELECT")
                        return IsValueToken(last) ? SqlKeywords.ToFrom() : none;       // SELECT ... INTO #t
                    if (last.IsSymbol(')'))
                        return SqlKeywords.AfterInsertColumns();
                    return IsValueToken(last) ? SqlKeywords.AfterInsertTable() : none;

                case "UNION":
                    if (!hasLast)
                        return SqlKeywords.AfterSetOperator();
                    return items.Count == 1 && last.IsKeyword("ALL") ? SqlKeywords.ToSelect() : none;

                case "EXCEPT":
                case "INTERSECT":
                    return hasLast ? none : SqlKeywords.ToSelect();

                default:
                    return none;
            }
        }

        private static List<KeywordSuggestion> EvaluateSelectList(List<SqlToken> items)
        {
            var none = new List<KeywordSuggestion>();
            if (items.Count == 0)
                return SqlKeywords.SelectStart();

            var last = items[items.Count - 1];
            if (items.Count == 1 && (last.IsKeyword("DISTINCT") || last.IsKeyword("ALL")))
                return SqlKeywords.AfterDistinct();

            // SELECT TOP 10 | : still waiting for the first column
            var topAt = items.FindIndex(t => t.IsKeyword("TOP"));
            if (topAt >= 0 && topAt <= 1 && items.Skip(topAt + 1).All(t => t.Kind == TokenKind.Number || t.Kind == TokenKind.Variable || t.IsSymbol(')') ||
                                                                           t.IsKeyword("PERCENT") || t.IsKeyword("WITH") || t.IsKeyword("TIES")))
                return none;

            if (last.IsSymbol('*'))
            {
                var before = items.Count >= 2 ? items[items.Count - 2] : default(SqlToken);
                var star = items.Count == 1 || before.IsSymbol(',') || before.IsSymbol('.') || before.IsKeyword("DISTINCT") || before.IsKeyword("ALL") || before.IsSymbol(')');
                return star ? SqlKeywords.ToFrom() : none;
            }

            return IsValueToken(last) ? SqlKeywords.AfterSelectItem() : none;
        }

        // GROUP BY a, b | / ORDER BY a | : the BY, then a list of values
        private static List<KeywordSuggestion> EvaluateList(List<SqlToken> items, List<KeywordSuggestion> afterValue, List<KeywordSuggestion> afterDirection)
        {
            var none = new List<KeywordSuggestion>();
            if (items.Count == 0)
                return SqlKeywords.ToBy();
            if (!items[0].IsKeyword("BY") || items.Count == 1)
                return none;

            var last = items[items.Count - 1];
            if (afterDirection != null && (last.IsKeyword("ASC") || last.IsKeyword("DESC")))
                return afterDirection;
            return IsValueToken(last) ? afterValue : none;
        }

        // SET a = 1, b = | : one assignment at a time
        private static List<KeywordSuggestion> EvaluateSet(List<SqlToken> items)
        {
            var none = new List<KeywordSuggestion>();
            var current = new List<SqlToken>();
            foreach (var t in items)
            {
                if (t.IsSymbol(','))
                    current.Clear();
                else
                    current.Add(t);
            }

            if (current.Count == 0)
                return none;
            var last = current[current.Count - 1];
            if (current.Any(t => t.IsSymbol('=')))
                return IsValueToken(last) && !last.IsSymbol('=') ? SqlKeywords.AfterSetValue() : none;
            return IsValueToken(last) ? SqlKeywords.ToEquals() : none;
        }

        // ---- conditions ----

        private static List<KeywordSuggestion> FromCondition(List<SqlToken> items, List<KeywordSuggestion> whenComplete)
        {
            switch (EvaluateCondition(items))
            {
                case ConditionState.Start: return SqlKeywords.ConditionStart();
                case ConditionState.NotAtStart: return SqlKeywords.AfterNotAtStart();
                case ConditionState.Partial: return SqlKeywords.Operators();
                case ConditionState.Complete: return whenComplete;
                case ConditionState.AfterIs: return SqlKeywords.AfterIs();
                case ConditionState.AfterIsNot: return SqlKeywords.AfterIsNot();
                case ConditionState.AfterNot: return SqlKeywords.AfterNot();
                case ConditionState.NeedBetweenAnd: return SqlKeywords.ToAnd();
                default: return new List<KeywordSuggestion>();
            }
        }

        private static ConditionState EvaluateCondition(List<SqlToken> items)
        {
            // Only what follows the last AND / OR matters; the AND of BETWEEN x AND y is part of its comparison
            var segment = new List<SqlToken>();
            var betweenOpen = false;
            foreach (var t in items)
            {
                if (t.IsKeyword("BETWEEN"))
                {
                    betweenOpen = true;
                    segment.Add(t);
                }
                else if (t.IsKeyword("AND") && betweenOpen)
                {
                    betweenOpen = false;
                    segment.Add(t);
                }
                else if (t.IsKeyword("AND") || t.IsKeyword("OR"))
                {
                    segment.Clear();
                    betweenOpen = false;
                }
                else
                {
                    segment.Add(t);
                }
            }

            if (segment.Count == 0)
                return ConditionState.Start;

            var last = segment[segment.Count - 1];
            if (last.IsKeyword("IS"))
                return ConditionState.AfterIs;
            if (last.IsKeyword("NOT"))
            {
                if (segment.Count == 1)
                    return ConditionState.NotAtStart;
                return segment[segment.Count - 2].IsKeyword("IS") ? ConditionState.AfterIsNot : ConditionState.AfterNot;
            }

            if (!IsValueToken(last))
                return ConditionState.NeedValue;
            if (betweenOpen)
                return ConditionState.NeedBetweenAnd;

            // (a = 1) on its own is a finished condition
            if (segment.Count == 1 && last.IsSymbol(')'))
                return ConditionState.Complete;

            return segment.Any(IsComparison) ? ConditionState.Complete : ConditionState.Partial;
        }

        private static bool IsComparison(SqlToken t) =>
            (t.Kind == TokenKind.Symbol && t.Text.Length == 1 && "=<>!".IndexOf(t.Text[0]) >= 0) ||
            t.IsKeyword("LIKE") || t.IsKeyword("IN") || t.IsKeyword("IS") || t.IsKeyword("BETWEEN") || t.IsKeyword("EXISTS");

        // ---- CASE ----

        // Inside an unfinished CASE: WHEN, then a condition, THEN a value, ELSE, END
        private static List<KeywordSuggestion> EvaluateCase(List<SqlToken> items)
        {
            var open = new Stack<int>();
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].IsKeyword("CASE"))
                    open.Push(i);
                else if (items[i].IsKeyword("END") && open.Count > 0)
                    open.Pop();
            }
            if (open.Count == 0)
                return null;

            var sub = items.Skip(open.Peek() + 1).ToList();

            // the last WHEN / THEN / ELSE of this CASE itself, not of a CASE nested inside it
            var nested = 0;
            var lastKeyword = -1;
            for (var i = 0; i < sub.Count; i++)
            {
                if (sub[i].IsKeyword("CASE"))
                    nested++;
                else if (sub[i].IsKeyword("END"))
                    nested--;
                else if (nested == 0 && (sub[i].IsKeyword("WHEN") || sub[i].IsKeyword("THEN") || sub[i].IsKeyword("ELSE")))
                    lastKeyword = i;
            }

            if (lastKeyword < 0)
                return sub.Count == 0 || IsValueToken(sub[sub.Count - 1]) ? SqlKeywords.ToWhen() : new List<KeywordSuggestion>();

            var after = sub.Skip(lastKeyword + 1).ToList();
            var word = sub[lastKeyword].Text.ToUpperInvariant();
            if (word == "WHEN")
                return FromCondition(after, SqlKeywords.AfterWhenCondition());

            var hasValue = after.Count > 0 && IsValueToken(after[after.Count - 1]);
            if (!hasValue)
                return new List<KeywordSuggestion>();
            return word == "THEN" ? SqlKeywords.AfterThenValue() : SqlKeywords.ToEnd();
        }

        // ---- tokens ----

        private static bool IsValueToken(SqlToken t)
        {
            switch (t.Kind)
            {
                case TokenKind.Number:
                case TokenKind.String:
                case TokenKind.Variable:
                case TokenKind.QuotedIdentifier:
                    return true;
                case TokenKind.Word:
                    return !NotValueWords.Contains(t.Text);
                case TokenKind.Symbol:
                    return t.IsSymbol(')');     // a star is only a value in a select list, which is handled there
                default:
                    return false;
            }
        }
    }
}
