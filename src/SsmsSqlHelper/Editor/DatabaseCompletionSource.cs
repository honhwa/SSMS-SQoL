using System;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SsmsSqlHelper.Metadata;
using SsmsSqlHelper.Parsing;
using SsmsSqlHelper.Ssms;

namespace SsmsSqlHelper.Editor
{
    [Export(typeof(IAsyncCompletionSourceProvider))]
    [Name("SsmsSqlHelper.DatabaseCompletion")]
    [ContentType("SQL")]
    internal sealed class DatabaseCompletionSourceProvider : IAsyncCompletionSourceProvider
    {
        public IAsyncCompletionSource GetOrCreate(ITextView textView) =>
            textView.Properties.GetOrCreateSingletonProperty(() => new DatabaseCompletionSource());
    }

    /// <summary>Offers accessible databases after USE, including on explicit Ctrl+Space.</summary>
    internal sealed class DatabaseCompletionSource : IAsyncCompletionSource
    {
        private static readonly ImageElement Icon = new ImageElement(
            new ImageId(KnownMonikers.Database.Guid, KnownMonikers.Database.Id), "Database");

        private ActiveConnection _connection;
        private bool _bracketed;

        public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            if (!ShouldConsider(trigger) || !ThreadHelper.CheckAccess())
                return CompletionStartData.DoesNotParticipateInCompletion;
            ThreadHelper.ThrowIfNotOnUIThread();

            var window = TextWindow.Around(triggerLocation.Snapshot, triggerLocation.Position, 4000, 200);
            if (!SqlContext.TryGetDatabaseNameSpan(window.Text, triggerLocation.Position - window.Start, out var start, out var end))
                return CompletionStartData.DoesNotParticipateInCompletion;

            _connection = SsmsConnectionAdapter.GetActiveConnection();
            if (_connection == null)
                return CompletionStartData.DoesNotParticipateInCompletion;

            _bracketed = end > start && window.Text[start] == '[';
            return new CompletionStartData(CompletionParticipation.ProvidesItems,
                new SnapshotSpan(triggerLocation.Snapshot, window.Start + start, end - start));
        }

        public async Task<CompletionContext> GetCompletionContextAsync(IAsyncCompletionSession session, CompletionTrigger trigger,
            SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
        {
            var connection = _connection;
            if (connection == null)
                return CompletionContext.Empty;

            var bracketed = _bracketed;
            var names = await DatabaseNameService.Instance.GetOrLoadAsync(connection).ConfigureAwait(false);
            var items = names.Select(name => new CompletionItem(
                displayText: name,
                source: this,
                icon: Icon,
                filters: ImmutableArray<CompletionFilter>.Empty,
                suffix: connection.Server,
                insertText: bracketed ? "[" + name.Replace("]", "]]") + "]" : SqlIdentifier.Quote(name),
                sortText: name,
                filterText: bracketed ? "[" + name : name,
                attributeIcons: ImmutableArray<ImageElement>.Empty)).ToImmutableArray();
            return new CompletionContext(items, null, InitialSelectionHint.RegularSelection);
        }

        public Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token) =>
            Task.FromResult<object>("Database on " + _connection?.Server);

        private static bool ShouldConsider(CompletionTrigger trigger)
        {
            if (trigger.Reason == CompletionTriggerReason.Invoke ||
                trigger.Reason == CompletionTriggerReason.InvokeAndCommitIfUnique ||
                trigger.Reason == CompletionTriggerReason.Backspace ||
                trigger.Reason == CompletionTriggerReason.Deletion)
                return true;
            return trigger.Reason == CompletionTriggerReason.Insertion &&
                   (char.IsLetterOrDigit(trigger.Character) || trigger.Character == '_' || trigger.Character == '[');
        }
    }
}
