using System;
using Microsoft.VisualStudio.Text.Editor;

namespace SsmsSqlHelper.Editor
{
    /// <summary>The query editor that last had focus: what commands from menus act on.</summary>
    internal static class ActiveEditor
    {
        private static WeakReference<IWpfTextView> _current;

        public static void Set(IWpfTextView view) => _current = new WeakReference<IWpfTextView>(view);

        public static IWpfTextView Current =>
            _current != null && _current.TryGetTarget(out var view) && !view.IsClosed ? view : null;
    }
}
