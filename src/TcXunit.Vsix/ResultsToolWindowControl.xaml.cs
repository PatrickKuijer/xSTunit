using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.Web.WebView2.Core;
using TcXunit.Vsix.TestRunner;

namespace TcXunit.Vsix
{
    /// <summary>
    /// The WebView2 page's #runButton (Resources/results.html/results.js) drives a run:
    /// a click posts 'run'/'stop' over window.chrome.webview.postMessage, handled here by
    /// OnWebMessageReceived, which calls StartRunAsync/StopRun. StartRunAsync shells
    /// out via TcxunitProcessRunner.RunAsync (TcXunit-1tt.3 -- async + cancellable, so the
    /// UI thread isn't blocked for a run's duration and Stop has something to cancel) and
    /// pushes the result into the page via window.tcxunitRenderResult(...); StopRun cancels
    /// the in-flight run, which kills the child tcxunit process. window.tcxunitSetRunning(bool)
    /// is pushed in around the run so the page's button/prog-sweep state always reflects
    /// whether a process is actually running, never an optimistic guess made on click.
    /// The former native WPF "Run tests" Button is retired -- see ResultsToolWindowControl.xaml's
    /// comment. StatusText remains for host-level errors the page itself can't show (WebView2
    /// failing to initialize, tcxunit.json missing/invalid).
    ///
    /// TcXunit-1tt.4 (click-to-navigate) reuses OnWebMessageReceived for a second message
    /// shape: a double-clicked suite/failed-test row posts a JSON object
    /// ({type:'openFile', filePath}) instead of a plain string, mirroring TcAgentPlugin's
    /// Browser_WebMessageReceived envelope pattern (ChatToolWindowControl.xaml.cs). It is
    /// distinguished from 'run'/'stop' by TryGetWebMessageAsString throwing for a non-string
    /// payload; the catch falls back to parsing WebMessageAsJson and opens the file via
    /// EnvDTE.DTE.ItemOperations.OpenFile, obtained the same way ResolveProjectDirectory below
    /// already does (Package.GetGlobalService(typeof(EnvDTE.DTE))).
    ///
    /// WebView2 hosting and VS-theme wiring mirror
    /// TcAgentPlugin/src/TcAgent/ChatToolWindowControl.xaml.cs exactly (EnsureCoreWebView2Async +
    /// CoreWebView2Environment, SetVirtualHostNameToFolderMapping onto Resources, a
    /// pre-navigation AddScriptToExecuteOnDocumentCreatedAsync theme push plus a live
    /// VSColorTheme.ThemeChanged -&gt; ExecuteScriptAsync push, and NewWindowRequested opening
    /// links in the user's real browser instead of a second WebView2 popup).
    /// </summary>
    public partial class ResultsToolWindowControl : UserControl
    {
        private const string VirtualHostName = "tcxunit.results";

        // WPF re-fires Loaded on tool-window redock/retab without disposing the control, but
        // EnsureCoreWebView2Async only tolerates being called once per environment -- a second
        // call with a fresh CoreWebView2Environment throws the "already initialized with a
        // different CoreWebView2Environment" error. Mirrors ChatToolWindowControl's guard.
        private bool _webViewInitialized;

        // Non-null exactly while a run is in flight; StartRunAsync creates it, StopRun (or a
        // second stray 'run' message while one is already running) cancels it, and
        // StartRunAsync's finally clears it back to null. Doubles as the "is a run currently
        // running" flag -- there's deliberately no separate bool to keep in sync with this.
        private CancellationTokenSource _runCts;

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

                // #runButton's click handler (results.js) posts the plain strings 'run'/'stop'
                // here -- see StartRunAsync/StopRun below.
                this.Browser.CoreWebView2.WebMessageReceived += this.OnWebMessageReceived;

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

            // A run left in flight when the tool window unloads (redock/retab churns
            // Loaded/Unloaded without necessarily tearing the control down -- see
            // _webViewInitialized's comment -- but closing VS or the tool window itself
            // can genuinely unload it mid-run) must not be left as an orphaned child
            // process; cancelling here reuses the exact same kill path Stop uses.
            this._runCts?.Cancel();

            if (this.Browser?.CoreWebView2 != null)
            {
                this.Browser.CoreWebView2.NewWindowRequested -= this.OnNewWindowRequested;
                this.Browser.CoreWebView2.WebMessageReceived -= this.OnWebMessageReceived;
            }
        }

        private async void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string message;
            try
            {
                message = e.TryGetWebMessageAsString();
            }
            catch (Exception)
            {
                // Not a plain string -- results.js also posts a JSON object for
                // click-to-navigate (TcXunit-1tt.4: {type:'openFile', filePath}). Anything
                // that isn't that shape either is genuinely foreign/unexpected and is
                // safely ignored, same as before this ticket.
                this.HandleJsonMessage(e.WebMessageAsJson);
                return;
            }

