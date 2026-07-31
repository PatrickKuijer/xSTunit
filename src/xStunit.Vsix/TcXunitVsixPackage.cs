using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace xStunit.Vsix
{
    /// <summary>
    /// Hosts the "TcXunit Results" tool window: registers the ToolWindowPane
    /// and the menu command that shows it. Scaffolding mirrors
    /// C:\Git\TcAgentPlugin\src\TcAgent\ChatToolWindowPackage.cs.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("#110", "#112", "0.1.0", IconResourceID = 400)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(ResultsToolWindow))]
    [Guid(PackageGuids.PackageGuidString)]
    public sealed class TcXunitVsixPackage : AsyncPackage
    {
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            await ShowResultsToolWindowCommand.InitializeAsync(this);
        }
    }
}
