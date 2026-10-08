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
using SsmsSqlHelper.Metadata;
using SsmsSqlHelper.Parsing;
using SsmsSqlHelper.Settings;

namespace SsmsSqlHelper.Editor
{
    [Export(typeof(IAsyncCompletionSourceProvider))]
    [Name("SsmsSqlHelper.ColumnCompletion")]
    [ContentType("SQL")]
    internal sealed class ColumnSourceProvider : IAsyncCompletionSourceProvider
    {
        public IAsyncCompletionSource GetOrCreate(ITextView textView) =>
            textView.Properties.GetOrCreateSingletonProperty(() => new ColumnCompletionSource());
    }

    /// <summary>
    /// Column names where they belong: after <c>alias.</c>, wherever an expression starts (SELECT list, WHERE, ON, ORDER BY, ...)
    /// and inside the column list of <c>INSERT INTO table (</c>. With several tables in the query, columns come with their alias.
    /// </summary>
    internal sealed class ColumnCompletionSource : IAsyncCompletionSource
    {
        private static readonly object SuggestionKey = typeof(ColumnSuggestion);
        private static readonly ImageElement ColumnIcon = Icon(KnownMonikers.Field, "Column");
        private static readonly ImageElement KeyIcon = Icon(KnownMonikers.Key, "Primary key");

        private DbMetadata _metadata;
        private List<ColumnSuggestion> _suggestions;
        private bool _qualify;

        public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            var explicitInvoke = trigger.Reason == CompletionTriggerReason.Invoke || trigger.Reason == CompletionTriggerReason.InvokeAndCommitIfUnique;
            if (!ShouldConsider(trigger, explicitInvoke) || !ThreadHelper.CheckAccess())
                return CompletionStartData.DoesNotParticipateInCompletion;
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!explicitInvoke && !SettingsStore.Instance.Current.ShowColumnHints)
                return CompletionStartData.DoesNotParticipateInCompletion;

            var window = TextWindow.Around(triggerLocation.Snapshot, triggerLocation.Position, 8000, 4000);
            var position = triggerLocation.Position - window.Start;
            if (!SqlContext.TryGetColumnContext(window.Text, position, out var context))
                return CompletionStartData.DoesNotParticipateInCompletion;

            // Typing the first letter of a bare word is enough; an empty word needs Ctrl+Space (or a dot, handled as Qualified)
            if (context.Kind != ColumnContextKind.Qualified && context.Start == context.End && !explicitInvoke)
                return CompletionStartData.DoesNotParticipateInCompletion;

            // The join-condition list owns the position right after ON
            if (SqlContext.TryGetJoinOn(window.Text, position, out _, out _, out _, out _))
                return CompletionStartData.DoesNotParticipateInCompletion;

            var metadata = EditorContext.GetMetadata();
            if (metadata == null)
                return CompletionStartData.DoesNotParticipateInCompletion;

            var suggestions = ColumnSuggester.Suggest(context, metadata);
            if (suggestions.Count == 0)
                return CompletionStartData.DoesNotParticipateInCompletion;

            _metadata = metadata;
            _suggestions = suggestions;
            _qualify = suggestions.Any(s => s.IsQualified);

            var span = new SnapshotSpan(triggerLocation.Snapshot, window.Start + context.Start, context.End - context.Start);
            return new CompletionStartData(CompletionParticipation.ProvidesItems, span);
        }

        public Task<CompletionContext> GetCompletionContextAsync(IAsyncCompletionSession session, CompletionTrigger trigger,
            SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
        {
            var suggestions = _suggestions;
            if (suggestions == null)
                return Task.FromResult(CompletionContext.Empty);

            var qualify = _qualify;
            var items = suggestions.Select(s =>
            {
                var c = s.Column;
                var item = new CompletionItem(
                    displayText: c.Name,
                    source: this,
                    icon: c.IsPrimaryKey ? KeyIcon : ColumnIcon,
                    filters: ImmutableArray<CompletionFilter>.Empty,
                    suffix: c.DisplayType + (qualify ? "  " + s.Qualifier : ""),
                    insertText: s.InsertText,
                    sortText: s.TableOrder.ToString("D2") + c.Ordinal.ToString("D5"),
                    filterText: c.Name,
                    attributeIcons: ImmutableArray<ImageElement>.Empty);
                item.Properties.AddProperty(SuggestionKey, s);
                return item;
            }).ToImmutableArray();

            return Task.FromResult(new CompletionContext(items, null, InitialSelectionHint.SoftSelection));
        }

        public Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token)
        {
            var metadata = _metadata;
            if (metadata == null || !item.Properties.TryGetProperty(SuggestionKey, out ColumnSuggestion s))
                return Task.FromResult<object>(null);

            return Task.FromResult<object>(ColumnSuggester.Describe(s, metadata));
        }

        private static bool ShouldConsider(CompletionTrigger trigger, bool explicitInvoke)
        {
            if (explicitInvoke || trigger.Reason == CompletionTriggerReason.Backspace || trigger.Reason == CompletionTriggerReason.Deletion)
                return true;
            if (trigger.Reason != CompletionTriggerReason.Insertion)
                return false;

            var c = trigger.Character;
            return char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '[';
        }

        private static ImageElement Icon(Microsoft.VisualStudio.Imaging.Interop.ImageMoniker moniker, string name) =>
            new ImageElement(new ImageId(moniker.Guid, moniker.Id), name);
    }
}
