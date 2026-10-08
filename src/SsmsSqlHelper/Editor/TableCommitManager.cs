using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SsmsSqlHelper.Diagnostics;
using SsmsSqlHelper.Generation;
using SsmsSqlHelper.Metadata;
using SsmsSqlHelper.Settings;

namespace SsmsSqlHelper.Editor
{
    [Export(typeof(IAsyncCompletionCommitManagerProvider))]
    [Name("SsmsSqlHelper.TableCommit")]
    [ContentType("SQL")]
    internal sealed class TableCommitManagerProvider : IAsyncCompletionCommitManagerProvider
    {
        [Import]
        internal IAsyncCompletionBroker Broker { get; set; }

        public IAsyncCompletionCommitManager GetOrCreate(ITextView textView) =>
            textView.Properties.GetOrCreateSingletonProperty(() => new TableCommitManager(Broker));
    }

    /// <summary>
    /// Committing a table also finishes the statement in the same edit (one undo step): the column list after
    /// <c>INSERT INTO</c> / <c>UPDATE</c>, or the alias (and the ON condition after JOIN) after <c>FROM</c> / <c>JOIN</c>.
    /// So <c>ii</c> → Tab → pick table → Tab gives a full INSERT.
    /// </summary>
    internal sealed class TableCommitManager : IAsyncCompletionCommitManager
    {
        private readonly IAsyncCompletionBroker _broker;

        public TableCommitManager(IAsyncCompletionBroker broker)
        {
            _broker = broker;
        }

        // Only Tab / Enter / double-click commit; typing a space keeps typing a name
        public IEnumerable<char> PotentialCommitCharacters => Enumerable.Empty<char>();

        public bool ShouldCommitCompletion(IAsyncCompletionSession session, SnapshotPoint location, char typedChar, CancellationToken token) => false;

        public CommitResult TryCommit(IAsyncCompletionSession session, ITextBuffer buffer, CompletionItem item, char typedChar, CancellationToken token)
        {
            if (!item.Properties.TryGetProperty(TableCompletionSource.TableKey, out TableInfo table) || !ThreadHelper.CheckAccess())
                return CommitResult.Unhandled;
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var snapshot = buffer.CurrentSnapshot;
                var span = session.ApplicableToSpan.GetSpan(snapshot);
                var insert = item.InsertText;

                // Run the expander on the text as it will look once the table name is in
                var window = TextWindow.Around(snapshot, span.Start.Position, 8000, 20000);
                var spanInWindow = span.Start.Position - window.Start;
                var futureText = window.Text.Remove(spanInWindow, Math.Min(span.Length, window.Text.Length - spanInWindow)).Insert(spanInWindow, insert);
                var caretInFuture = spanInWindow + insert.Length;
                var newLine = EditorContext.GetNewLine(span.Start.GetContainingLine());

                var edit = ContextExpander.TryExpand(futureText, caretInFuture, newLine, EditorContextMetadata(table), SettingsStore.Instance.Current.AutoAlias);
                var text = insert;
                var caretOffset = insert.Length;
                var offerJoinConditions = false;
                if (edit != null && edit.Start == caretInFuture && edit.Length == 0)
                {
                    text += edit.NewText;
                    caretOffset += edit.CaretOffset;
                    offerJoinConditions = edit.OfferJoinConditions;
                }

                var after = buffer.Replace(span.Span, text);
                session.TextView.Caret.MoveTo(new SnapshotPoint(after, span.Start.Position + caretOffset));

                // The table list is still closing; open the condition list on the next UI turn
                if (offerJoinConditions)
                    CompletionLauncher.ShowIfApplicableSoon(_broker, session.TextView);
                return new CommitResult(isHandled: true, CommitBehavior.None);
            }
            catch (Exception ex)
            {
                Log.Error("Table commit failed", ex);
                return CommitResult.Unhandled;
            }
        }

        // Metadata of the active window; falls back to a single-table snapshot so expansion still works if the cache was refreshed meanwhile
        private static DbMetadata EditorContextMetadata(TableInfo table)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return EditorContext.GetMetadata()
                   ?? new DbMetadata(null, null, new[] { table }, DateTime.Now, TimeSpan.Zero);
        }
    }
}
