using System.Collections.Generic;
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
using SsmsSqlHelper.Generation;
using SsmsSqlHelper.Parsing;

namespace SsmsSqlHelper.Editor
{
    [Export(typeof(IAsyncCompletionSourceProvider))]
    [Name("SsmsSqlHelper.JoinConditionCompletion")]
    [ContentType("SQL")]
    internal sealed class JoinConditionSourceProvider : IAsyncCompletionSourceProvider
    {
        public IAsyncCompletionSource GetOrCreate(ITextView textView) =>
            textView.Properties.GetOrCreateSingletonProperty(() => new JoinConditionCompletionSource());
    }

    /// <summary>
    /// After <c>JOIN table alias ON </c>, offers the conditions that link the table to the ones before it:
    /// declared foreign keys first, then guesses from column names.
    /// </summary>
    internal sealed class JoinConditionCompletionSource : IAsyncCompletionSource
    {
        private static readonly object CandidateKey = typeof(JoinCandidate);
        private static readonly ImageElement ForeignKeyIcon = Icon(KnownMonikers.Key, "Foreign key");
        private static readonly ImageElement GuessIcon = Icon(KnownMonikers.QuestionMark, "Guessed from column names");

        private List<JoinCandidate> _candidates;

        public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            if (!ShouldConsider(trigger) || !ThreadHelper.CheckAccess())
                return CompletionStartData.DoesNotParticipateInCompletion;
            ThreadHelper.ThrowIfNotOnUIThread();

            var window = TextWindow.Around(triggerLocation.Snapshot, triggerLocation.Position, 4000, 200);
            if (!SqlContext.TryGetJoinOn(window.Text, triggerLocation.Position - window.Start, out var start, out var end, out var target, out var earlier))
                return CompletionStartData.DoesNotParticipateInCompletion;

            var metadata = EditorContext.GetMetadata();
            var targetTable = metadata?.FindTable(target.Name.Text);
            if (targetTable == null)
                return CompletionStartData.DoesNotParticipateInCompletion;

            _candidates = JoinSuggester.Suggest(metadata, targetTable, ContextExpander.QualifierOf(target, targetTable),
                ContextExpander.ResolveScope(earlier, metadata));
            if (_candidates.Count == 0)
                return CompletionStartData.DoesNotParticipateInCompletion;

            var span = new SnapshotSpan(triggerLocation.Snapshot, window.Start + start, end - start);
            return new CompletionStartData(CompletionParticipation.ProvidesItems, span);
        }

        public Task<CompletionContext> GetCompletionContextAsync(IAsyncCompletionSession session, CompletionTrigger trigger,
            SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
        {
            var candidates = _candidates;
            if (candidates == null)
                return Task.FromResult(CompletionContext.Empty);

            var items = candidates.Select((c, i) =>
            {
                var item = new CompletionItem(
                    displayText: c.Condition,
                    source: this,
                    icon: c.IsForeignKey ? ForeignKeyIcon : GuessIcon,
                    filters: ImmutableArray<CompletionFilter>.Empty,
                    suffix: c.Source,
                    insertText: c.Condition,
                    sortText: (c.IsForeignKey ? "0" : "1") + i.ToString("D3"),
                    filterText: c.Condition,
                    attributeIcons: ImmutableArray<ImageElement>.Empty);
                item.Properties.AddProperty(CandidateKey, c);
                return item;
            }).ToImmutableArray();

            return Task.FromResult(new CompletionContext(items, null, InitialSelectionHint.RegularSelection));
        }

        public Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token) =>
            Task.FromResult<object>(item.Properties.TryGetProperty(CandidateKey, out JoinCandidate c) ? c.Detail : null);

        private static bool ShouldConsider(CompletionTrigger trigger)
        {
            switch (trigger.Reason)
            {
                case CompletionTriggerReason.Invoke:
                case CompletionTriggerReason.InvokeAndCommitIfUnique:
                case CompletionTriggerReason.Backspace:
                case CompletionTriggerReason.Deletion:
                    return true;
                case CompletionTriggerReason.Insertion:
                    var c = trigger.Character;
                    return char.IsLetterOrDigit(c) || c == '_' || c == ' ' || c == '.' || c == '[';
                default:
                    return false;
            }
        }

        private static ImageElement Icon(Microsoft.VisualStudio.Imaging.Interop.ImageMoniker moniker, string name) =>
            new ImageElement(new ImageId(moniker.Guid, moniker.Id), name);
    }
}
