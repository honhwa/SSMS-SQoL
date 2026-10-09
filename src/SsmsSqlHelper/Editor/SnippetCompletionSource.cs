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
using SsmsSqlHelper.Snippets;

namespace SsmsSqlHelper.Editor
{
    [Export(typeof(IAsyncCompletionSourceProvider))]
    [Name("SsmsSqlHelper.SnippetCompletion")]
    [ContentType("SQL")]
    internal sealed class SnippetSourceProvider : IAsyncCompletionSourceProvider
    {
        public IAsyncCompletionSource GetOrCreate(ITextView textView) =>
            textView.Properties.GetOrCreateSingletonProperty(() => new SnippetCompletionSource());
    }

    /// <summary>
    /// While typing a word that starts one of the snippet shortcuts (<c>ss</c> → <c>ssf</c>), lists the matching
    /// shortcuts with what they expand to. Tab accepts; Enter still starts a new line.
    /// </summary>
    internal sealed class SnippetCompletionSource : IAsyncCompletionSource
    {
        /// <summary>Key of the <see cref="Snippet"/> stored on each item, read by <see cref="SnippetCommitManager"/>.</summary>
        internal static readonly object SnippetKey = typeof(Snippet);

        // Column completion opens on the first character. Join that same session
        // immediately, otherwise a later "ssf" cannot enter its already-open list.
        private const int MinTypedCharacters = 1;
        private static readonly ImageElement Icon = new ImageElement(
            new ImageId(KnownMonikers.Snippet.Guid, KnownMonikers.Snippet.Id), "Snippet");

        public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            var explicitInvoke = trigger.Reason == CompletionTriggerReason.Invoke || trigger.Reason == CompletionTriggerReason.InvokeAndCommitIfUnique;
            if (!ShouldConsider(trigger, explicitInvoke) || !ThreadHelper.CheckAccess())
                return CompletionStartData.DoesNotParticipateInCompletion;
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!SettingsStore.Instance.Current.ShowSnippetHints && !explicitInvoke)
                return CompletionStartData.DoesNotParticipateInCompletion;

            var line = triggerLocation.GetContainingLine();
            var before = new SnapshotSpan(line.Start, triggerLocation).GetText();
            if (!SnippetExpander.TryFindShortcut(before, explicitInvoke, out var wordStart))
                return CompletionStartData.DoesNotParticipateInCompletion;

            var word = before.Substring(wordStart);
            if (!explicitInvoke && word.Length < MinTypedCharacters)
                return CompletionStartData.DoesNotParticipateInCompletion;

            var snippets = SnippetStore.Instance.All;
            if (word.Length > 0 && !snippets.Any(s => s.Shortcut.StartsWith(word, StringComparison.OrdinalIgnoreCase)))
                return CompletionStartData.DoesNotParticipateInCompletion;

            // A table name or a join condition belongs here instead: those lists take priority
            var window = TextWindow.Around(triggerLocation.Snapshot, triggerLocation.Position, 4000, 200);
            var position = triggerLocation.Position - window.Start;
            if (SqlContext.TryGetTableNameSpan(window.Text, position, out _, out _) ||
                SqlContext.TryGetDatabaseNameSpan(window.Text, position, out _, out _) ||
                SqlContext.TryGetJoinOn(window.Text, position, out _, out _, out _, out _))
                return CompletionStartData.DoesNotParticipateInCompletion;

            // Ctrl+Space with nothing typed lists every snippet only where a statement can start; in the middle of one
            // the keywords and columns that fit are what is wanted, not fifteen unrelated snippets
            if (word.Length == 0 && !(SqlContext.TryGetKeywordContext(window.Text, position, out var keywordContext) && keywordContext.IsStatementStart))
                return CompletionStartData.DoesNotParticipateInCompletion;

            var span = new SnapshotSpan(triggerLocation.Snapshot, line.Start.Position + wordStart, word.Length);
            return new CompletionStartData(CompletionParticipation.ProvidesItems, span);
        }

        public Task<CompletionContext> GetCompletionContextAsync(IAsyncCompletionSession session, CompletionTrigger trigger,
            SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
        {
            var items = SnippetStore.Instance.All.Select(CreateItem).ToImmutableArray();

            // Soft selection: the first match is highlighted for Tab, but Enter is not hijacked while typing ordinary SQL
            return Task.FromResult(new CompletionContext(items, null, InitialSelectionHint.SoftSelection));
        }

        public Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token)
        {
            if (!item.Properties.TryGetProperty(SnippetKey, out Snippet snippet))
                return Task.FromResult<object>(null);

            // Show the body as it will look, with | where the caret ends up
            var preview = snippet.Body.Replace("\r\n", "\n").Replace(SnippetExpander.CursorMarker, "|");
            var text = string.IsNullOrWhiteSpace(snippet.Description) ? preview : snippet.Description + "\n\n" + preview;
            return Task.FromResult<object>(text);
        }

        private CompletionItem CreateItem(Snippet snippet)
        {
            var item = new CompletionItem(
                displayText: snippet.Shortcut,
                source: this,
                icon: Icon,
                filters: ImmutableArray<CompletionFilter>.Empty,
                suffix: FirstLine(snippet),
                insertText: snippet.Shortcut,
                sortText: snippet.Shortcut,
                filterText: snippet.Shortcut,
                attributeIcons: ImmutableArray<ImageElement>.Empty);
            item.Properties.AddProperty(SnippetKey, snippet);
            return item;
        }

        // What the shortcut stands for, one line: the description if there is one, else the start of the body
        private static string FirstLine(Snippet snippet)
        {
            var text = string.IsNullOrWhiteSpace(snippet.Description)
                ? snippet.Body.Replace(SnippetExpander.CursorMarker, "").Replace("\r\n", "\n").Split('\n')[0].Trim()
                : snippet.Description;
            return text.Length > 60 ? text.Substring(0, 57) + "..." : text;
        }

        private static bool ShouldConsider(CompletionTrigger trigger, bool explicitInvoke)
        {
            if (explicitInvoke || trigger.Reason == CompletionTriggerReason.Backspace || trigger.Reason == CompletionTriggerReason.Deletion)
                return true;
            if (trigger.Reason != CompletionTriggerReason.Insertion)
                return false;

            var c = trigger.Character;
            return char.IsLetterOrDigit(c) || c == '_';
        }
    }
}
