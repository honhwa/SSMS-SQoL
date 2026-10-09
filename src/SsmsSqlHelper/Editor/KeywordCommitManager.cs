using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.Language.Intellisense;
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

        [Import]
        internal ISignatureHelpBroker SignatureHelpBroker { get; set; }

        public IAsyncCompletionCommitManager GetOrCreate(ITextView textView) =>
            textView.Properties.GetOrCreateSingletonProperty(() => new KeywordCommitManager(Broker, SignatureHelpBroker));
    }

    /// <summary>
    /// Writes the keyword and then offers what normally follows it: the columns after WHERE / AND / ORDER BY, the tables
    /// after JOIN, the join conditions after ON, so a clause can be written with a couple of key presses.
    /// </summary>
    internal sealed class KeywordCommitManager : IAsyncCompletionCommitManager
    {
        private readonly IAsyncCompletionBroker _broker;
        private readonly ISignatureHelpBroker _signatureHelpBroker;

        public KeywordCommitManager(IAsyncCompletionBroker broker, ISignatureHelpBroker signatureHelpBroker)
        {
            _broker = broker;
            _signatureHelpBroker = signatureHelpBroker;
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
                if (session.TextView is IWpfTextView view)
                {
                    if (item.InsertText.TrimEnd().EndsWith("(", StringComparison.Ordinal))
                    {
#pragma warning disable VSSDK007 // The completion session must finish closing before signature help opens.
                        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                        {
                            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(alwaysYield: true);
                            if (view.IsClosed)
                                return;
                            try { _signatureHelpBroker.TriggerSignatureHelp(view); }
                            catch (Exception ex) { Log.Error("Function signature help failed", ex); }
                        }).FileAndForget("SsmsSqlHelper/FunctionSignatureHelp");
#pragma warning restore VSSDK007
                    }
                    else
                        CompletionLauncher.ShowIfApplicableSoon(_broker, view);
                }

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
