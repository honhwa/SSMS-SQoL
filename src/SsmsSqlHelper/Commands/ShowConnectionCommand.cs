using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SsmsSqlHelper.Diagnostics;
using SsmsSqlHelper.Metadata;
using SsmsSqlHelper.Ssms;
using Task = System.Threading.Tasks.Task;

namespace SsmsSqlHelper.Commands
{
    /// <summary>Tools > SQL Helper > Show Active Connection. Diagnostic view of connection + metadata cache.</summary>
    internal static class ShowConnectionCommand
    {
        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            if (!(await package.GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commandService))
            {
                Log.Error("IMenuCommandService unavailable; command not registered");
                return;
            }

            var id = new CommandID(PackageIds.CommandSet, PackageIds.ShowConnectionCommandId);
            commandService.AddCommand(new MenuCommand((s, e) => Execute(package), id));
        }

        private static void Execute(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var conn = SsmsConnectionAdapter.GetActiveConnection();
            string message;
            if (conn == null)
            {
                message = "No active query window connection.";
            }
            else
            {
                var metadata = MetadataService.Instance.TryGet(conn);
                var metadataText = metadata == null
                    ? "loading... (run again in a moment)"
                    : $"{metadata.Tables.Count} tables/views, {metadata.ColumnCount} columns (loaded {metadata.LoadedAt:HH:mm:ss})";

                message = $"Server:   {conn.Server}\nDatabase: {conn.Database ?? "(default)"}\n" +
                          $"Auth:     {(conn.IsSqlAuth ? "SQL Server" : "Windows")}\nUser:     {conn.UserName}\n\n" +
                          $"Metadata: {metadataText}";
            }

            Log.Info("Active connection: " + (conn?.ToString() ?? "none"));
            VsShellUtilities.ShowMessageBox(package, message, "SQL Helper",
                OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
