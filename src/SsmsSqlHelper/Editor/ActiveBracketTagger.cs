using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows.Media;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using SsmsSqlHelper.Parsing;
using SsmsSqlHelper.Settings;

namespace SsmsSqlHelper.Editor
{
    internal static class ActiveBracketFormatNames
    {
        public const string Border = "SQL Helper Active Bracket Border";
    }

    [Export(typeof(EditorFormatDefinition))]
    [Name(ActiveBracketFormatNames.Border)]
    [UserVisible(true)]
    internal sealed class ActiveBracketBorderFormat : MarkerFormatDefinition
    {
        public ActiveBracketBorderFormat()
        {
            DisplayName = "SQL Helper: Active Bracket Border";
            Border = new Pen(new SolidColorBrush(Color.FromRgb(255, 64, 64)), 1.5);
            Fill = Brushes.Transparent;
            ZOrder = 10;
        }
    }

    [Export(typeof(IViewTaggerProvider))]
    [ContentType("SQL")]
    [TagType(typeof(TextMarkerTag))]
    internal sealed class ActiveBracketTaggerProvider : IViewTaggerProvider
    {
        public ITagger<T> CreateTagger<T>(ITextView view, ITextBuffer buffer) where T : ITag
        {
            if (view.TextBuffer != buffer)
                return null;
            return view.Properties.GetOrCreateSingletonProperty(() => new ActiveBracketTagger(view)) as ITagger<T>;
        }
    }

    internal sealed class ActiveBracketTagger : ITagger<TextMarkerTag>
    {
        private readonly ITextView _view;
        private readonly TextMarkerTag _tag = new TextMarkerTag(ActiveBracketFormatNames.Border);
        private ITextSnapshot _snapshot;
        private IReadOnlyDictionary<int, BracketPair> _pairs;
        private BracketPair? _active;

        public ActiveBracketTagger(ITextView view)
        {
            _view = view;
            _view.Caret.PositionChanged += OnCaretPositionChanged;
            _view.LayoutChanged += OnLayoutChanged;
            _view.Closed += OnClosed;
            SettingsStore.Instance.Changed += OnSettingsChanged;
            UpdateAtCaret();
        }

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        public IEnumerable<ITagSpan<TextMarkerTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (spans.Count == 0 || spans[0].Snapshot != _snapshot || !_active.HasValue)
                yield break;

            var pair = _active.Value;
            if (Contains(spans, pair.Open))
                yield return new TagSpan<TextMarkerTag>(new SnapshotSpan(_snapshot, pair.Open, 1), _tag);
            if (Contains(spans, pair.Close))
                yield return new TagSpan<TextMarkerTag>(new SnapshotSpan(_snapshot, pair.Close, 1), _tag);
        }

        private static bool Contains(NormalizedSnapshotSpanCollection spans, int position)
        {
            foreach (var span in spans)
            {
                if (span.Start.Position <= position && position < span.End.Position)
                    return true;
                if (span.Start.Position > position)
                    break;
            }
            return false;
        }

        private void OnCaretPositionChanged(object sender, CaretPositionChangedEventArgs e) => UpdateAtCaret();

        private void OnSettingsChanged(object sender, EventArgs e) => UpdateAtCaret();

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            if (e.NewSnapshot != e.OldSnapshot)
                UpdateAtCaret();
        }

        private void UpdateAtCaret()
        {
            if (_view.IsClosed)
                return;

            var snapshot = _view.TextBuffer.CurrentSnapshot;
            var oldSnapshot = _snapshot;
            var oldPair = _active;
            var enabled = SettingsStore.Instance.Current.HighlightActiveBracket;
            if (_snapshot != snapshot || (enabled && _pairs == null))
            {
                _pairs = enabled ? BracketColorizer.IndexPairs(snapshot.GetText()) : null;
                _snapshot = snapshot;
            }

            var caret = _view.Caret.Position.BufferPosition.Position;
            _active = enabled && BracketColorizer.TryGetPairAtCaret(_pairs, caret, out var pair) ? pair : (BracketPair?)null;

            if (oldSnapshot != snapshot)
                TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(new SnapshotSpan(snapshot, 0, snapshot.Length)));
            else if (oldPair?.Open != _active?.Open || oldPair?.Close != _active?.Close)
            {
                RaisePairChanged(snapshot, oldPair);
                RaisePairChanged(snapshot, _active);
            }
        }

        private void RaisePairChanged(ITextSnapshot snapshot, BracketPair? pair)
        {
            if (!pair.HasValue)
                return;
            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(new SnapshotSpan(snapshot, pair.Value.Open, 1)));
            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(new SnapshotSpan(snapshot, pair.Value.Close, 1)));
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _view.Caret.PositionChanged -= OnCaretPositionChanged;
            _view.LayoutChanged -= OnLayoutChanged;
            _view.Closed -= OnClosed;
            SettingsStore.Instance.Changed -= OnSettingsChanged;
        }
    }
}
