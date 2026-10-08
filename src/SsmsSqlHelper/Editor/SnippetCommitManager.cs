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
using SsmsSqlHelper.Snippets;

namespace SsmsSqlHelper.Editor
{
    [Export(typeof(IAsyncCompletionCommitManagerProvider))]
    [Name("SsmsSqlHelper.SnippetCommit")]
    [ContentType("SQL")]
    internal sealed class SnippetCommitManagerProvider : IAsyncCompletionCommitManagerProvider
    {
        [Import]
        internal IAsyncCompletionBroker Broker { get; set; }

        public IAsyncCompletionCommitManager GetOrCreate(ITextView textView) =>
            textView.Properties.GetOrCreateSingletonProperty(() => new SnippetCommitManager(Broker));
    }

    /// <summary>
    /// Accepting a snippet from the list does exactly what Tab does on a typed shortcut: the typed text is replaced by the
    /// expanded body, indented to the line, with the caret at the cursor marker. A table list follows if a table name is next.
    /// </summary>
    internal sealed class SnippetCommitManager : IAsyncCompletionCommitManager
    {
        private readonly IAsyncCompletionBroker _broker;

        public SnippetCommitManager(IAsyncCompletionBroker broker)
        {
            _broker = broker;
        }

        // Only Tab (or Enter/double-click on an explicitly chosen item) commits; typing keeps typing
        public IEnumerable<char> PotentialCommitCharacters => Enumerable.Empty<char>();

        public bool ShouldCommitCompletion(IAsyncCompletionSession session, SnapshotPoint location, char typedChar, CancellationToken token) => false;

        public CommitResult TryCommit(IAsyncCompletionSession session, ITextBuffer buffer, CompletionItem item, char typedChar, CancellationToken token)
        {
            if (!item.Properties.TryGetProperty(SnippetCompletionSource.SnippetKey, out Snippet snippet) || !ThreadHelper.CheckAccess())
                return CommitResult.Unhandled;
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var span = session.ApplicableToSpan.GetSpan(buffer.CurrentSnapshot);
                if (!(session.TextView is IWpfTextView view))
                    return CommitResult.Unhandled;

                SnippetInserter.Insert(view, span.Span, snippet);

                // ssf / ii / ... leave the caret where a table name goes (a snippet with tab stops leads the caret itself)
                if (SnippetSession.Get(view) == null)
                    CompletionLauncher.ShowIfApplicableSoon(_broker, view);
                return new CommitResult(isHandled: true, CommitBehavior.None);
            }
            catch (Exception ex)
            {
                Log.Error("Snippet commit failed", ex);
                return CommitResult.Unhandled;
            }
        }
    }
}
