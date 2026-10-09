using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SsmsSqlHelper.Parsing;
using SsmsSqlHelper.Diagnostics;

namespace SsmsSqlHelper.Editor
{
    /// <summary>Opens our completion lists from code, e.g. right after a snippet or a table commit.</summary>
    internal static class CompletionLauncher
    {
        private sealed class DeletionRequest
        {
            public int Generation;
        }

        /// <summary>
        /// Ctrl+Space (and Ctrl+J). SSMS handles these itself, so without this our lists would never open for them. Opens
        /// whatever applies at the caret; false if nothing of ours does, so SSMS gets the key as before.
        /// </summary>
        public static bool TryInvoke(IAsyncCompletionBroker broker, ITextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var caret = view.Caret.Position.BufferPosition;
            var trigger = new CompletionTrigger(CompletionTriggerReason.Invoke, caret.Snapshot);
            var session = broker.TriggerCompletion(view, trigger, caret, CancellationToken.None);
            if (session == null)
                return false;

            session.OpenOrUpdate(trigger, caret, CancellationToken.None);
            return true;
        }

        /// <summary>Opens the list if the caret is where a table name, a join condition or a column belongs.</summary>
        public static void ShowIfApplicable(IAsyncCompletionBroker broker, ITextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var caret = view.Caret.Position.BufferPosition;
            var window = TextWindow.Around(caret.Snapshot, caret.Position, 8000, 4000);
            var position = caret.Position - window.Start;

            if (!SqlContext.TryGetTableNameSpan(window.Text, position, out _, out _) &&
                !SqlContext.TryGetDatabaseNameSpan(window.Text, position, out _, out _) &&
                !SqlContext.TryGetProcedureNameSpan(window.Text, position, out _, out _) &&
                !SqlContext.TryGetJoinOn(window.Text, position, out _, out _, out _, out _) &&
                !SqlContext.TryGetColumnContext(window.Text, position, out _) &&
                !SqlContext.ShouldRefreshCompletionAfterSpace(window.Text, position) &&
                !HasMatchingKeyword(window.Text, position))
                return;

            var trigger = new CompletionTrigger(CompletionTriggerReason.Invoke, caret.Snapshot);
            var session = broker.TriggerCompletion(view, trigger, caret, CancellationToken.None);
            session?.OpenOrUpdate(trigger, caret, CancellationToken.None);
        }

        private static bool HasMatchingKeyword(string text, int position)
        {
            if (!SqlContext.TryGetKeywordContext(text, position, out var context))
                return false;
            if (context.Suggestions.Exists(s => s.Text == "GETDATE()"))
                return true;
            var word = text.Substring(context.Start, context.End - context.Start);
            return word.Length > 0 && context.Suggestions.Exists(s =>
                s.Text.StartsWith(word, System.StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Same as <see cref="ShowIfApplicable"/> but on the next UI turn, so a session that is still closing doesn't swallow it.</summary>
        public static void ShowIfApplicableSoon(IAsyncCompletionBroker broker, ITextView view)
        {
#pragma warning disable VSSDK007 // Fire-and-forget: the commit must not wait for the list
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(alwaysYield: true);
#pragma warning disable VSTHRD010 // The analyzer doesn't follow the alwaysYield overload; we are on the UI thread here
                if (view.IsClosed)
                    return;
                try
                {
                    ShowIfApplicable(broker, view);
                }
                catch (System.Exception ex)
                {
                    Log.Error("Could not open completion", ex);
                }
#pragma warning restore VSTHRD010
            }).FileAndForget("SsmsSqlHelper/ShowCompletion");
#pragma warning restore VSSDK007
        }

        /// <summary>
        /// After Backspace or Delete, reopen suggestions only after a short pause. Repeated deletion otherwise starts
        /// overlapping async completion sessions against snapshots that are being replaced on every keystroke.
        /// </summary>
        public static void ShowAfterDeletionSoon(IAsyncCompletionBroker broker, ITextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var request = view.Properties.GetOrCreateSingletonProperty(() => new DeletionRequest());
            var generation = ++request.Generation;
            var snapshot = view.TextSnapshot;
            var caretPosition = view.Caret.Position.BufferPosition.Position;
#pragma warning disable VSSDK007 // Fire-and-forget: editing must not wait for the list
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await Task.Delay(150).ConfigureAwait(false);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
#pragma warning disable VSTHRD010 // SwitchToMainThreadAsync above ensures editor access is on the UI thread
                try
                {
                    if (view.IsClosed || request.Generation != generation ||
                        !ReferenceEquals(view.TextSnapshot, snapshot) ||
                        view.Caret.Position.BufferPosition.Position != caretPosition ||
                        broker.IsCompletionActive(view))
                        return;

                    var caret = view.Caret.Position.BufferPosition;
                    var trigger = new CompletionTrigger(CompletionTriggerReason.Backspace, caret.Snapshot);
                    var session = broker.TriggerCompletion(view, trigger, caret, CancellationToken.None);
                    session?.OpenOrUpdate(trigger, caret, CancellationToken.None);
                }
                catch (System.Exception ex)
                {
                    Log.Error("Completion after deletion failed", ex);
                }
#pragma warning restore VSTHRD010
            }).FileAndForget("SsmsSqlHelper/ShowCompletionAfterDeletion");
#pragma warning restore VSSDK007
        }
    }
}
