using System;

namespace SsmsSqlHelper.Editor
{
    /// <summary>Sort groups shared by keyword and column completion sources.</summary>
    internal static class CompletionSortOrder
    {
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
