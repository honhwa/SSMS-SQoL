using System.Threading;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SsmsSqlHelper.Parsing;

namespace SsmsSqlHelper.Editor
{
    /// <summary>Opens our completion lists from code, e.g. right after a snippet or a table commit.</summary>
    internal static class CompletionLauncher
    {
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
                !SqlContext.TryGetJoinOn(window.Text, position, out _, out _, out _, out _) &&
                !SqlContext.TryGetColumnContext(window.Text, position, out _))
                return;

            var trigger = new CompletionTrigger(CompletionTriggerReason.Invoke, caret.Snapshot);
            var session = broker.TriggerCompletion(view, trigger, caret, CancellationToken.None);
            session?.OpenOrUpdate(trigger, caret, CancellationToken.None);
        }

        /// <summary>Same as <see cref="ShowIfApplicable"/> but on the next UI turn, so a session that is still closing doesn't swallow it.</summary>
        public static void ShowIfApplicableSoon(IAsyncCompletionBroker broker, ITextView view)
        {
#pragma warning disable VSSDK007 // Fire-and-forget: the commit must not wait for the list
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(alwaysYield: true);
#pragma warning disable VSTHRD010 // The analyzer doesn't follow the alwaysYield overload; we are on the UI thread here
                ShowIfApplicable(broker, view);
#pragma warning restore VSTHRD010
            }).FileAndForget("SsmsSqlHelper/ShowCompletion");
#pragma warning restore VSSDK007
        }

        /// <summary>
        /// After Backspace or Delete: the editor does not open a list when characters are removed, so a name that is being
        /// corrected would get no suggestions. Asks every source (they decide by position whether a list belongs there), on the
        /// next UI turn when the deletion is done and any session that the deletion closed is gone.
        /// </summary>
        public static void ShowAfterDeletionSoon(IAsyncCompletionBroker broker, ITextView view)
        {
#pragma warning disable VSSDK007 // Fire-and-forget: editing must not wait for the list
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(alwaysYield: true);
#pragma warning disable VSTHRD010 // The analyzer doesn't follow the alwaysYield overload; we are on the UI thread here
                if (view.IsClosed || broker.IsCompletionActive(view))     // an open list refilters by itself
                    return;

                var caret = view.Caret.Position.BufferPosition;
                var trigger = new CompletionTrigger(CompletionTriggerReason.Backspace, caret.Snapshot);
                var session = broker.TriggerCompletion(view, trigger, caret, CancellationToken.None);
                session?.OpenOrUpdate(trigger, caret, CancellationToken.None);
#pragma warning restore VSTHRD010
            }).FileAndForget("SsmsSqlHelper/ShowCompletionAfterDeletion");
#pragma warning restore VSSDK007
        }
    }
}
