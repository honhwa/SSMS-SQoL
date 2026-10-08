using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using SsmsSqlHelper.Metadata;
using SsmsSqlHelper.Ssms;

namespace SsmsSqlHelper.Editor
{
    /// <summary>A slice of the document around the caret, so parsing cost doesn't grow with file size.</summary>
    internal readonly struct TextWindow
    {
        public TextWindow(int start, string text)
        {
            Start = start;
            Text = text;
        }

        public int Start { get; }
        public string Text { get; }

        /// <summary>Takes up to <paramref name="before"/>/<paramref name="after"/> characters, starting at a line boundary.</summary>
        public static TextWindow Around(ITextSnapshot snapshot, int position, int before, int after)
        {
            var start = snapshot.GetLineFromPosition(Math.Max(0, position - before)).Start.Position;
            var end = Math.Min(snapshot.Length, position + after);
            return new TextWindow(start, snapshot.GetText(start, end - start));
        }
    }

    internal static class EditorContext
    {
        /// <summary>Metadata for the active query window, or null if not connected or still loading.</summary>
        public static DbMetadata GetMetadata()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return MetadataService.Instance.TryGet(SsmsConnectionAdapter.GetActiveConnection());
        }

        public static string GetNewLine(ITextSnapshotLine line) =>
            line.LineBreakLength > 0 ? line.GetLineBreakText() : Environment.NewLine;
    }
}
