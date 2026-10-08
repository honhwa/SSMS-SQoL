using System.ComponentModel.Composition;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using SsmsSqlHelper.Diagnostics;
using SsmsSqlHelper.Metadata;
using SsmsSqlHelper.Ssms;

namespace SsmsSqlHelper.Editor
{
    /// <summary>Attaches <see cref="SnippetCommandFilter"/> to every SSMS query editor.</summary>
    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType("SQL")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal sealed class SnippetFilterProvider : IVsTextViewCreationListener
    {
        [Import]
        internal IVsEditorAdaptersFactoryService EditorAdapters { get; set; }

        [Import]
        internal ICompletionBroker CompletionBroker { get; set; }

        [Import]
        internal IAsyncCompletionBroker AsyncCompletionBroker { get; set; }

        public void VsTextViewCreated(IVsTextView textViewAdapter)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var view = EditorAdapters.GetWpfTextView(textViewAdapter);
            if (view == null)
                return;

            // Warm the metadata cache whenever a query window is focused, so it's ready before it's needed
            view.GotAggregateFocus += (s, e) =>
            {
                ActiveEditor.Set(view);
                PrefetchMetadata();
            };

            var filter = new SnippetCommandFilter(view, CompletionBroker, AsyncCompletionBroker);
            if (ErrorHandler.Succeeded(textViewAdapter.AddCommandFilter(filter, out var next)))
                filter.Next = next;
            else
                Log.Error("AddCommandFilter failed; snippets disabled for this editor");
        }

        private static void PrefetchMetadata()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var connection = SsmsConnectionAdapter.GetActiveConnection();
            MetadataService.Instance.TryGet(connection);
            MetadataService.Instance.RequestFreshnessCheck(connection);
        }
    }
}
