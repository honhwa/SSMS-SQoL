namespace SsmsSqlHelper.Parsing
{
    internal static partial class SqlContext
    {
        /// <summary>The database name after USE, empty or partially typed.</summary>
        public static bool TryGetDatabaseNameSpan(string text, int caret, out int start, out int end)
        {
            start = end = caret;
            var tokens = SqlTokenizer.Tokenize(text);
            if (IsInsideTrivia(tokens, caret))
                return false;

            var sig = Significant(tokens);
            var last = LastIndexBefore(sig, caret);
            var nameIndex = last + 1;
            if (last >= 0 && sig[last].End >= caret && sig[last].IsNamePart &&
                !sig[last].IsKeyword("USE"))
            {
                nameIndex = last;
                start = sig[last].Start;
                end = sig[last].End;
            }

            var previous = nameIndex - 1;
            return previous >= 0 && sig[previous].IsKeyword("USE") &&
                   (nameIndex <= last || caret > sig[previous].End);
        }
    }
}
