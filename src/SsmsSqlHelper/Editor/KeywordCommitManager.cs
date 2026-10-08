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

namespace SsmsSqlHelper.Editor
{
    [Export(typeof(IAsyncCompletionCommitManagerProvider))]
    [Name("SsmsSqlHelper.KeywordCommit")]
    [ContentType("SQL")]
    internal sealed class KeywordCommitManagerProvider : IAsyncCompletionCommitManagerProvider
    {
        [Import]
        internal IAsyncCompletionBroker Broker { get; set; }

        public IAsyncCompletionCommitManager GetOrCreate(ITextView textView) =>
            textView.Properties.GetOrCreateSingletonProperty(() => new KeywordCommitManager(Broker));
    }

    /// <summary>
    /// Writes the keyword and then offers what normally follows it: the columns after WHERE / AND / ORDER BY, the tables
    /// after JOIN, the join conditions after ON, so a clause can be written with a couple of key presses.
    /// </summary>
    internal sealed class KeywordCommitManager : IAsyncCompletionCommitManager
    {
        private readonly IAsyncCompletionBroker _broker;

        public KeywordCommitManager(IAsyncCompletionBroker broker)
        {
            _broker = broker;
        }

        public IEnumerable<char> PotentialCommitCharacters => Enumerable.Empty<char>();

        public bool ShouldCommitCompletion(IAsyncCompletionSession session, SnapshotPoint location, char typedChar, CancellationToken token) => false;

        public CommitResult TryCommit(IAsyncCompletionSession session, ITextBuffer buffer, CompletionItem item, char typedChar, CancellationToken token)
        {
            if (!item.Properties.ContainsProperty(KeywordCompletionSource.KeywordKey) || !ThreadHelper.CheckAccess())
                return CommitResult.Unhandled;
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var span = session.ApplicableToSpan.GetSpan(buffer.CurrentSnapshot);
                var after = buffer.Replace(span.Span, item.InsertText);
                session.TextView.Caret.MoveTo(new SnapshotPoint(after, span.Start.Position + item.InsertText.Length));

                // A bracket opened (IN (, VALUES (): what goes inside is not a column of the query, so no list there
                if (session.TextView is IWpfTextView view && !item.InsertText.TrimEnd().EndsWith("(", StringComparison.Ordinal))
                    CompletionLauncher.ShowIfApplicableSoon(_broker, view);

                return new CommitResult(isHandled: true, CommitBehavior.None);
            }
            catch (Exception ex)
            {
                Log.Error("Keyword commit failed", ex);
                return CommitResult.Unhandled;
            }
        }
    }
}
