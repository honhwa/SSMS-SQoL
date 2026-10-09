using System;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SsmsSqlHelper.Metadata;
using SsmsSqlHelper.Parsing;

namespace SsmsSqlHelper.Editor
{
    [Export(typeof(IAsyncCompletionSourceProvider))]
    [Name("SsmsSqlHelper.ProcedureCompletion")]
    [ContentType("SQL")]
    internal sealed class ProcedureSourceProvider : IAsyncCompletionSourceProvider
    {
        public IAsyncCompletionSource GetOrCreate(ITextView textView) =>
            textView.Properties.GetOrCreateSingletonProperty(() => new ProcedureCompletionSource());
    }

    internal sealed class ProcedureCompletionSource : IAsyncCompletionSource
    {
        private static readonly ImageElement Icon = new ImageElement(
            new ImageId(KnownMonikers.Method.Guid, KnownMonikers.Method.Id), "Procedure");
        private DbMetadata _metadata;
        private DbMetadata _itemsMetadata;
        private ImmutableArray<CompletionItem> _items;

        public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            if (!ShouldConsider(trigger) || !ThreadHelper.CheckAccess())
                return CompletionStartData.DoesNotParticipateInCompletion;
            ThreadHelper.ThrowIfNotOnUIThread();

            var window = TextWindow.Around(triggerLocation.Snapshot, triggerLocation.Position, 4000, 200);
            if (!SqlContext.TryGetProcedureNameSpan(window.Text, triggerLocation.Position - window.Start, out var start, out var end))
                return CompletionStartData.DoesNotParticipateInCompletion;

            _metadata = EditorContext.GetMetadata();
            if (_metadata == null || _metadata.Procedures.Count == 0)
                return CompletionStartData.DoesNotParticipateInCompletion;

            return new CompletionStartData(CompletionParticipation.ProvidesItems,
                new SnapshotSpan(triggerLocation.Snapshot, window.Start + start, end - start));
        }

        public Task<CompletionContext> GetCompletionContextAsync(IAsyncCompletionSession session, CompletionTrigger trigger,
            SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
        {
            var metadata = _metadata;
            if (metadata == null)
                return Task.FromResult(CompletionContext.Empty);

            if (!ReferenceEquals(metadata, _itemsMetadata))
            {
                _items = metadata.Procedures.Select(p => new CompletionItem(
                    displayText: p.Name,
                    source: this,
                    icon: Icon,
                    filters: ImmutableArray<CompletionFilter>.Empty,
                    suffix: p.Schema + " · " + p.Parameters.Count + " parameters",
                    insertText: p.QualifiedName,
                    sortText: p.Name,
                    filterText: p.Schema + "." + p.Name,
                    attributeIcons: ImmutableArray<ImageElement>.Empty)).ToImmutableArray();
                _itemsMetadata = metadata;
            }
            return Task.FromResult(new CompletionContext(_items, null, InitialSelectionHint.RegularSelection));
        }

        public Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token)
        {
            var procedure = _metadata?.FindProcedure(item.InsertText);
            if (procedure == null)
                return Task.FromResult<object>(null);

            var sb = new StringBuilder(procedure.ToString());
            foreach (var p in procedure.Parameters.Take(25))
                sb.AppendLine().Append("  ").Append(p.Name).Append(' ').Append(p.DisplayType)
                  .Append(p.IsOutput ? " OUTPUT" : p.IsReadOnly ? " READONLY" : "");
            if (procedure.Parameters.Count > 25)
                sb.AppendLine().Append("  ... ").Append(procedure.Parameters.Count - 25).Append(" more");
            return Task.FromResult<object>(sb.ToString());
        }

        private static bool ShouldConsider(CompletionTrigger trigger)
        {
            if (trigger.Reason == CompletionTriggerReason.Invoke ||
                trigger.Reason == CompletionTriggerReason.InvokeAndCommitIfUnique ||
                trigger.Reason == CompletionTriggerReason.Backspace ||
                trigger.Reason == CompletionTriggerReason.Deletion)
                return true;
            if (trigger.Reason != CompletionTriggerReason.Insertion)
                return false;
            var c = trigger.Character;
            return char.IsLetterOrDigit(c) || c == '_' || c == ' ' || c == '.' || c == '[';
        }
    }
}
