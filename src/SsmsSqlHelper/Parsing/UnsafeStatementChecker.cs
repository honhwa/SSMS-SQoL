using System;
using System.Collections.Generic;
using System.Linq;

namespace SsmsSqlHelper.Parsing
{
    internal sealed class UnsafeStatement
    {
        public UnsafeStatement(string keyword, int line, int offset, string text)
        {
            Keyword = keyword;
            Line = line;
            Offset = offset;
            Text = text;
        }

        /// <summary>UPDATE or DELETE.</summary>
        public string Keyword { get; }

        /// <summary>1-based line of the keyword.</summary>
        public int Line { get; }
        public int Offset { get; }

        /// <summary>The start of the statement, for the warning.</summary>
        public string Text { get; }
    }

    /// <summary>
    /// Finds UPDATE and DELETE statements that have no WHERE and would touch every row. Errs towards staying quiet: anything
    /// that is not clearly such a statement (FK actions, triggers and procedures being created, MERGE branches, GRANT lists,
    /// temp tables and table variables, TOP batches) is left alone. No Visual Studio dependencies.
    /// </summary>
    internal static class UnsafeStatementChecker
    {
        // Words that can only start a new statement; seeing one at the top level ends the UPDATE/DELETE we are looking at
        private static readonly HashSet<string> StatementStarts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "EXEC", "EXECUTE", "DECLARE", "IF", "WHILE", "BEGIN", "END", "PRINT",
            "RETURN", "CREATE", "DROP", "ALTER", "TRUNCATE", "USE", "RAISERROR", "THROW", "COMMIT", "ROLLBACK", "GOTO", "BREAK",
            "CONTINUE", "OPEN", "CLOSE", "FETCH", "DEALLOCATE", "BACKUP", "RESTORE", "GRANT", "DENY", "REVOKE", "WAITFOR", "ELSE",
        };

