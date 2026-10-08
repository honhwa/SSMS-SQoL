using System.Collections.Generic;

namespace SsmsSqlHelper.Parsing
{
    internal enum TokenKind
    {
        Word,
        QuotedIdentifier,
        String,
        Number,
        Variable,
        Symbol,
        Comment,
        WhiteSpace,
    }

    internal readonly struct SqlToken
    {
        public SqlToken(TokenKind kind, int start, string text)
        {
            Kind = kind;
            Start = start;
            Text = text;
        }

        public TokenKind Kind { get; }
        public int Start { get; }
        public string Text { get; }
        public int End => Start + Text.Length;

        public bool IsTrivia => Kind == TokenKind.WhiteSpace || Kind == TokenKind.Comment;
        public bool IsNamePart => Kind == TokenKind.Word || Kind == TokenKind.QuotedIdentifier;

        public bool IsKeyword(string keyword) =>
            Kind == TokenKind.Word && string.Equals(Text, keyword, System.StringComparison.OrdinalIgnoreCase);

        public bool IsSymbol(char c) => Kind == TokenKind.Symbol && Text.Length == 1 && Text[0] == c;

        public override string ToString() => $"{Kind}:{Text}";
    }

    /// <summary>
    /// Lenient T-SQL lexer. Never throws: unterminated strings, comments and brackets
    /// simply run to the end of the text, which is what we want for half-typed queries.
    /// </summary>
    internal static class SqlTokenizer
    {
        public static List<SqlToken> Tokenize(string text)
        {
            var tokens = new List<SqlToken>();
            var i = 0;
            while (i < text.Length)
            {
                var start = i;
                var c = text[i];
                var next = i + 1 < text.Length ? text[i + 1] : '\0';
                TokenKind kind;

                if (char.IsWhiteSpace(c))
                {
                    while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                    kind = TokenKind.WhiteSpace;
                }
                else if (c == '-' && next == '-')
                {
                    while (i < text.Length && text[i] != '\n' && text[i] != '\r') i++;
                    kind = TokenKind.Comment;
                }
                else if (c == '/' && next == '*')
                {
                    i = SkipBlockComment(text, i);
                    kind = TokenKind.Comment;
                }
                else if (c == '\'' || ((c == 'N' || c == 'n') && next == '\''))
                {
                    i = SkipDelimited(text, c == '\'' ? i : i + 1, '\'');
                    kind = TokenKind.String;
                }
                else if (c == '[')
                {
                    i = SkipDelimited(text, i, ']');
                    kind = TokenKind.QuotedIdentifier;
                }
                else if (c == '"')
                {
                    i = SkipDelimited(text, i, '"');
                    kind = TokenKind.QuotedIdentifier;
                }
                else if (c == '@')
                {
                    i++;
                    while (i < text.Length && IsWordChar(text[i])) i++;
                    kind = TokenKind.Variable;
                }
                else if (char.IsLetter(c) || c == '_' || c == '#')
                {
                    while (i < text.Length && IsWordChar(text[i])) i++;
                    kind = TokenKind.Word;
                }
                else if (char.IsDigit(c))
                {
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '.')) i++;
                    kind = TokenKind.Number;
                }
                else
                {
                    i++;
                    kind = TokenKind.Symbol;
                }

                tokens.Add(new SqlToken(kind, start, text.Substring(start, i - start)));
            }
            return tokens;
        }

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '#' || c == '$' || c == '@';

        /// <summary>Skips from an opening delimiter to just after its close; a doubled close is an escape.</summary>
        private static int SkipDelimited(string text, int open, char close)
        {
            var i = open + 1;
            while (i < text.Length)
            {
                if (text[i] == close)
                {
                    if (i + 1 < text.Length && text[i + 1] == close)
                    {
                        i += 2;
                        continue;
                    }
                    return i + 1;
                }
                i++;
            }
            return i;
        }

        // T-SQL block comments nest
        private static int SkipBlockComment(string text, int i)
        {
            var depth = 0;
            while (i < text.Length)
            {
                if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    depth++;
                    i += 2;
                }
                else if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    i += 2;
                    if (--depth == 0)
                        return i;
                }
                else
                {
                    i++;
                }
            }
            return i;
        }
    }
}
