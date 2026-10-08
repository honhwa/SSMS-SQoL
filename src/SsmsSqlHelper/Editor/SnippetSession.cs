using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SsmsSqlHelper.Snippets;

namespace SsmsSqlHelper.Editor
{
    /// <summary>
    /// An expanded snippet whose tab stops are still being visited: Tab goes to the next stop, Shift+Tab to the previous one,
    /// Esc (or clicking away) ends it. Stops are tracked, so editing before or inside them keeps them in place.
    /// </summary>
    internal sealed class SnippetSession
    {
        private readonly IWpfTextView _view;
        private readonly List<List<ITrackingSpan>> _groups;   // one group per stop number; the first span is the one the caret visits
        private readonly ITrackingPoint _final;
        private int _current;

        private SnippetSession(IWpfTextView view, List<List<ITrackingSpan>> groups, ITrackingPoint final)
        {
            _view = view;
            _groups = groups;
            _final = final;
        }

        public static SnippetSession Get(ITextView view) =>
            view.Properties.TryGetProperty(typeof(SnippetSession), out SnippetSession session) ? session : null;

        public static void End(ITextView view) => view.Properties.RemoveProperty(typeof(SnippetSession));

        /// <summary>Starts visiting the stops of a snippet just inserted at <paramref name="insertStart"/> and selects the first one.</summary>
        public static void Begin(IWpfTextView view, int insertStart, IReadOnlyList<SnippetStop> stops, int finalOffset)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            End(view);

            var snapshot = view.TextSnapshot;
            var groups = stops
                .GroupBy(s => s.Index)
                .Select(g => g.Select(s => snapshot.CreateTrackingSpan(insertStart + s.Start, s.Length, SpanTrackingMode.EdgeInclusive)).ToList())
                .ToList();

            var session = new SnippetSession(view, groups, snapshot.CreateTrackingPoint(insertStart + finalOffset, PointTrackingMode.Positive));
            view.Properties[typeof(SnippetSession)] = session;
            session.Select(0);
        }

        /// <summary>Tab: on to the next stop, or to the final position after the last one. False if the caret has left the snippet.</summary>
        public bool MoveNext()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!CaretInCurrentStop())
            {
                End(_view);
                return false;
            }

            SyncMirrors(_current);
            if (_current == _groups.Count - 1)
            {
                var final = _final.GetPoint(_view.TextSnapshot);
                _view.Selection.Clear();
                _view.Caret.MoveTo(final);
                End(_view);
                return true;
            }

            Select(_current + 1);
            return true;
        }

        /// <summary>Shift+Tab: back to the previous stop (the first stop stays put).</summary>
        public bool MovePrevious()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!CaretInCurrentStop())
            {
                End(_view);
                return false;
            }

            SyncMirrors(_current);
            Select(System.Math.Max(0, _current - 1));
            return true;
        }

        private void Select(int group)
        {
            _current = group;
            var span = _groups[group][0].GetSpan(_view.TextSnapshot);
            if (span.Length > 0)
            {
                _view.Selection.Select(span, false);
                _view.Caret.MoveTo(span.End);
            }
            else
            {
                _view.Selection.Clear();
                _view.Caret.MoveTo(span.Start);
            }
            _view.ViewScroller.EnsureSpanVisible(span);
        }

        private bool CaretInCurrentStop()
        {
            var snapshot = _view.TextSnapshot;
            var span = _groups[_current][0].GetSpan(snapshot);
            var caret = _view.Caret.Position.BufferPosition;
            return caret >= span.Start && caret <= span.End;
        }

        // Leaving a stop copies what was typed into every other place that uses the same number
        private void SyncMirrors(int group)
        {
            var spans = _groups[group];
            if (spans.Count < 2)
                return;

            var snapshot = _view.TextSnapshot;
            var text = spans[0].GetText(snapshot);
            using (var edit = _view.TextBuffer.CreateEdit())
            {
                foreach (var mirror in spans.Skip(1))
                {
                    var span = mirror.GetSpan(snapshot);
                    if (span.GetText() != text)
                        edit.Replace(span, text);
                }
                edit.Apply();
            }
        }
    }
}
