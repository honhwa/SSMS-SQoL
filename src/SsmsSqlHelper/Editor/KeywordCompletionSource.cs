using System;
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
using SsmsSqlHelper.Parsing;
using SsmsSqlHelper.Settings;

namespace SsmsSqlHelper.Editor
{
    [Export(typeof(IAsyncCompletionSourceProvider))]
    [Name("SsmsSqlHelper.KeywordCompletion")]
    [ContentType("SQL")]
    internal sealed class KeywordSourceProvider : IAsyncCompletionSourceProvider
    {
        public IAsyncCompletionSource GetOrCreate(ITextView textView) =>
            textView.Properties.GetOrCreateSingletonProperty(() => new KeywordCompletionSource());
    }

    /// <summary>
    /// What can come next in the statement: after a table WHERE / JOIN / GROUP BY ..., after a column the comparison operators,
    /// after a condition AND / OR, at the start of a statement SELECT / INSERT / ... Always on Ctrl+Space; while typing, once two
    /// letters of one of the words are there. The words follow the case already used in the script.
    /// </summary>
    internal sealed class KeywordCompletionSource : IAsyncCompletionSource
    {
        internal static readonly object KeywordKey = typeof(KeywordSuggestion);

        private const int MinTypedCharacters = 2;
        private static readonly ImageElement Icon = new ImageElement(
            new ImageId(KnownMonikers.IntellisenseKeyword.Guid, KnownMonikers.IntellisenseKeyword.Id), "Keyword");

        private List<KeywordSuggestion> _suggestions;
        private bool _lowercase;
        private bool _leadingSpace;

        public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            var explicitInvoke = trigger.Reason == CompletionTriggerReason.Invoke || trigger.Reason == CompletionTriggerReason.InvokeAndCommitIfUnique;
            if (!ShouldConsider(trigger, explicitInvoke) || !ThreadHelper.CheckAccess())
                return CompletionStartData.DoesNotParticipateInCompletion;
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!explicitInvoke && !SettingsStore.Instance.Current.ShowKeywordHints)
                return CompletionStartData.DoesNotParticipateInCompletion;

            var window = TextWindow.Around(triggerLocation.Snapshot, triggerLocation.Position, 8000, 4000);
            var position = triggerLocation.Position - window.Start;
            if (!SqlContext.TryGetKeywordContext(window.Text, position, out var context))
                return CompletionStartData.DoesNotParticipateInCompletion;

            var word = window.Text.Substring(context.Start, context.End - context.Start);
            if (word.Length > 0 && !context.Suggestions.Any(s => s.Text.StartsWith(word, StringComparison.OrdinalIgnoreCase)))
                return CompletionStartData.DoesNotParticipateInCompletion;     // a name or alias is being typed, not a keyword
            // Column completion starts on the first letter. A function must join that same
            // session immediately or it cannot appear alongside the columns later.
            var startsFunction = word.Length == 1 && context.Suggestions.Any(s =>
                s.Text.StartsWith(word, StringComparison.OrdinalIgnoreCase) &&
                (s.Text.EndsWith("(", StringComparison.Ordinal) || s.Text.EndsWith("()", StringComparison.Ordinal)));
            if (!explicitInvoke && word.Length < MinTypedCharacters && !startsFunction)
                return CompletionStartData.DoesNotParticipateInCompletion;

            _suggestions = context.Suggestions;
            _lowercase = word.Length > 0 && word.Any(char.IsLetter) ? word == word.ToLowerInvariant() : SqlContext.PrefersLowercaseKeywords(window.Text, position);

            // Right behind a closing bracket or a name there is no space to separate the keyword from it
            _leadingSpace = word.Length == 0 && position > 0 && !char.IsWhiteSpace(window.Text[position - 1]) && window.Text[position - 1] != '(';

            var span = new SnapshotSpan(triggerLocation.Snapshot, window.Start + context.Start, context.End - context.Start);
            return new CompletionStartData(CompletionParticipation.ProvidesItems, span);
        }

        public Task<CompletionContext> GetCompletionContextAsync(IAsyncCompletionSession session, CompletionTrigger trigger,
            SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
        {
            var suggestions = _suggestions;
            if (suggestions == null)
                return Task.FromResult(CompletionContext.Empty);

            var lowercase = _lowercase;
            var leadingSpace = _leadingSpace;
            var items = suggestions.Select((s, i) =>
            {
                var insert = lowercase ? s.InsertText.ToLowerInvariant() : s.InsertText;
                var function = s.Text.EndsWith("(", StringComparison.Ordinal)
                    ? SqlFunctionSignatures.Find(s.Text.Substring(0, s.Text.Length - 1)) : null;
                var item = new CompletionItem(
                    displayText: lowercase ? s.Text.ToLowerInvariant() : s.Text,
                    source: this,
                    icon: Icon,
                    filters: ImmutableArray<CompletionFilter>.Empty,
                    suffix: function?.Syntax ?? s.Description,
                    insertText: (leadingSpace ? " " : "") + insert,
                    sortText: "0" + i.ToString("D3"),
                    filterText: s.Text,
                    attributeIcons: ImmutableArray<ImageElement>.Empty);
                item.Properties.AddProperty(KeywordKey, s);
                return item;
            }).ToImmutableArray();

            // Ctrl+Space was asked for, so Enter may take the first one; while typing it must stay a new line
            var explicitInvoke = trigger.Reason == CompletionTriggerReason.Invoke || trigger.Reason == CompletionTriggerReason.InvokeAndCommitIfUnique;
            return Task.FromResult(new CompletionContext(items, null, explicitInvoke ? InitialSelectionHint.RegularSelection : InitialSelectionHint.SoftSelection));
        }

        public Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token) =>
            Task.FromResult<object>(item.Properties.TryGetProperty(KeywordKey, out KeywordSuggestion s) && s.Description.Length > 0 ? s.Description : null);

        private static bool ShouldConsider(CompletionTrigger trigger, bool explicitInvoke)
        {
            if (explicitInvoke || trigger.Reason == CompletionTriggerReason.Backspace || trigger.Reason == CompletionTriggerReason.Deletion)
                return true;
            if (trigger.Reason != CompletionTriggerReason.Insertion)
                return false;

            var c = trigger.Character;
            return char.IsLetter(c) || c == '_';
        }
    }
}
