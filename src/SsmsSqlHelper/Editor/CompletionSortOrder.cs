using System;
using SsmsSqlHelper.Metadata;

namespace SsmsSqlHelper.Editor
{
    /// <summary>Sort groups shared by keyword and column completion sources.</summary>
    internal static class CompletionSortOrder
    {
        public static string ObjectNameWord(string typedName)
        {
            var parts = SqlIdentifier.Split(typedName ?? "");
            return parts[parts.Count - 1];
        }

        public static int ObjectMatch(string name, string word)
        {
            if (string.IsNullOrEmpty(word))
                return 0;
            if (string.Equals(name, word, StringComparison.OrdinalIgnoreCase))
                return 0;
            if (name.StartsWith(word, StringComparison.OrdinalIgnoreCase))
                return 1;
            return name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0 ? 2 : 3;
        }

        public static string Keyword(string text, string typedWord, int index)
        {
            // At an empty expression, every column precedes keywords and functions.
            // Once a word is typed, matching suggestions move above columns.
            var matching = !string.IsNullOrEmpty(typedWord) &&
                           text.StartsWith(typedWord, StringComparison.OrdinalIgnoreCase);
            return (matching ? "0_" : "2_") + index.ToString("D4");
        }

        public static string Column(int tableOrder, int ordinal) =>
            "1_" + tableOrder.ToString("D4") + ordinal.ToString("D5");
    }
}
