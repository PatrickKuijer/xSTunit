using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace xStunit.Vsix
{
    // "#110" and "#112" are resource IDs into VSPackage.resx, resolved by VS at
    // runtime; renaming or removing those entries shows up as a blank product name in
    // the extension manager, not as a build error.
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("#110", "#112", "0.1.0", IconResourceID = 400)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(ResultsToolWindow))]
    [Guid(PackageGuids.PackageGuidString)]
    public sealed class xStunitVsixPackage : AsyncPackage
    {
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            await ShowResultsToolWindowCommand.InitializeAsync(this);
        }
    }
}
