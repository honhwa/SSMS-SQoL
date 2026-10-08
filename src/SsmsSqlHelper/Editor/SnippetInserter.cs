using System;
using System.Windows;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SsmsSqlHelper.Diagnostics;
using SsmsSqlHelper.Snippets;
using SsmsSqlHelper.Ssms;

namespace SsmsSqlHelper.Editor
{
    /// <summary>Puts a snippet into the editor: text, caret or tab stops, variables. Every way of using a snippet ends up here.</summary>
    internal static class SnippetInserter
    {
        /// <summary>
        /// Values for <c>$DATE$</c>, <c>$USER$</c>, <c>$SELECTED$</c>, ... Looked up only when the body uses them, because
        /// some (the connection, the clipboard) are not free to read.
        /// </summary>
        public static Func<string, string> Variables(string selectedText = "")
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return name =>
            {
                switch (name)
                {
                    case SnippetTemplate.Selected: return selectedText;
                    case "DATE": return DateTime.Now.ToString("yyyy-MM-dd");
                    case "TIME": return DateTime.Now.ToString("HH:mm:ss");
                    case "DATETIME": return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    case "USER": return Environment.UserName;
                    case "MACHINE": return Environment.MachineName;
                    case "SERVER": return SsmsConnectionAdapter.GetActiveConnection()?.Server ?? "";
                    case "DATABASE": return SsmsConnectionAdapter.GetActiveConnection()?.Database ?? "";
                    case "CLIPBOARD": return ReadClipboard();
                    default: return "";
                }
            };
        }

        /// <summary>Replaces <paramref name="replace"/> with an already expanded snippet and starts its tab stops.</summary>
        /// <param name="expansion">Offsets inside it are relative to the start of its text (<see cref="SnippetExpansion.ReplaceStart"/> is ignored).</param>
        public static void Apply(IWpfTextView view, Span replace, SnippetExpansion expansion)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            SnippetSession.End(view);

            var snapshot = view.TextBuffer.Replace(replace, expansion.Text);
            if (expansion.Stops.Count > 0)
            {
                SnippetSession.Begin(view, replace.Start, expansion.Stops, expansion.FinalOffset);
                return;
            }

            view.Selection.Clear();
            view.Caret.MoveTo(new SnapshotPoint(snapshot, replace.Start + expansion.FinalOffset));
        }

        /// <summary>Expands <paramref name="snippet"/> in place of <paramref name="replace"/>, indented like the line it lands on.</summary>
        public static void Insert(IWpfTextView view, Span replace, Snippet snippet, string selectedText = "")
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var line = view.TextSnapshot.GetLineFromPosition(replace.Start);
            var expansion = SnippetExpander.Render(snippet, SnippetExpander.GetIndent(line.GetText()), EditorContext.GetNewLine(line), Variables(selectedText));
            Apply(view, replace, expansion);
        }

        private static string ReadClipboard()
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : "";
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException || ex is InvalidOperationException)
            {
                // The clipboard is briefly locked by other programs now and then
                Log.Error("Could not read the clipboard for $CLIPBOARD$", ex);
                return "";
            }
        }
    }
}
