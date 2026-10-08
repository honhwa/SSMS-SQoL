using System;
using System.ComponentModel.Design;
using System.IO;
using System.Runtime.Serialization;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SsmsSqlHelper.Diagnostics;
using SsmsSqlHelper.Settings;
using SsmsSqlHelper.Snippets;
using SsmsSqlHelper.UI;
using Task = System.Threading.Tasks.Task;

namespace SsmsSqlHelper.Commands
{
    /// <summary>
    /// Tools > SQL Helper > Edit Snippets opens the editor window. The editor can also open
    /// snippets.json as a document; changes apply as soon as it is saved.
    /// </summary>
    internal static class EditSnippetsCommand
    {
        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            if (!(await package.GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commandService))
            {
                Log.Error("IMenuCommandService unavailable; command not registered");
                return;
            }

            commandService.AddCommand(new MenuCommand((s, e) => OpenEditor(package),
                new CommandID(PackageIds.CommandSet, PackageIds.EditSnippetsCommandId)));
        }

        private static void OpenEditor(IServiceProvider serviceProvider)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                System.Collections.Generic.List<Snippet> snippets;
                try
                {
                    snippets = SnippetStore.Instance.ReadFileForEditing();
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SerializationException)
                {
                    Log.Error("snippets.json could not be read for the editor", ex);
                    if (!AskHowToProceed(serviceProvider, ex.Message, out snippets))
                        return;
                    if (snippets == null)
                    {
                        OpenJson(serviceProvider);
                        return;
                    }
                }

                var window = new SnippetEditorWindow(snippets, SettingsStore.Instance.Current);
                window.ShowModal();

                if (window.OpenJsonRequested)
                    OpenJson(serviceProvider);
            }
            catch (Exception ex)
            {
                Log.Error("Snippet editor failed", ex);
                VsShellUtilities.ShowMessageBox(serviceProvider, "The snippet editor could not be opened:\n\n" + ex.Message, "SQL Helper",
                    OLEMSGICON.OLEMSGICON_CRITICAL, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
        }

        /// <summary>The file is damaged (usually a typo made while editing by hand): fix it, or start over from the defaults.</summary>
        /// <returns>False to cancel; otherwise <paramref name="snippets"/> is the defaults, or null meaning "open the file".</returns>
        private static bool AskHowToProceed(IServiceProvider serviceProvider, string reason, out System.Collections.Generic.List<Snippet> snippets)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            snippets = null;

            var answer = VsShellUtilities.ShowMessageBox(serviceProvider,
                "snippets.json could not be read:\n\n" + reason + "\n\n" +
                "Yes: open the file so you can fix it\nNo: start the editor from the default snippets (the file is replaced when you save)\nCancel: do nothing",
                "SQL Helper", OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_YESNOCANCEL, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

            const int yes = 6, no = 7;
            if (answer == yes)
                return true;
            if (answer == no)
            {
                snippets = SnippetStore.ReadDefaults();
                return true;
            }
            return false;
        }

        private static void OpenJson(IServiceProvider serviceProvider)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                SnippetStore.Instance.EnsureFileExists();
                VsShellUtilities.OpenDocument(serviceProvider, SnippetStore.FilePath);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to open " + SnippetStore.FilePath, ex);
            }
        }
    }
}
