using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using SsmsSqlHelper.Commands;
using SsmsSqlHelper.Diagnostics;
using SsmsSqlHelper.Metadata;
using Task = System.Threading.Tasks.Task;

namespace SsmsSqlHelper
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageIds.PackageGuidString)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class SsmsSqlHelperPackage : AsyncPackage
    {
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            Log.Info($"Package loaded (v{typeof(SsmsSqlHelperPackage).Assembly.GetName().Version})");

            // Tell the user when the cache followed a schema change on its own
            MetadataService.Instance.SchemaReloaded += metadata =>
                JoinableTaskFactory.RunAsync(async () =>
                {
                    await JoinableTaskFactory.SwitchToMainThreadAsync();
                    StatusBar.Show($"SQL Helper: schema of {metadata.Database} changed - metadata reloaded ({metadata.Tables.Count} tables/views)");
                }).FileAndForget("SsmsSqlHelper/SchemaReloaded");

            await ShowConnectionCommand.InitializeAsync(this);
            await RefreshMetadataCommand.InitializeAsync(this);
            await EditSnippetsCommand.InitializeAsync(this);
            await SurroundWithSnippetCommand.InitializeAsync(this);
            await ExecuteGuard.InitializeAsync(this);
            await RecoverQueriesCommand.InitializeAsync(this);
        }
    }
}
