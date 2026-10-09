using System.Runtime.InteropServices;
using System;
using System.Threading;
using System.Linq;
using System.Windows.Input;
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
using SsmsSqlHelper.Metadata;
using SsmsSqlHelper.Parsing;
using SsmsSqlHelper.Settings;
using SsmsSqlHelper.Snippets;
using SsmsSqlHelper.Ssms;
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
        private readonly ISignatureHelpBroker _signatureHelpBroker;

        public SnippetCommandFilter(IWpfTextView view, ICompletionBroker completionBroker, IAsyncCompletionBroker asyncCompletionBroker,
            ISignatureHelpBroker signatureHelpBroker)
        {
            _view = view;
            _completionBroker = completionBroker;
            _asyncCompletionBroker = asyncCompletionBroker;
            _signatureHelpBroker = signatureHelpBroker;
        }

        public IOleCommandTarget Next { get; set; }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (pguidCmdGroup == VSConstants.GUID_VSStandardCommandSet97 &&
                nCmdID == (uint)VSConstants.VSStd97CmdID.GotoDefn)
            {
                var handled = Keyboard.Modifiers == ModifierKeys.Control
                    ? TryGoToProcedure() || TrySchemaObject(true)
                    : TryOpenAlterProcedure() || TrySchemaObject(false);
                if (handled)
                    return VSConstants.S_OK;
            }

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
                        var character = pvaIn == IntPtr.Zero ? '\0' : (char)(ushort)Marshal.GetObjectForNativeVariant(pvaIn);
                        if (character == '(' || character == ',' || character == ')')
                        {
                            var typed = Next.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
                            if (ErrorHandler.Succeeded(typed))
                                ShowFunctionSignature();
                            return typed;
                        }
                        // "bl" opened a list for the bare word; a dot turns it into "bl." whose list is a different one (the
                        // columns of that table only), so the old session is closed and the right one opened
                        if (character == '.')
                        {
                            _asyncCompletionBroker.GetSession(_view)?.Dismiss();
                            var typed = Next.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
                            if (ErrorHandler.Succeeded(typed))
                                CompletionLauncher.ShowIfApplicableSoon(_asyncCompletionBroker, _view);
                            return typed;
                        }
                        if (character == ' ')
                        {
                            var typed = Next.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
                            if (ErrorHandler.Succeeded(typed))
                            {
                                var caret = _view.Caret.Position.BufferPosition;
                                var window = TextWindow.Around(caret.Snapshot, caret.Position, WindowBefore, WindowAfter);
                                var position = caret.Position - window.Start;
                                if (SqlFunctionCallParser.TryFindActive(window.Text, position, out var function) &&
                                    (function.Signature.Name == "CAST" || function.Signature.Name == "TRY_CAST") &&
                                    function.ArgumentIndex == 1)
                                    ShowFunctionSignature();
                                var startsSelectExpression = SqlContext.TryGetKeywordContext(window.Text, position, out var keywordContext) &&
                                    keywordContext.Suggestions.Exists(s => s.Text == "GETDATE()");
                                var changedKeywordContext = SqlContext.ShouldRefreshCompletionAfterSpace(window.Text, position);
                                if (SqlContext.TryGetTableNameSpan(window.Text, position, out _, out _) ||
                                    startsSelectExpression || changedKeywordContext)
                                {
                                    // The old session cannot add items for the context after this space.
                                    if (startsSelectExpression || changedKeywordContext)
                                        _completionBroker.DismissAllSessions(_view);
                                    _asyncCompletionBroker.GetSession(_view)?.Dismiss();
                                    CompletionLauncher.ShowIfApplicableSoon(_asyncCompletionBroker, _view);
                                }
                            }
                            return typed;
                        }
                        break;

                    case VSConstants.VSStd2KCmdID.BACKSPACE:
                    case VSConstants.VSStd2KCmdID.DELETE:
                    {
                        // Close the old list before its span is invalidated by deletion. Reopen once typing pauses.
                        _asyncCompletionBroker.GetSession(_view)?.Dismiss();
                        var result = Next.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
                        if (ErrorHandler.Succeeded(result))
                            CompletionLauncher.ShowAfterDeletionSoon(_asyncCompletionBroker, _view);
                        return result;
                    }
                }
            }

            return Next.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
        }

        private void ShowFunctionSignature()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var caret = _view.Caret.Position.BufferPosition;
                var window = TextWindow.Around(caret.Snapshot, caret.Position, WindowBefore, WindowAfter);
                var position = caret.Position - window.Start;
                if (SqlFunctionCallParser.TryFindActive(window.Text, position, out _))
                {
                    _signatureHelpBroker.DismissAllSessions(_view);
                    _signatureHelpBroker.TriggerSignatureHelp(_view);
                }
                else
                    foreach (var session in _signatureHelpBroker.GetSessions(_view))
                        if (session.Signatures.OfType<SqlFunctionSignatureItem>().Any())
                            session.Dismiss();
            }
            catch (Exception ex)
            {
                Log.Error("Function signature help failed", ex);
            }
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (pguidCmdGroup != VSConstants.GUID_VSStandardCommandSet97 ||
                (ProcedureAtCaret() == null && SchemaObjectAtCaret() == null))
                return Next.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);

            var nextResult = Next.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
            var found = false;
            for (var i = 0; i < cCmds; i++)
            {
                if (prgCmds[i].cmdID != (uint)VSConstants.VSStd97CmdID.GotoDefn)
                    continue;
                prgCmds[i].cmdf = (uint)(OLECMDF.OLECMDF_SUPPORTED | OLECMDF.OLECMDF_ENABLED);
                found = true;
            }
            return found ? VSConstants.S_OK : nextResult;
        }

        private ObjectName ProcedureAtCaret()
        {
            var caret = _view.Caret.Position.BufferPosition;
            var window = TextWindow.Around(caret.Snapshot, caret.Position, 2000, 500);
            return SqlContext.GetProcedureAtCaret(window.Text, caret.Position - window.Start);
        }

        private ObjectName SchemaObjectAtCaret()
        {
            var caret = _view.Caret.Position.BufferPosition;
            var window = TextWindow.Around(caret.Snapshot, caret.Position, 2000, 500);
            return SqlContext.GetSchemaObjectAtCaret(window.Text, caret.Position - window.Start);
        }

        internal bool TryGoToProcedure()
        {
            var name = ProcedureAtCaret();
            if (name == null)
                return false;

            Log.Info("Ctrl+F12 on procedure name " + name.Text);

            var connection = SsmsConnectionAdapter.GetActiveConnection();
            if (connection == null || string.IsNullOrEmpty(connection.Database))
            {
                StatusBar.Show("SQL Helper: connect the query window to a database before using Ctrl+F12");
                return true;
            }
            var metadata = MetadataService.Instance.TryGet(connection);
            if (metadata == null)
            {
                return false;
            }

            var procedure = metadata.FindProcedure(name.Text);
            if (procedure == null)
                return false;

            if (!ObjectExplorerNavigator.TrySelectProcedure(connection, procedure))
                StatusBar.Show("SQL Helper: could not find " + procedure + " in Object Explorer; check its connection and filter");
            return true;
        }

        internal bool TryOpenAlterProcedure()
        {
            var name = ProcedureAtCaret();
            if (name == null)
                return false;

            var connection = SsmsConnectionAdapter.GetActiveConnection();
            if (connection == null || string.IsNullOrEmpty(connection.Database))
            {
                StatusBar.Show("SQL Helper: connect the query window to a database before using F12");
                return true;
            }
            var metadata = MetadataService.Instance.TryGet(connection);
            if (metadata == null)
            {
                return false;
            }
            var procedure = metadata.FindProcedure(name.Text);
            if (procedure == null)
                return false;

            SsmsConnectionAdapter.GetQueryWindowConnection(out var ui, out var live);
            if (ui == null)
            {
                StatusBar.Show("SQL Helper: could not read the query window connection for the new tab");
                return true;
            }
            Log.Info("F12 opening ALTER script for " + procedure);
            ProcedureAlterEditor.Open(connection, procedure, ui, live);
            return true;
        }

        internal bool TrySchemaObject(bool navigate)
        {
            var name = SchemaObjectAtCaret();
            if (name == null)
                return false;
            var connection = SsmsConnectionAdapter.GetActiveConnection();
            if (connection == null || string.IsNullOrEmpty(connection.Database))
            {
                StatusBar.Show("SQL Helper: connect the query window to a database first");
                return true;
            }
            SsmsConnectionAdapter.GetQueryWindowConnection(out var ui, out var live);
            OpenSchemaObjectAsync(connection, name.Text, navigate, ui, live);
            return true;
        }

        private async void OpenSchemaObjectAsync(ActiveConnection connection, string name, bool navigate,
            Microsoft.SqlServer.Management.Smo.RegSvrEnum.UIConnectionInfo ui, System.Data.IDbConnection live)
        {
            try
            {
                var schemaObject = await SchemaObjectResolver.FindAsync(connection, name).ConfigureAwait(false);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (schemaObject == null)
                {
                    StatusBar.Show("SQL Helper: object " + name + " was not found in " + connection.Database);
                    return;
                }
                if (navigate)
                {
                    if (!ObjectExplorerNavigator.TrySelectObject(connection, schemaObject))
                        StatusBar.Show("SQL Helper: could not select " + schemaObject + " in Object Explorer");
                }
                else if (schemaObject.IsTable)
                {
                    if (!ObjectExplorerNavigator.TryOpenTableDesigner(connection, schemaObject))
                        StatusBar.Show("SQL Helper: could not open Table Designer for " + schemaObject);
                }
                else if (ui == null)
                    StatusBar.Show("SQL Helper: could not read the query window connection");
                else if (schemaObject.IsProcedure)
                    ProcedureAlterEditor.Open(connection, new ProcedureInfo(schemaObject.Schema, schemaObject.Name), ui, live);
                else
                    SchemaObjectEditor.Open(connection, schemaObject, ui, live);
            }
            catch (Exception ex)
            {
                Log.Error("F12 lookup failed for " + name, ex);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                StatusBar.Show("SQL Helper: could not resolve " + name + "; see SQL Helper Output");
            }
        }

        private bool HandleTab()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Inside an expanded snippet Tab goes to the next stop (whose placeholder is selected, so this must come before the selection check)
            if (!IsCompletionActive() && SnippetSession.Get(_view)?.MoveNext() == true)
                return true;

            // An exact shortcut wins over an open column/keyword list. In an EXISTS
            // subquery the column list can already be open before "ssf" is complete.
            if (_view.Selection.IsEmpty && HasExactSnippetShortcut())
            {
                _completionBroker.DismissAllSessions(_view);
                _asyncCompletionBroker.GetSession(_view)?.Dismiss();
                try { return TrySnippetExpand(); }
                catch (Exception ex)
                {
                    Log.Error("Tab snippet expansion failed", ex);
                    return false;
                }
            }

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

        private bool HasExactSnippetShortcut()
        {
            var caret = _view.Caret.Position.BufferPosition;
            var line = caret.GetContainingLine();
            var beforeCaret = new SnapshotSpan(line.Start, caret).GetText();
            return SnippetExpander.HasExactShortcut(beforeCaret, SnippetStore.Instance.Find);
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
                CompletionLauncher.ShowIfApplicableSoon(_asyncCompletionBroker, _view);
            return true;
        }

        private bool IsCompletionActive() =>
            _completionBroker.IsCompletionActive(_view) || _asyncCompletionBroker.IsCompletionActive(_view);
    }
}
