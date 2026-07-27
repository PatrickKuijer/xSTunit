using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.Web.WebView2.Core;
using TcXunit.Vsix.TestRunner;

namespace TcXunit.Vsix
{
    /// <summary>
    /// "Run tests" (WPF Button) shells out via TcxunitProcessRunner and shows a pass/fail
    /// summary in StatusText, same as before. The former WPF TreeView is now a WebView2
    /// loading Resources/results.html, which pulls in Resources/vsix-tokens.css,
    /// vsix-shell.css, and results.css -- see docs/design-system.html section 8. Hosting
    /// and VS-theme wiring mirror TcAgentPlugin/src/TcAgent/ChatToolWindowControl.xaml.cs
    /// exactly (EnsureCoreWebView2Async + CoreWebView2Environment,
    /// SetVirtualHostNameToFolderMapping onto Resources, a pre-navigation
    /// AddScriptToExecuteOnDocumentCreatedAsync theme push plus a live
    /// VSColorTheme.ThemeChanged -&gt; ExecuteScriptAsync push, and NewWindowRequested
    /// opening links in the user's real browser instead of a second WebView2 popup).
    ///
    /// Rendering the run result into the WebView2's tree is not implemented yet -- see
    /// TcXunit-1tt.2 (static results tree render) and the rest of TcXunit-1tt's children.
    /// </summary>
    public partial class ResultsToolWindowControl : UserControl
    {
        private const string VirtualHostName = "tcxunit.results";

        // WPF re-fires Loaded on tool-window redock/retab without disposing the control, but
        // EnsureCoreWebView2Async only tolerates being called once per environment -- a second
        // call with a fresh CoreWebView2Environment throws the "already initialized with a
        // different CoreWebView2Environment" error. Mirrors ChatToolWindowControl's guard.
        private bool _webViewInitialized;

        public ResultsToolWindowControl()
        {
            this.InitializeComponent();

            this.Loaded += this.ResultsToolWindowControl_Loaded;
            this.Unloaded += this.ResultsToolWindowControl_Unloaded;
        }

        private async void ResultsToolWindowControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_webViewInitialized)
            {
                return;
            }

            _webViewInitialized = true;

            try
            {
                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TcXunit",
                    "WebView2");

