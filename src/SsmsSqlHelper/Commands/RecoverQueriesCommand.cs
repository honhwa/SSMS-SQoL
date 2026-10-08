using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SsmsSqlHelper.Backup;
using SsmsSqlHelper.Diagnostics;
using SsmsSqlHelper.UI;
using Task = System.Threading.Tasks.Task;

namespace SsmsSqlHelper.Commands
{
    /// <summary>
    /// Tools > SQL Helper > Recover Unsaved Queries, and the question asked a few seconds after SSMS starts when an earlier session
    /// left unsaved tabs behind (it crashed, or was ended with Task Manager).
    /// </summary>
    internal static class RecoverQueriesCommand
    {
        private const int IdYes = 6;
        private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(8);

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            // Claim this session before anything else, so another SSMS that starts now sees it is alive
            BackupService.Instance.Start();

            if (await package.GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commandService)
            {
                commandService.AddCommand(new MenuCommand((s, e) => OpenWindow(package, askFirst: false),
                    new CommandID(PackageIds.CommandSet, PackageIds.RecoverQueriesCommandId)));
            }
            else
            {
                Log.Error("IMenuCommandService unavailable; command not registered");
            }

#pragma warning disable VSSDK007 // Fire-and-forget: the question must not hold up starting SSMS
            package.JoinableTaskFactory.RunAsync(async () =>
            {
                // SSMS is busy opening windows for the first moments; asking then would only get in the way
                await Task.Delay(StartupDelay);
                await package.JoinableTaskFactory.SwitchToMainThreadAsync();
                OpenWindow(package, askFirst: true);
            }).FileAndForget("SsmsSqlHelper/RecoverQueriesAtStartup");
#pragma warning restore VSSDK007
        }

        private static void OpenWindow(AsyncPackage package, bool askFirst)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var entries = BackupService.Instance.FindOrphans();
                if (entries.Count == 0)
                {
                    if (!askFirst)
                        StatusBar.Show("SQL Helper: no unsaved queries to recover");
                    return;
                }

                if (askFirst)
                {
                    var answer = VsShellUtilities.ShowMessageBox(package,
                        (entries.Count == 1 ? "1 query tab" : entries.Count + " query tabs") +
                        " had unsaved changes when SSMS last stopped unexpectedly.\n\nLook at " + (entries.Count == 1 ? "it" : "them") + " now?\n" +
                        "(Choose No to decide later with Tools > SQL Helper > Recover Unsaved Queries.)",
                        "SQL Helper", OLEMSGICON.OLEMSGICON_QUERY, OLEMSGBUTTON.OLEMSGBUTTON_YESNO, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                    if (answer != IdYes)
                        return;
                }

                new RecoveryWindow(package, entries).ShowModal();
            }
            catch (Exception ex)
            {
                Log.Error("Recovery window failed", ex);
                StatusBar.Show("SQL Helper: could not open the recovery window - " + ex.Message);
            }
        }
    }
}
