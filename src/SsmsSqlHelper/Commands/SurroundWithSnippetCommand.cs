using System;
using System.ComponentModel.Design;
using System.Linq;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SsmsSqlHelper.Diagnostics;
using SsmsSqlHelper.Editor;
using SsmsSqlHelper.Snippets;
using SsmsSqlHelper.UI;
using Task = System.Threading.Tasks.Task;

namespace SsmsSqlHelper.Commands
{
    /// <summary>
    /// Tools > SQL Helper > Surround With Snippet. Wraps the selected text in a snippet whose body uses <c>$SELECTED$</c>
    /// (BEGIN TRANSACTION ... ROLLBACK, TRY/CATCH, ...). Assign a shortcut key to it in Tools > Options > Keyboard if you use it often.
    /// </summary>
    internal static class SurroundWithSnippetCommand
    {
        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            if (!(await package.GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commandService))
            {
                Log.Error("IMenuCommandService unavailable; command not registered");
                return;
            }

            commandService.AddCommand(new MenuCommand((s, e) => Execute(package),
                new CommandID(PackageIds.CommandSet, PackageIds.SurroundWithSnippetCommandId)));
        }

        private static void Execute(IServiceProvider serviceProvider)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var view = ActiveEditor.Current;
                if (view == null)
                {
                    StatusBar.Show("SQL Helper: click into a query window first");
                    return;
                }

                var candidates = SnippetStore.Instance.All.Where(s => (s.Body ?? "").Contains("$" + SnippetTemplate.Selected + "$")).ToList();
                if (candidates.Count == 0)
                {
                    VsShellUtilities.ShowMessageBox(serviceProvider,
                        "No snippet uses $SELECTED$ yet. Add one in Tools > SQL Helper > Edit Snippets (for example a BEGIN TRANSACTION or TRY/CATCH wrapper).",
                        "SQL Helper", OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                    return;
                }

                var picker = new SnippetPickerWindow("Surround with snippet",
                    "Wraps the selected text; with nothing selected the snippet is inserted at the caret. Enter to apply, Esc to cancel.", candidates);
                if (picker.ShowModal() != true || picker.Chosen == null)
                    return;

                // The editor may have lost focus to the dialog, but its caret and selection are untouched
                var selection = view.Selection.IsEmpty ? default(Microsoft.VisualStudio.Text.SnapshotSpan?) : view.Selection.SelectedSpans[0];
                var replace = selection?.Span ?? new Microsoft.VisualStudio.Text.Span(view.Caret.Position.BufferPosition.Position, 0);
                SnippetInserter.Insert(view, replace, picker.Chosen, selection?.GetText() ?? "");
                view.VisualElement.Focus();
            }
            catch (Exception ex)
            {
                Log.Error("Surround with snippet failed", ex);
                StatusBar.Show("SQL Helper: surround failed - " + ex.Message);
            }
        }
    }
}
