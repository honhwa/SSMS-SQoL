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

    internal readonly struct BracketPair
    {
        public BracketPair(int open, int close, int level)
        {
            Open = open;
            Close = close;
            Level = level;
        }

        public int Open { get; }
        public int Close { get; }
        public int Level { get; }
    }

    /// <summary>Finds matched SQL delimiters without treating strings or comments as code.</summary>
    internal static class BracketColorizer
    {
        public static IReadOnlyList<BracketColorSpan> Find(string sql)
        {
            var result = new List<BracketColorSpan>();
            foreach (var pair in FindPairs(sql))
            {
                result.Add(new BracketColorSpan(pair.Open, pair.Level));
                result.Add(new BracketColorSpan(pair.Close, pair.Level));
            }
            result.Sort((a, b) => a.Start.CompareTo(b.Start));
            return result;
        }

        public static IReadOnlyList<BracketPair> FindPairs(string sql)
        {
            if (sql == null) throw new ArgumentNullException(nameof(sql));

            var result = new List<BracketPair>();
            var open = new Stack<(char Kind, int Start, int Level)>();
            foreach (var token in SqlTokenizer.Tokenize(sql))
            {
                if (token.Kind == TokenKind.QuotedIdentifier && token.Text.Length >= 2 &&
                    token.Text[0] == '[' && HasClosingSquareBracket(token.Text))
                {
                    // [name] is one SQL identifier. Escaped ]] inside it are not delimiters.
                    result.Add(new BracketPair(token.Start, token.End - 1, open.Count));
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
                        result.Add(new BracketPair(pair.Start, token.Start, pair.Level));
                    }
                }
            }

            return result;
        }

        public static IReadOnlyDictionary<int, BracketPair> IndexPairs(string sql)
        {
            var index = new Dictionary<int, BracketPair>();
            foreach (var pair in FindPairs(sql))
            {
                index[pair.Open] = pair;
                index[pair.Close] = pair;
            }
            return index;
        }

        public static bool TryGetPairAtCaret(IReadOnlyDictionary<int, BracketPair> index, int caret, out BracketPair pair)
        {
            // Prefer the character under the caret; otherwise use the one immediately before it.
            if (index.TryGetValue(caret, out pair))
                return true;
            if (caret > 0 && index.TryGetValue(caret - 1, out pair))
                return true;
            pair = default;
            return false;
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
