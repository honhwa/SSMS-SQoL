using System;

namespace SsmsSqlHelper.Snippets
{
    internal sealed class SnippetExpansion
    {
        private static readonly System.Collections.Generic.IReadOnlyList<SnippetStop> NoStops = new SnippetStop[0];

        public SnippetExpansion(int replaceStart, int replaceLength, string text, int caretOffset,
            System.Collections.Generic.IReadOnlyList<SnippetStop> stops = null, int? finalOffset = null)
        {
            ReplaceStart = replaceStart;
            ReplaceLength = replaceLength;
            Text = text;
            CaretOffset = caretOffset;
            Stops = stops ?? NoStops;
            FinalOffset = finalOffset ?? caretOffset;
        }

        /// <summary>Start of the shortcut, relative to the start of the line.</summary>
        public int ReplaceStart { get; }
        public int ReplaceLength { get; }
        public string Text { get; }
        /// <summary>Caret position after expansion, relative to <see cref="ReplaceStart"/>: the first tab stop if there is one.</summary>
        public int CaretOffset { get; }

        /// <summary>Tab stops in visiting order (offsets relative to the start of <see cref="Text"/>); empty for plain snippets.</summary>
        public System.Collections.Generic.IReadOnlyList<SnippetStop> Stops { get; }

        /// <summary>Where the caret goes after the last stop.</summary>
        public int FinalOffset { get; }
    }

    /// <summary>
    /// Editor-independent expansion logic: works on plain line text so it can be unit tested
    /// without Visual Studio.
    /// </summary>
    internal static class SnippetExpander
    {
        public const string CursorMarker = SnippetTemplate.CursorMarker;

        /// <param name="lineTextBeforeCaret">Text from the start of the line up to the caret.</param>
        /// <param name="lineIndent">Leading whitespace of the line; applied to every body line after the first.</param>
        /// <param name="newLine">Line break used by the document.</param>
        /// <param name="lookup">Returns the snippet for a shortcut, or null.</param>
        public static SnippetExpansion TryExpand(string lineTextBeforeCaret, string lineIndent, string newLine, Func<string, Snippet> lookup,
            Func<string, string> variable = null)
        {
            if (!TryFindShortcut(lineTextBeforeCaret, allowEmpty: false, out var wordStart))
                return null;

            var shortcut = lineTextBeforeCaret.Substring(wordStart);
            var snippet = lookup(shortcut);
            if (snippet?.Body == null)
                return null;

            var rendered = Render(snippet, lineIndent, newLine, variable);
            return new SnippetExpansion(wordStart, shortcut.Length, rendered.Text, rendered.CaretOffset, rendered.Stops, rendered.FinalOffset);
        }

        /// <summary>Whether a fully typed shortcut should take Tab ahead of an open completion list.</summary>
        public static bool HasExactShortcut(string lineTextBeforeCaret, Func<string, Snippet> lookup) =>
            TryFindShortcut(lineTextBeforeCaret, allowEmpty: false, out var wordStart) &&
            lookup(lineTextBeforeCaret.Substring(wordStart)) != null;

        /// <summary>
        /// Finds where the shortcut being typed starts, if a snippet may be expanded at the caret at all: the word
        /// must start at a boundary and must not sit inside a string literal, a comment or a qualified name.
        /// </summary>
        /// <param name="allowEmpty">Accept an empty word (nothing typed yet), e.g. for an explicit Ctrl+Space.</param>
        public static bool TryFindShortcut(string lineTextBeforeCaret, bool allowEmpty, out int wordStart)
        {
            wordStart = FindWordStart(lineTextBeforeCaret);
            if (wordStart == lineTextBeforeCaret.Length && !allowEmpty)
                return false;

            if (wordStart > 0 && !IsBoundary(lineTextBeforeCaret[wordStart - 1]))
                return false;

            return !IsInsideStringOrComment(lineTextBeforeCaret.Substring(0, wordStart));
        }

        /// <summary>
        /// The text a snippet inserts (indented to the line, with the document's line breaks), the tab stops inside it and where
        /// the caret goes. <paramref name="variable"/> supplies values for <c>$DATE$</c>, <c>$SELECTED$</c>, ...
        /// </summary>
        public static SnippetExpansion Render(Snippet snippet, string lineIndent, string newLine, Func<string, string> variable = null)
        {
            var rendered = SnippetTemplate.Render(snippet.Body ?? "", lineIndent, newLine, variable);
            return new SnippetExpansion(0, 0, rendered.Text, rendered.InitialCaretOffset, rendered.Stops, rendered.FinalOffset);
        }

        public static string GetIndent(string lineText)
        {
            var i = 0;
            while (i < lineText.Length && (lineText[i] == ' ' || lineText[i] == '\t'))
                i++;
            return lineText.Substring(0, i);
        }

        private static int FindWordStart(string text)
        {
            var i = text.Length;
            while (i > 0 && (char.IsLetterOrDigit(text[i - 1]) || text[i - 1] == '_'))
                i--;
            return i;
        }

        // Rejects e.g. "dbo.ssf", "@ssf", "#ssf", "[ssf" so identifiers are never expanded
        private static bool IsBoundary(char c) =>
            char.IsWhiteSpace(c) || c == '(' || c == ',' || c == ';' || c == '=';

        private static bool IsInsideStringOrComment(string prefix)
        {
            var inString = false;
            for (var i = 0; i < prefix.Length; i++)
            {
                if (prefix[i] == '\'')
                    inString = !inString;
                else if (!inString && prefix[i] == '-' && i + 1 < prefix.Length && prefix[i + 1] == '-')
                    return true;
            }
            return inString;
        }
    }
}
