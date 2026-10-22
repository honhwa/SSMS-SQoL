using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SsmsSqlHelper.Metadata;

namespace SsmsSqlHelper.Editor
{
    [Export(typeof(IAsyncCompletionItemManagerProvider))]
    [Name("SsmsSqlHelper.ObjectCompletionItemManager")]
    [ContentType("SQL")]
    [Order(Before = PredefinedCompletionNames.DefaultCompletionItemManager)]
    internal sealed class ObjectCompletionItemManagerProvider : IAsyncCompletionItemManagerProvider
    {
        [ImportMany]
        internal IEnumerable<Lazy<IAsyncCompletionItemManagerProvider, IDictionary<string, object>>> Providers { get; set; }

        public IAsyncCompletionItemManager GetOrCreate(ITextView textView)
        {
            var provider = Providers.FirstOrDefault(p => p.Metadata.TryGetValue("Name", out var name) &&
                string.Equals(name as string, PredefinedCompletionNames.DefaultCompletionItemManager, StringComparison.Ordinal));
            return provider == null ? null : textView.Properties.GetOrCreateSingletonProperty(() =>
                new ObjectCompletionItemManager(provider.Value.GetOrCreate(textView)));
        }
    }

    /// <summary>Keep the editor's filtering/highlighting, then rank object names again on each typing update.</summary>
    internal sealed class ObjectCompletionItemManager : IAsyncCompletionItemManager
    {
        private readonly IAsyncCompletionItemManager _inner;

        public ObjectCompletionItemManager(IAsyncCompletionItemManager inner) => _inner = inner;

        public Task<ImmutableArray<CompletionItem>> SortCompletionListAsync(IAsyncCompletionSession session,
            AsyncCompletionSessionInitialDataSnapshot data, CancellationToken token) =>
            _inner.SortCompletionListAsync(session, data, token);

        public async Task<FilteredCompletionModel> UpdateCompletionListAsync(IAsyncCompletionSession session,
            AsyncCompletionSessionDataSnapshot data, CancellationToken token)
        {
            var model = await _inner.UpdateCompletionListAsync(session, data, token).ConfigureAwait(false);
            if (model == null || model.Items.Length < 2 || !model.Items.All(i => ObjectName(i.CompletionItem) != null))
                return model;

            var word = CompletionSortOrder.ObjectNameWord(session.ApplicableToSpan.GetText(data.Snapshot));
            if (word.Length == 0)
                return model;

            token.ThrowIfCancellationRequested();
            // OrderBy is stable: JOIN relevance and the editor's order still break ties within each match group.
            var items = model.Items.OrderBy(i => CompletionSortOrder.ObjectMatch(ObjectName(i.CompletionItem), word))
                .ToImmutableArray();
            return new FilteredCompletionModel(items, model.SelectedItemIndex < 0 ? -1 : 0,
                model.Filters, model.SelectionHint, model.CenterSelection, model.UniqueItem);
        }

        private static string ObjectName(CompletionItem item)
        {
            if (item.Properties.TryGetProperty(TableCompletionSource.TableKey, out TableInfo table))
                return table.Name;
            return item.Properties.TryGetProperty(TableCompletionSource.FunctionKey, out TableFunctionInfo function)
                ? function.Name : null;
        }
    }
}