            if (string.Equals(message, "run", StringComparison.Ordinal))
            {
                await this.StartRunAsync();
            }
            else if (string.Equals(message, "stop", StringComparison.Ordinal))
            {
                this.StopRun();
            }
        }

        /// <summary>Handles the non-string postMessage shape (currently just
        /// click-to-navigate's {type:'openFile', filePath}). JavaScriptSerializer is already
        /// referenced by this project via System.Web.Extensions (see
        /// TcxunitProcessRunner.RunAsync) -- reused here rather than adding a JSON
        /// dependency for one small envelope. Any parse failure or unrecognized/missing
        /// "type"/"filePath" is ignored rather than surfaced as an error: a malformed or
        /// future/foreign message from the page is not a host-level failure worth alarming
        /// the user over.</summary>
        private void HandleJsonMessage(string json)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            Dictionary<string, object> envelope;
            try
            {
                envelope = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            }
            catch (Exception)
            {
                return;
            }

            if (envelope == null
                || !envelope.TryGetValue("type", out var typeObj)
                || !string.Equals(typeObj as string, "openFile", StringComparison.Ordinal))
            {
                return;
            }

            if (envelope.TryGetValue("filePath", out var filePathObj) && filePathObj is string filePath)
            {
                this.OpenFile(filePath);
            }
        }

        /// <summary>Opens filePath (a suite's, or -- for a failed leaf test row -- its
        /// parent suite's, per the epic's explicit "no per-test file granularity" design
        /// decision) in the XAE Shell editor via EnvDTE, the interaction
        /// TcXunit.Vsix/README.md flagged as deferred pending exactly this data. Uses the
        /// same Package.GetGlobalService(typeof(EnvDTE.DTE)) lookup ResolveProjectDirectory
        /// already relies on elsewhere in this file, rather than caching a DTE field the way
        /// ChatToolWindowControl does -- this path is click-driven and infrequent, so a
        /// fresh lookup per click is simpler than keeping a cached reference valid across
        /// the control's Loaded/Unloaded churn.</summary>
        private void OpenFile(string filePath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }

            try
            {
                if (Package.GetGlobalService(typeof(EnvDTE.DTE)) is EnvDTE.DTE dte)
                {
                    dte.ItemOperations.OpenFile(filePath);
                }
                else
                {
                    this.ShowError("Could not open file: no DTE available.");
                }
            }
            catch (Exception ex)
            {
                this.ShowError($"Failed to open {filePath}: {ex.Message}");
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

        /// <summary>Starts one run: pushes window.tcxunitSetRunning(true) in (which flips
        /// #runButton to its Stop state and shows the .prog sweep -- see PushSetRunning),
        /// awaits TcxunitProcessRunner.RunAsync, then pushes either the result into the
        /// tree or an error into StatusText, and finally pushes
        /// window.tcxunitSetRunning(false) back in. A stray second 'run' message while
        /// _runCts is already non-null (the button should already read Stop, so this is
        /// defensive, not an expected path) is a no-op -- one run at a time, per the
        /// acceptance criteria's "unambiguous about which action is live".</summary>
        private async Task StartRunAsync()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (this._runCts != null)
            {
                return;
            }

            this.StatusText.Text = string.Empty;
            this._runCts = new CancellationTokenSource();
            this.PushSetRunning(true);

            try
            {
                var directory = ResolveProjectDirectory();
                var config = TcxunitConfig.Load(directory);
                var runner = new TcxunitProcessRunner();
                var result = await runner.RunAsync(config, directory, this._runCts.Token).ConfigureAwait(true);

                if (!string.IsNullOrEmpty(result.Error))
                {
                    this.StatusText.Text = "Error: " + result.Error;
                }
                else if (this.Browser.CoreWebView2 != null && !string.IsNullOrEmpty(result.RawJson))
                {
                    _ = this.Browser.CoreWebView2.ExecuteScriptAsync(BuildRenderResultScript(result.RawJson));
                }
            }
            catch (OperationCanceledException)
            {
                // Stop was clicked -- not a run failure. Whatever the tree already showed
                // from a prior run is left exactly as it was (StartRunAsync never clears
                // #tree itself; only a completed run's window.tcxunitRenderResult call
                // does), per the acceptance criteria's "results already rendered ... stay
                // visible ... during a subsequent run".
                this.StatusText.Text = "Stopped.";
            }
            catch (Exception ex)
            {
                this.StatusText.Text = "Error: " + ex.Message;
            }
            finally
            {
                this._runCts = null;
                this.PushSetRunning(false);
            }
        }

        /// <summary>Cancels the in-flight run, if any -- TcxunitProcessRunner.RunAsync's
        /// CancellationToken.Register callback is what actually kills the child process
        /// tree (see KillProcessTree there); this just requests it. A 'stop' message with
        /// no run in flight (button already reads "Run tests") is a no-op.</summary>
        private void StopRun()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            this._runCts?.Cancel();
        }

        /// <summary>Pushes window.tcxunitSetRunning(running) into the page (see
        /// Resources/results.js) so #runButton's label/class and #prog's visibility are
        /// always a direct reflection of whether TcxunitProcessRunner.RunAsync actually
        /// has a process in flight, never an optimistic guess made purely from the
        /// click.</summary>
        private void PushSetRunning(bool running)
        {
            if (this.Browser.CoreWebView2 != null)
            {
                _ = this.Browser.CoreWebView2.ExecuteScriptAsync(
                    "(function(){if(window.tcxunitSetRunning){window.tcxunitSetRunning(" + (running ? "true" : "false") + ");}})();");
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
