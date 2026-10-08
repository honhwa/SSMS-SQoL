using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
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

namespace SsmsSqlHelper.Editor
{
    [Export(typeof(IAsyncCompletionSourceProvider))]
    [Name("SsmsSqlHelper.TableCompletion")]
    [ContentType("SQL")]
    internal sealed class TableCompletionSourceProvider : IAsyncCompletionSourceProvider
    {
        public IAsyncCompletionSource GetOrCreate(ITextView textView) =>
            textView.Properties.GetOrCreateSingletonProperty(() => new TableCompletionSource());
    }

    /// <summary>Offers tables and views wherever a table name belongs (FROM, JOIN, INTO, UPDATE, ...).</summary>
    internal sealed class TableCompletionSource : IAsyncCompletionSource
    {
        /// <summary>Key of the <see cref="TableInfo"/> stored on each item, read by <see cref="TableCommitManager"/>.</summary>
        internal static readonly object TableKey = typeof(TableInfo);

        private static readonly ImageElement TableIcon = Icon(KnownMonikers.Table, "Table");
        private static readonly ImageElement ViewIcon = Icon(KnownMonikers.View, "View");

        private DbMetadata _metadata;
        private DbMetadata _itemsMetadata;
        private ImmutableArray<CompletionItem> _items;
        private Dictionary<TableInfo, string> _related;

        public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            if (!ShouldConsider(trigger) || !ThreadHelper.CheckAccess())
                return CompletionStartData.DoesNotParticipateInCompletion;
            ThreadHelper.ThrowIfNotOnUIThread();

            var window = TextWindow.Around(triggerLocation.Snapshot, triggerLocation.Position, 4000, 200);
            if (!SqlContext.TryGetTableNameSpan(window.Text, triggerLocation.Position - window.Start, out var start, out var end))
                return CompletionStartData.DoesNotParticipateInCompletion;

            _metadata = EditorContext.GetMetadata();
            if (_metadata == null || _metadata.Tables.Count == 0)
                return CompletionStartData.DoesNotParticipateInCompletion;

            _related = FindRelatedTables(_metadata, window.Text, start);

            var span = new SnapshotSpan(triggerLocation.Snapshot, window.Start + start, end - start);
            return new CompletionStartData(CompletionParticipation.ProvidesItems, span);
        }

        public Task<CompletionContext> GetCompletionContextAsync(IAsyncCompletionSession session, CompletionTrigger trigger,
            SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
        {
            var metadata = _metadata;
            if (metadata == null)
                return Task.FromResult(CompletionContext.Empty);

            // Building thousands of items per keystroke is wasteful; reuse them while the metadata is unchanged
            if (!ReferenceEquals(metadata, _itemsMetadata))
            {
                _items = metadata.Tables.Select(CreateItem).ToImmutableArray();
                _itemsMetadata = metadata;
            }

            var items = _items;
            var related = _related;
            if (related != null)
            {
                // After JOIN: tables linked to the ones already in the query go first, labelled with who they join to
                var builder = items.ToBuilder();
                for (var i = 0; i < builder.Count; i++)
                {
                    if (builder[i].Properties.TryGetProperty(TableKey, out TableInfo table) && related.TryGetValue(table, out var qualifier))
                        builder[i] = CreateItem(table, sortPrefix: "0_", suffix: $"{table.Schema} · joins {qualifier}");
                }
                items = builder.ToImmutable();
            }

            return Task.FromResult(new CompletionContext(items, null, InitialSelectionHint.RegularSelection));
        }

        /// <summary>For a table name after a plain JOIN: every table linked (by foreign key or column name) to a table already in the query.</summary>
        private static Dictionary<TableInfo, string> FindRelatedTables(DbMetadata metadata, string text, int nameStart)
        {
            if (SqlContext.GetJoinScope(text, nameStart) == null)
                return null;

            var scope = ContextExpander.JoinTablesBefore(text, nameStart, metadata);
            if (scope.Count == 0)
                return null;

            var related = new Dictionary<TableInfo, string>();
            foreach (var table in metadata.Tables)
            {
                foreach (var s in scope)
                {
                    if (JoinSuggester.AreRelated(metadata, table, s.Table))
                    {
                        related[table] = s.Qualifier;
                        break;
                    }
                }
            }
            return related.Count > 0 ? related : null;
        }

        public Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token)
        {
            if (!item.Properties.TryGetProperty(TableKey, out TableInfo table))
                return Task.FromResult<object>(null);

            const int maxColumns = 25;
            var sb = new StringBuilder().Append(table).Append(table.IsView ? " (view)" : " (table)");
            foreach (var c in table.Columns.Take(maxColumns))
                sb.AppendLine().Append("  ").Append(c.Name).Append(' ').Append(c.DisplayType).Append(c.IsPrimaryKey ? "  PK" : "");
            if (table.Columns.Count > maxColumns)
                sb.AppendLine().Append($"  ... {table.Columns.Count - maxColumns} more");
            return Task.FromResult<object>(sb.ToString());
        }

        private CompletionItem CreateItem(TableInfo table) => CreateItem(table, "", table.Schema);

        private CompletionItem CreateItem(TableInfo table, string sortPrefix, string suffix)
        {
            var item = new CompletionItem(
                displayText: table.Name,
                source: this,
                icon: table.IsView ? ViewIcon : TableIcon,
                filters: ImmutableArray<CompletionFilter>.Empty,
                suffix: suffix,
                insertText: table.QualifiedName,
                sortText: sortPrefix + table.Name,
                filterText: table.Schema + "." + table.Name,
                attributeIcons: ImmutableArray<ImageElement>.Empty);
            item.Properties.AddProperty(TableKey, table);
            return item;
        }

        private static bool ShouldConsider(CompletionTrigger trigger)
        {
            switch (trigger.Reason)
            {
                case CompletionTriggerReason.Invoke:
                case CompletionTriggerReason.InvokeAndCommitIfUnique:
                case CompletionTriggerReason.Backspace:     // editing a name that is already there: whether a list belongs is decided by the position
                case CompletionTriggerReason.Deletion:
                    return true;
                case CompletionTriggerReason.Insertion:
                    var c = trigger.Character;
                    return char.IsLetterOrDigit(c) || c == '_' || c == ' ' || c == '.' || c == '[' || c == '#';
                default:
                    return false;
            }
        }

        private static ImageElement Icon(Microsoft.VisualStudio.Imaging.Interop.ImageMoniker moniker, string name) =>
            new ImageElement(new ImageId(moniker.Guid, moniker.Id), name);
    }
}
