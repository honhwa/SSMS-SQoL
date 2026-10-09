using System.Collections.Generic;

namespace SsmsSqlHelper.Parsing
{
    internal readonly struct SqlFunctionCall
    {
        public SqlFunctionCall(SqlFunctionSignature signature, int nameStart, int openStart, int argumentIndex)
        {
            Signature = signature;
            NameStart = nameStart;
            OpenStart = openStart;
            ArgumentIndex = argumentIndex;
        }

        public SqlFunctionSignature Signature { get; }
        public int NameStart { get; }
        public int OpenStart { get; }
        public int ArgumentIndex { get; }
    }

    internal static class SqlFunctionCallParser
    {
        private sealed class Frame
        {
            public SqlFunctionSignature Signature;
            public int NameStart;
            public int OpenStart;
            public int ArgumentIndex;
        }

        public static bool TryFindActive(string sql, int caret, out SqlFunctionCall call)
        {
            call = default;
            if (sql == null || caret < 0 || caret > sql.Length)
                return false;

            var tokens = SqlTokenizer.Tokenize(sql);
            var stack = new Stack<Frame>();
            SqlToken previous = default;
            foreach (var token in tokens)
            {
                if (token.Start >= caret)
                    break;
                if ((token.Kind == TokenKind.String || token.Kind == TokenKind.Comment || token.Kind == TokenKind.QuotedIdentifier) &&
                    token.Start < caret && caret < token.End)
                    return false;
                if (token.IsTrivia)
                    continue;

                if (token.IsSymbol('('))
                {
                    var signature = previous.Kind == TokenKind.Word ? SqlFunctionSignatures.Find(previous.Text) : null;
                    stack.Push(new Frame { Signature = signature, NameStart = previous.Start, OpenStart = token.Start });
                }
                else if (token.IsSymbol(')'))
                {
                    if (stack.Count > 0)
                        stack.Pop();
                }
                else if (stack.Count > 0 && token.IsSymbol(','))
                {
                    stack.Peek().ArgumentIndex++;
                }
                else if (stack.Count > 0 && token.IsKeyword("AS") &&
                         (stack.Peek().Signature?.Name == "CAST" || stack.Peek().Signature?.Name == "TRY_CAST"))
                {
                    stack.Peek().ArgumentIndex = 1;
                }
                previous = token;
            }

            if (stack.Count == 0 || stack.Peek().Signature == null)
                return false;
            var frame = stack.Peek();
            call = new SqlFunctionCall(frame.Signature, frame.NameStart, frame.OpenStart, frame.ArgumentIndex);
            return true;
        }
    }
}