        // The word before UPDATE/DELETE shows it is not the start of a data-changing statement
        private static readonly HashSet<string> NotAStatementAfter = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ON",       // ON DELETE CASCADE
            "THEN",     // MERGE ... WHEN MATCHED THEN UPDATE
            "FOR",      // cursor FOR UPDATE, trigger FOR UPDATE
            "AFTER", "OF", "GRANT", "DENY", "REVOKE",
        };

        private static readonly HashSet<string> DefinitionKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PROC", "PROCEDURE", "FUNCTION", "TRIGGER", "VIEW",
        };

        /// <param name="firstLine">The line number of the first line of <paramref name="text"/>, when it is only a part of a document.</param>
        public static List<UnsafeStatement> Find(string text, int firstLine = 1)
        {
            var result = new List<UnsafeStatement>();
            var sig = SqlTokenizer.Tokenize(text).Where(t => !t.IsTrivia).ToList();

            foreach (var (from, to) in SplitBatches(text, sig))
            {
                if (IsDefinition(sig, from, to))
                    continue;

                for (var i = from; i < to; i++)
                {
                    var t = sig[i];
                    if (t.Kind != TokenKind.Word || !(t.IsKeyword("UPDATE") || t.IsKeyword("DELETE")))
                        continue;

                    if (!IsDataChange(sig, i, from, to) || HasWhere(sig, i, to))
                        continue;

                    result.Add(new UnsafeStatement(t.Text.ToUpperInvariant(), firstLine + CountLines(text, t.Start), t.Start, Preview(text, t.Start)));
                }
            }
            return result;
        }

        // A batch ends at a GO on a line of its own
        private static List<(int From, int To)> SplitBatches(string text, List<SqlToken> sig)
        {
            var batches = new List<(int, int)>();
            var from = 0;
            for (var i = 0; i < sig.Count; i++)
            {
                if (sig[i].IsKeyword("GO") && IsOnItsOwnLine(text, sig[i]))
                {
                    batches.Add((from, i));
                    from = i + 1;
                }
            }
            batches.Add((from, sig.Count));
            return batches;
        }

        private static bool IsOnItsOwnLine(string text, SqlToken go)
        {
            var lineStart = go.Start;
            while (lineStart > 0 && text[lineStart - 1] != '\n' && text[lineStart - 1] != '\r')
                lineStart--;
            if (text.Substring(lineStart, go.Start - lineStart).Trim().Length != 0)
                return false;

            // "GO" or "GO 5"; anything else on the line (GO TO label) is not a separator
            var lineEnd = go.End;
            while (lineEnd < text.Length && text[lineEnd] != '\n' && text[lineEnd] != '\r')
                lineEnd++;
            var rest = text.Substring(go.End, lineEnd - go.End).Trim();
            return rest.Length == 0 || rest.All(char.IsDigit);
        }

        // CREATE/ALTER PROCEDURE|FUNCTION|TRIGGER|VIEW: the body is stored, not run
        private static bool IsDefinition(List<SqlToken> sig, int from, int to)
        {
            if (from >= to || !(sig[from].IsKeyword("CREATE") || sig[from].IsKeyword("ALTER")))
                return false;

            for (var i = from + 1; i < to && i <= from + 4; i++)
            {
                if (sig[i].Kind == TokenKind.Word && DefinitionKinds.Contains(sig[i].Text))
                    return true;
            }
            return false;
        }

        private static bool IsDataChange(List<SqlToken> sig, int i, int from, int to)
        {
            if (i > from)
            {
                var prev = sig[i - 1];
                if (prev.IsSymbol(',') || (prev.Kind == TokenKind.Word && NotAStatementAfter.Contains(prev.Text)))
                    return false;
            }

            if (i + 1 >= to)
                return true;

            var next = sig[i + 1];
            if (next.IsKeyword("STATISTICS") || next.IsSymbol('(') || next.IsKeyword("TOP"))
                return false;                       // UPDATE STATISTICS, IF UPDATE(col), UPDATE TOP (n): deliberate or not a statement

            // Temp tables and table variables are scratch space; emptying them without a WHERE is routine
            var target = sig[i].IsKeyword("DELETE") && next.IsKeyword("FROM") && i + 2 < to ? sig[i + 2] : next;
            return !(target.Kind == TokenKind.Variable || (target.Kind == TokenKind.Word && target.Text.StartsWith("#", StringComparison.Ordinal)));
        }

        private static bool HasWhere(List<SqlToken> sig, int i, int to)
        {
            var isUpdate = sig[i].IsKeyword("UPDATE");
            var seenSet = false;
            var depth = 0;

            for (var j = i + 1; j < to; j++)
            {
                var t = sig[j];
                if (t.IsSymbol('('))
                {
                    depth++;
                    continue;
                }
                if (t.IsSymbol(')'))
                {
                    if (--depth < 0)
                        return false;
                    continue;
                }
                if (depth > 0)
                    continue;
                if (t.IsSymbol(';'))
                    return false;
                if (t.Kind != TokenKind.Word)
                    continue;

                if (t.IsKeyword("WHERE"))
                    return true;

                if (t.IsKeyword("SET"))
                {
                    // UPDATE ... SET happens once; another SET (SET @x = 1, SET NOCOUNT ON) is the next statement
                    if (!isUpdate || seenSet)
                        return false;
                    seenSet = true;
                    continue;
                }

                if (t.IsKeyword("WITH"))
                {
                    // WITH (NOLOCK) is a table hint; WITH name AS (...) starts a CTE and with it a new statement
                    if (j + 1 < to && sig[j + 1].IsSymbol('('))
                        continue;
                    return false;
                }

                if (StatementStarts.Contains(t.Text))
                    return false;
            }
            return false;
        }

        private static int CountLines(string text, int offset)
        {
            var lines = 0;
            for (var i = 0; i < offset && i < text.Length; i++)
            {
                if (text[i] == '\n')
                    lines++;
                else if (text[i] == '\r' && !(i + 1 < text.Length && text[i + 1] == '\n'))
                    lines++;
            }
            return lines;
        }

        private static string Preview(string text, int offset)
        {
            var end = offset;
            while (end < text.Length && text[end] != '\n' && text[end] != '\r')
                end++;
            var line = text.Substring(offset, end - offset).Trim();
            return line.Length > 80 ? line.Substring(0, 77) + "..." : line;
        }
    }
}
