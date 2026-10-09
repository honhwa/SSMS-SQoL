using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SsmsSqlHelper.Diagnostics;
using SsmsSqlHelper.Updates;

namespace SsmsSqlHelper.Commands
{
    internal static class CheckForUpdatesCommand
    {
        private static readonly HttpClient Client = CreateClient();

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            if (!(await package.GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commandService))
            {
                Log.Error("IMenuCommandService unavailable; update command not registered");
                return;
            }

            var id = new CommandID(PackageIds.CommandSet, PackageIds.CheckForUpdatesCommandId);
            commandService.AddCommand(new MenuCommand((sender, args) =>
                package.JoinableTaskFactory.RunAsync(() => ExecuteAsync(package))
                    .FileAndForget("SsmsSqlHelper/CheckForUpdates"), id));
        }

        private static async Task ExecuteAsync(AsyncPackage package)
        {
            var assemblyVersion = typeof(SsmsSqlHelperPackage).Assembly.GetName().Version;
            var installed = new Version(assemblyVersion.Major, assemblyVersion.Minor, assemblyVersion.Build);
            var installedLabel = typeof(SsmsSqlHelperPackage).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? installed.ToString(3);
            Version published = null;
            string error = null;

            try
            {
                using (var response = await Client.GetAsync(ReleaseChecker.ApiUrl, package.DisposalToken).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    published = ReleaseChecker.LatestInstallerVersion(json);
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Error("Update check failed", ex);
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            string message;
            if (error != null)
                message = "Could not check GitHub Releases: " + error;
            else if (published == null)
                message = "No versioned installer was found in GitHub Releases.";
            else if (published.CompareTo(installed) > 0)
                message = $"An update is available.\n\nInstalled: {installedLabel}\nPublished: {published.ToString(3)}";
            else if (published.CompareTo(installed) < 0)
                message = $"No newer published installer was found.\n\nInstalled: {installedLabel}\nPublished: {published.ToString(3)}";
            else
                message = $"You have the latest version.\n\nInstalled: {installedLabel}\nPublished: {published.ToString(3)}";

            var answer = VsShellUtilities.ShowMessageBox(package,
                message + "\n\nOpen GitHub Releases?", "SQL Helper - Check for Updates",
                error == null ? OLEMSGICON.OLEMSGICON_INFO : OLEMSGICON.OLEMSGICON_WARNING,
                OLEMSGBUTTON.OLEMSGBUTTON_YESNO, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            if (answer != (int)System.Windows.Forms.DialogResult.Yes)
                return;

            try
            {
                Process.Start(new ProcessStartInfo(ReleaseChecker.ReleasesUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error("Could not open GitHub Releases", ex);
                VsShellUtilities.ShowMessageBox(package, "Could not open GitHub Releases: " + ex.Message,
                    "SQL Helper", OLEMSGICON.OLEMSGICON_WARNING,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SsmsSqlHelper/" +
                typeof(SsmsSqlHelperPackage).Assembly.GetName().Version.ToString(3));
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }
    }
}
