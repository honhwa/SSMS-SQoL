using System.Runtime.InteropServices;
using System;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SsmsSqlHelper.Diagnostics;
using SsmsSqlHelper.Generation;
using SsmsSqlHelper.Parsing;
using SsmsSqlHelper.Settings;
using SsmsSqlHelper.Snippets;
using SsmsSqlHelper.UI;

namespace SsmsSqlHelper.Editor
{
    /// <summary>
    /// Intercepts Tab in SQL editors. In order: schema-aware expansion (INSERT/UPDATE/SELECT *),
    /// then snippet shortcuts, otherwise a normal Tab.
    /// </summary>
    internal sealed class SnippetCommandFilter : IOleCommandTarget
    {
        private const int WindowBefore = 8000;
        private const int WindowAfter = 20000;

        private readonly IWpfTextView _view;
        private readonly ICompletionBroker _completionBroker;
        private readonly IAsyncCompletionBroker _asyncCompletionBroker;

        public SnippetCommandFilter(IWpfTextView view, ICompletionBroker completionBroker, IAsyncCompletionBroker asyncCompletionBroker)
        {
            _view = view;
            _completionBroker = completionBroker;
            _asyncCompletionBroker = asyncCompletionBroker;
        }

        public IOleCommandTarget Next { get; set; }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (pguidCmdGroup == VSConstants.VSStd2K)
            {
                switch ((VSConstants.VSStd2KCmdID)nCmdID)
                {
                    case VSConstants.VSStd2KCmdID.TAB:
                        if (HandleTab())
                            return VSConstants.S_OK;
                        break;

                    case VSConstants.VSStd2KCmdID.BACKTAB:
                        if (!IsCompletionActive() && SnippetSession.Get(_view)?.MovePrevious() == true)
                            return VSConstants.S_OK;
                        break;

                    case VSConstants.VSStd2KCmdID.CANCEL:
                        // Esc leaves the tab stops; it still does whatever it normally does (closing a list, ...)
                        SnippetSession.End(_view);
                        break;

                    case VSConstants.VSStd2KCmdID.COMPLETEWORD:
                    case VSConstants.VSStd2KCmdID.SHOWMEMBERLIST:
                        // Ctrl+Space / Ctrl+J: ours first; when none of our lists applies SSMS still gets the key
                        if (!IsCompletionActive() && CompletionLauncher.TryInvoke(_asyncCompletionBroker, _view))
                            return VSConstants.S_OK;
                        break;

                    case VSConstants.VSStd2KCmdID.TYPECHAR:
                        // "bl" opened a list for the bare word; a dot turns it into "bl." whose list is a different one (the
                        // columns of that table only), so the old session is closed and the right one opened
                        if (pvaIn != IntPtr.Zero && (char)(ushort)Marshal.GetObjectForNativeVariant(pvaIn) == '.')
                        {
                            _asyncCompletionBroker.GetSession(_view)?.Dismiss();
                            var typed = Next.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
                            if (ErrorHandler.Succeeded(typed))
                                CompletionLauncher.ShowAfterDeletionSoon(_asyncCompletionBroker, _view);
                            return typed;
                        }
                        break;

                    case VSConstants.VSStd2KCmdID.BACKSPACE:
                    case VSConstants.VSStd2KCmdID.DELETE:
                    {
                        // Let the editor remove the text, then offer suggestions again for what is left
                        var result = Next.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
                        if (ErrorHandler.Succeeded(result))
                            CompletionLauncher.ShowAfterDeletionSoon(_asyncCompletionBroker, _view);
                        return result;
                    }
                }
            }

            return Next.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return Next.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
        }

        private bool HandleTab()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Inside an expanded snippet Tab goes to the next stop (whose placeholder is selected, so this must come before the selection check)
            if (!IsCompletionActive() && SnippetSession.Get(_view)?.MoveNext() == true)
                return true;

            // Let Tab commit an open completion list or indent a selection as usual
            if (!_view.Selection.IsEmpty || IsCompletionActive())
                return false;

            try
            {
                return TryContextExpand() || TrySnippetExpand();
            }
            catch (Exception ex)
            {
                Log.Error("Tab expansion failed", ex);
                return false;
            }
        }

        private bool TryContextExpand()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var caret = _view.Caret.Position.BufferPosition;
            var window = TextWindow.Around(caret.Snapshot, caret.Position, WindowBefore, WindowAfter);
            var caretInWindow = caret.Position - window.Start;

            // Cheap syntax check first so we only look up the connection when it matters
            var context = SqlContext.GetTabContext(window.Text, caretInWindow);
            if (context == null)
                return false;

            var metadata = EditorContext.GetMetadata();
            if (metadata == null)
            {
                StatusBar.Show("SQL Helper: metadata is still loading - try again in a moment");
                return false;
            }

            var newLine = EditorContext.GetNewLine(caret.GetContainingLine());
            if (context.Kind == TabContextKind.SelectStar)
            {
                var columns = ContextExpander.GetStarColumns(context, metadata);
                if (columns == null)
                    return false;

                var picker = new StarColumnPickerWindow(columns);
                if (picker.ShowModal() != true)
                    return true; // Cancel leaves the star untouched and does not insert a tab

                // The dialog is modal, but verify the document has not changed before applying its original offsets.
                if (!ReferenceEquals(_view.TextBuffer.CurrentSnapshot, caret.Snapshot))
                    return true;

                var selectedEdit = ContextExpander.ExpandSelectedStar(window.Text, context, newLine, picker.SelectedColumns);
                if (selectedEdit == null)
                    return true;

                var selectedStart = window.Start + selectedEdit.Start;
                var selectedSnapshot = _view.TextBuffer.Replace(new Span(selectedStart, selectedEdit.Length), selectedEdit.NewText);
                _view.Caret.MoveTo(new SnapshotPoint(selectedSnapshot, selectedStart + selectedEdit.CaretOffset));
                return true;
            }

            var edit = ContextExpander.TryExpand(window.Text, caretInWindow, newLine, metadata, SettingsStore.Instance.Current.AutoAlias);
            if (edit == null)
                return false;

            var start = window.Start + edit.Start;
            var snapshot = _view.TextBuffer.Replace(new Span(start, edit.Length), edit.NewText);
            _view.Caret.MoveTo(new SnapshotPoint(snapshot, start + edit.CaretOffset));

            // JOIN t alias ON | with several possible conditions: let the user pick
            if (edit.OfferJoinConditions)
                CompletionLauncher.ShowIfApplicable(_asyncCompletionBroker, _view);
            return true;
        }

        private bool TrySnippetExpand()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var caret = _view.Caret.Position.BufferPosition;
            var line = caret.GetContainingLine();
            var beforeCaret = new SnapshotSpan(line.Start, caret).GetText();

            var expansion = SnippetExpander.TryExpand(
                beforeCaret, SnippetExpander.GetIndent(line.GetText()), EditorContext.GetNewLine(line), SnippetStore.Instance.Find,
                SnippetInserter.Variables());
            if (expansion == null)
                return false;

            SnippetInserter.Apply(_view, new Span(line.Start.Position + expansion.ReplaceStart, expansion.ReplaceLength), expansion);

            // ssf / ii / ... leave the caret where a table name goes: offer the table list right away
            if (expansion.Stops.Count == 0)
                CompletionLauncher.ShowIfApplicable(_asyncCompletionBroker, _view);
            return true;
        }

        private bool IsCompletionActive() =>
            _completionBroker.IsCompletionActive(_view) || _asyncCompletionBroker.IsCompletionActive(_view);
    }
}
