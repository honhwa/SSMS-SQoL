using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SsmsSqlHelper.Diagnostics;
using SsmsSqlHelper.Metadata;
using SsmsSqlHelper.Ssms;
using Task = System.Threading.Tasks.Task;

namespace SsmsSqlHelper.Commands
{
    /// <summary>Tools > SQL Helper > Refresh Metadata. Reloads tables/columns after schema changes.</summary>
    internal static class RefreshMetadataCommand
    {
        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            if (!(await package.GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commandService))
            {
                Log.Error("IMenuCommandService unavailable; command not registered");
                return;
            }

            var id = new CommandID(PackageIds.CommandSet, PackageIds.RefreshMetadataCommandId);
            commandService.AddCommand(new MenuCommand((s, e) => package.JoinableTaskFactory.RunAsync(ExecuteAsync).FileAndForget("SsmsSqlHelper/RefreshMetadata"), id));
        }

        private static async Task ExecuteAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var conn = SsmsConnectionAdapter.GetActiveConnection();
            if (conn == null)
            {
                StatusBar.Show("SQL Helper: no active query window connection");
                return;
            }

            StatusBar.Show($"SQL Helper: loading metadata for {conn.Database}...");
            try
            {
                var metadata = await MetadataService.Instance.RefreshAsync(conn);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                StatusBar.Show($"SQL Helper: {metadata.Database} - {metadata.Tables.Count} tables/views, " +
                               $"{metadata.ColumnCount} columns ({metadata.LoadDuration.TotalMilliseconds:N0} ms)");
            }
            catch (Exception ex)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                StatusBar.Show("SQL Helper: metadata load failed - " + ex.Message);
            }
        }
    }
}
