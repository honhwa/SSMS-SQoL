using System;
using System.Collections.Generic;

namespace SsmsSqlHelper.Parsing
{
    internal readonly struct BracketColorSpan
    {
        public BracketColorSpan(int start, int level)
        {
            Start = start;
            Level = level;
        }

        public int Start { get; }
        public int Level { get; }
    }

    /// <summary>Finds matched SQL delimiters without treating strings or comments as code.</summary>
    internal static class BracketColorizer
    {
        public static IReadOnlyList<BracketColorSpan> Find(string sql)
        {
            if (sql == null) throw new ArgumentNullException(nameof(sql));

            var result = new List<BracketColorSpan>();
            var open = new Stack<(char Kind, int Start, int Level)>();
            foreach (var token in SqlTokenizer.Tokenize(sql))
            {
                if (token.Kind == TokenKind.QuotedIdentifier && token.Text.Length >= 2 &&
                    token.Text[0] == '[' && HasClosingSquareBracket(token.Text))
                {
                    // [name] is one SQL identifier. Escaped ]] inside it are not delimiters.
                    result.Add(new BracketColorSpan(token.Start, open.Count));
                    result.Add(new BracketColorSpan(token.End - 1, open.Count));
                }
                else if (token.Kind == TokenKind.Symbol && token.Text.Length == 1)
                {
                    var c = token.Text[0];
                    if (c == '(' || c == '{')
                        open.Push((c, token.Start, open.Count));
                    else if ((c == ')' || c == '}') && open.Count > 0 &&
                             open.Peek().Kind == (c == ')' ? '(' : '{'))
                    {
                        var pair = open.Pop();
                        result.Add(new BracketColorSpan(pair.Start, pair.Level));
                        result.Add(new BracketColorSpan(token.Start, pair.Level));
                    }
                }
            }

            result.Sort((a, b) => a.Start.CompareTo(b.Start));
            return result;
        }

        private static bool HasClosingSquareBracket(string text)
        {
            var trailing = 0;
            for (var i = text.Length - 1; i > 0 && text[i] == ']'; i--)
                trailing++;
            // Pairs of ]] escape an identifier character; an odd final ] closes it.
            return trailing % 2 == 1;
        }
    }
}