                var environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolder);

                await this.Browser.EnsureCoreWebView2Async(environment);

                var resourcesFolder = Path.Combine(
                    Path.GetDirectoryName(typeof(ResultsToolWindowControl).Assembly.Location) ?? string.Empty,
                    "Resources");

                this.Browser.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    VirtualHostName,
                    resourcesFolder,
                    CoreWebView2HostResourceAccessKind.DenyCors);

                // Injected before any navigation's HTML parses, so the page never paints in the
                // wrong theme first. VSColorTheme.ThemeChanged (subscribed below) keeps it in
                // sync afterwards via a direct ExecuteScriptAsync push.
                await this.Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    BuildApplyThemeScript(GetCurrentThemeName()));

                VSColorTheme.ThemeChanged += this.OnVsThemeChanged;

                // results.html has no outbound links today, but this mirrors
                // ChatToolWindowControl's handling defensively: left unhandled, WebView2's
                // default action for a target="_blank"/window.open() is to open another
                // embedded WebView2 popup rather than the user's actual browser.
                this.Browser.CoreWebView2.NewWindowRequested += this.OnNewWindowRequested;

                this.Browser.CoreWebView2.Navigate($"https://{VirtualHostName}/results.html");
            }
            catch (Exception ex)
            {
                this.ShowError($"WebView2 failed to initialize: {ex.Message}");
            }
        }

        private void ResultsToolWindowControl_Unloaded(object sender, RoutedEventArgs e)
        {
            VSColorTheme.ThemeChanged -= this.OnVsThemeChanged;

            if (this.Browser?.CoreWebView2 != null)
            {
                this.Browser.CoreWebView2.NewWindowRequested -= this.OnNewWindowRequested;
            }
        }

        private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs args)
        {
            args.Handled = true;
            try
            {
                Process.Start(new ProcessStartInfo(args.Uri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                this.ShowError($"Failed to open browser: {ex.Message}");
            }
        }

        private void OnVsThemeChanged(ThemeChangedEventArgs e)
        {
            if (this.Browser.CoreWebView2 == null)
            {
                return;
            }

            _ = this.Browser.CoreWebView2.ExecuteScriptAsync(BuildApplyThemeScript(GetCurrentThemeName()));
        }

        /// <summary>Classifies the current VS environment theme as "light" or "dark" from the
        /// tool window background's perceived luminance -- same formula as
        /// ChatToolWindowControl.GetCurrentThemeName, so both panels agree on the same
        /// light/dark boundary.</summary>
        private static string GetCurrentThemeName()
        {
            var background = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey);
            double luminance = ((0.299 * background.R) + (0.587 * background.G) + (0.114 * background.B)) / 255.0;
            return luminance > 0.5 ? "light" : "dark";
        }

        /// <summary>Sets data-theme on &lt;body&gt; (vsix-tokens.css keys its light palette off
        /// body[data-theme="light"]; dark is the :root default). Deferred to DOMContentLoaded
        /// when document.body isn't parsed yet, so this is safe both as a pre-navigation
        /// injected script and as a live ExecuteScriptAsync push after the page has
        /// loaded.</summary>
        private static string BuildApplyThemeScript(string themeName)
        {
            return "(function(){function apply(){document.body.setAttribute('data-theme','" + themeName + "');}"
                + "if(document.body){apply();}else{document.addEventListener('DOMContentLoaded',apply);}})();";
        }

        private void RunButton_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            this.StatusText.Text = "Running...";

            try
            {
                var directory = ResolveProjectDirectory();
                var config = TcxunitConfig.Load(directory);
                var runner = new TcxunitProcessRunner();
                var result = runner.Run(config, directory);

                if (!string.IsNullOrEmpty(result.Error))
                {
                    this.StatusText.Text = "Error: " + result.Error;
                    return;
                }

                this.StatusText.Text = $"Passed: {result.Passed}  Failed: {result.Failed}  (exit code {result.ExitCode})";

                if (this.Browser.CoreWebView2 != null && !string.IsNullOrEmpty(result.RawJson))
                {
                    _ = this.Browser.CoreWebView2.ExecuteScriptAsync(BuildRenderResultScript(result.RawJson));
                }
            }
            catch (Exception ex)
            {
                this.StatusText.Text = "Error: " + ex.Message;
            }
        }

        /// <summary>Pushes one run's results into the page by calling
        /// window.tcxunitRenderResult(result) (see Resources/results.js) -- same
        /// ExecuteScriptAsync mechanism as BuildApplyThemeScript above, chosen over
        /// PostWebMessageAsJson because this is a single one-shot push after a
        /// completed run, not an ongoing bidirectional stream (contrast with
        /// TcAgentPlugin's chat panel, which does need that). rawJson is the CLI's
        /// own `tcxunit ... --format json` stdout text (TcxunitProcessRunner stashes
        /// it verbatim on TcxunitRunResult.RawJson) embedded directly as a JS object
        /// literal -- valid JSON is valid JS expression syntax, so no JSON.parse
        /// round-trip or escaping is needed here.</summary>
        private static string BuildRenderResultScript(string rawJson)
        {
            return "(function(){function apply(){if(window.tcxunitRenderResult){window.tcxunitRenderResult(" + rawJson + ");}}"
                + "if(document.readyState!=='loading'){apply();}else{document.addEventListener('DOMContentLoaded',apply);}})();";
        }

        private void ShowError(string message)
        {
            this.StatusText.Text = message;
        }

        // Resolves the open solution's directory via DTE (mirrors TcAgentPlugin's
        // ChatToolWindowControl_Loaded pattern: dte.Solution.FullName is the .sln path,
        // empty when no solution is open). Falls back to the process's current directory
        // so this still works if no solution/DTE is available (e.g. a quick manual test).
        private static string ResolveProjectDirectory()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (Package.GetGlobalService(typeof(EnvDTE.DTE)) is EnvDTE.DTE dte)
            {
                var solutionPath = dte.Solution?.FullName;
                if (!string.IsNullOrEmpty(solutionPath))
                {
                    return Path.GetDirectoryName(solutionPath);
                }
            }

            return Environment.CurrentDirectory;
        }
    }
}
