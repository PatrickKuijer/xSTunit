using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Web.WebView2.Core;
using xStunit.Vsix.TestRunner;
using Task = System.Threading.Tasks.Task;

namespace xStunit.Vsix
{
    /// <summary>
    /// Host side of the WebView2 results panel (Resources/results.html + results.js).
    /// There is no native WPF UI left beyond StatusText.
    /// </summary>
    /// <remarks>
    /// The page drives everything through window.chrome.webview.postMessage, in two
    /// payload shapes that <see cref="OnWebMessageReceived"/> tells apart by
    /// TryGetWebMessageAsString throwing on anything that isn't a string: the plain
    /// strings "run"/"stop", and a JSON envelope ({type:'openFile', filePath} or
    /// {type:'rerunFailed'}).
    ///
    /// State flows back the other way as one-shot ExecuteScriptAsync pushes -
    /// xstunitBeginRun, xstunitSetRunning, xstunitSetStatus, xstunitRenderResult - so
    /// the page's button, progress sweep and status line always reflect whether a
    /// process is genuinely in flight rather than an optimistic guess made on click.
    /// StatusText is reserved for host-level failures the page cannot show at all
    /// (WebView2 failing to initialize, xstunit.json missing or invalid).
    ///
    /// WebView2 hosting and VS-theme wiring mirror
    /// TcAgentPlugin/src/TcAgent/ChatToolWindowControl.xaml.cs: EnsureCoreWebView2Async
    /// with an explicit CoreWebView2Environment, SetVirtualHostNameToFolderMapping onto
    /// Resources, a pre-navigation AddScriptToExecuteOnDocumentCreatedAsync theme push
    /// plus a live VSColorTheme.ThemeChanged push, and NewWindowRequested opening links
    /// in the user's real browser instead of a second embedded popup.
    /// </remarks>
    public partial class ResultsToolWindowControl : UserControl
    {
        private const string VirtualHostName = "xstunit.results";

        // results.js's postMessage envelopes use camelCase keys ("type", "filePath").
        private static readonly JsonSerializerOptions MessageEnvelopeSerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        // WPF re-fires Loaded on tool-window redock/retab without disposing the control,
        // but EnsureCoreWebView2Async tolerates being called only once per environment: a
        // second call with a fresh CoreWebView2Environment throws "already initialized
        // with a different CoreWebView2Environment".
        private bool _webViewInitialized;

        // Non-null exactly while a run is in flight, and deliberately doubling as the "a
        // run is in flight" flag - there is no separate bool that could fall out of sync
        // with it.
        private CancellationTokenSource _runCts;

        // Failed suite names from the most recently *completed* run. A stopped or errored
        // run leaves this untouched, having produced no result to refresh it from. Empty
        // covers both "no run yet" and "last run had no failures", which are the same
        // thing to rerun-failed: nothing to do.
        private List<string> _lastFailedSuiteNames = new List<string>();

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

        private const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;

        // WebView2Loader.dll ships as two arch-specific copies (x86\ and x64\, see the
        // csproj) because devenv.exe's bitness varies by host. Microsoft.Web.WebView2.Core's
        // DllImport names it with no path, so whichever copy the OS loader finds first
        // wins; preloading the right one by full path here means that implicit load
        // resolves to an already-loaded module instead of guessing. Getting it wrong
        // surfaces as a BadImageFormatException (0x8007000B) out of
        // CoreWebView2Environment.CreateAsync.
        private static void PreloadWebView2Loader()
        {
            var archFolder = Environment.Is64BitProcess ? "x64" : "x86";
            var dllPath = Path.Combine(
                Path.GetDirectoryName(typeof(ResultsToolWindowControl).Assembly.Location) ?? string.Empty,
                archFolder,
                "WebView2Loader.dll");

            LoadLibraryEx(dllPath, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH);
        }

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
                PreloadWebView2Loader();

                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "xStunit",
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

                // Injected before any navigation's HTML parses, so the page never paints
                // in the wrong theme first; ThemeChanged below keeps it in sync after.
                await this.Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    BuildApplyThemeScript(GetCurrentThemeName()));

                VSColorTheme.ThemeChanged += this.OnVsThemeChanged;

                // results.html has no outbound links today; handled anyway because
                // WebView2's default action for target="_blank"/window.open() is another
                // embedded popup rather than the user's actual browser.
                this.Browser.CoreWebView2.NewWindowRequested += this.OnNewWindowRequested;

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

            // A run still in flight when the window unloads for real (closing VS or the
            // tool window, as opposed to the redock/retab churn _webViewInitialized
            // guards against) must not be left as an orphaned child process; cancelling
            // reuses the exact kill path Stop uses.
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
                // Throwing is how a non-string payload announces itself; results.js's JSON
                // envelopes arrive this way.
                await this.HandleJsonMessageAsync(e.WebMessageAsJson);
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

        /// <summary>
        /// Handles the JSON-envelope messages: {type:'openFile', filePath} and
        /// {type:'rerunFailed'}. A parse failure or an unrecognized/missing "type" is
        /// ignored rather than surfaced - a malformed or foreign message from the page is
        /// not a host-level failure worth alarming the user over.
        /// </summary>
        private async Task HandleJsonMessageAsync(string json)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            MessageEnvelope envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<MessageEnvelope>(json, MessageEnvelopeSerializerOptions);
            }
            catch (JsonException)
            {
                return;
            }

            if (envelope == null || string.IsNullOrEmpty(envelope.Type))
            {
                return;
            }

            if (string.Equals(envelope.Type, "openFile", StringComparison.Ordinal))
            {
                if (!string.IsNullOrEmpty(envelope.FilePath))
                {
                    this.OpenFile(envelope.FilePath);
                }
            }
            else if (string.Equals(envelope.Type, "rerunFailed", StringComparison.Ordinal))
            {
                // No suite names travel in the envelope: this host, not the page, tracks
                // which suites failed. A stray click with nothing to rerun is a no-op
                // rather than falling through to a full unfiltered run, which would
                // surprise a user who asked for "just the failed ones".
                if (this._lastFailedSuiteNames.Count > 0)
                {
                    await this.StartRunAsync(this._lastFailedSuiteNames);
                }
            }
        }

        /// <summary>
        /// Opens a file in the XAE Shell editor. A failed leaf test row navigates to its
        /// parent suite's file - there is no per-test file granularity by design.
        /// </summary>
        /// <remarks>
        /// Goes through VsShellUtilities.OpenDocument (a wrapper over the native
        /// IVsUIShellOpenDocument service) rather than EnvDTE: EnvDTE._DTE.ItemOperations
        /// throws the same MissingMethodException on x64 TwinCAT XAE Shell that
        /// _DTE.Solution does (see <see cref="ResolveProjectDirectory"/>), so the DTE
        /// automation layer is unreliable there across the board.
        /// </remarks>
        private void OpenFile(string filePath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }

            try
            {
                VsShellUtilities.OpenDocument(ServiceProvider.GlobalProvider, filePath);
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

        /// <summary>
        /// Classifies the current VS theme as "light" or "dark" from the tool window
        /// background's perceived luminance. Same formula as
        /// ChatToolWindowControl.GetCurrentThemeName, so both panels put the light/dark
        /// boundary in the same place.
        /// </summary>
        private static string GetCurrentThemeName()
        {
            var background = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey);
            double luminance = ((0.299 * background.R) + (0.587 * background.G) + (0.114 * background.B)) / 255.0;
            return luminance > 0.5 ? "light" : "dark";
        }

        /// <summary>
        /// Sets data-theme on &lt;body&gt; (vsix-tokens.css keys its light palette off
        /// body[data-theme="light"]; dark is the :root default). Defers to
        /// DOMContentLoaded when document.body isn't parsed yet, so the same script is
        /// safe both as a pre-navigation injection and as a live push afterwards.
        /// </summary>
        private static string BuildApplyThemeScript(string themeName)
        {
            return "(function(){function apply(){document.body.setAttribute('data-theme','" + themeName + "');}"
                + "if(document.body){apply();}else{document.addEventListener('DOMContentLoaded',apply);}})();";
        }

        /// <summary>
        /// Runs one invocation end to end, pushing run state into the page around it.
        /// </summary>
        /// <remarks>
        /// One run at a time: a second "run" arriving while <see cref="_runCts"/> is
        /// non-null is refused, so the page can never be ambiguous about which action is
        /// live.
        ///
        /// <paramref name="suiteNames"/> is null for a normal run (every suite under
        /// config.Paths) and non-empty for rerun-failed. Either way the result REPLACES
        /// the tree via the same xstunitRenderResult push; a rerun-failed result is never
        /// merged into what is already displayed.
        /// </remarks>
        private async Task StartRunAsync(IReadOnlyList<string> suiteNames = null)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (this._runCts != null)
            {
                // Says so rather than no-opping silently: from the user's side an ignored
                // click is indistinguishable from a dead button.
                this.SetStatusText("A run is already in progress.");
                return;
            }

            this.SetStatusText(string.Empty);
            this._runCts = new CancellationTokenSource();
            // Ordered before the CLI process starts, so a prior run's rows are never left
            // on screen for the duration of a new run.
            this.PushBeginRun();
            this.PushSetRunning(true);
            this.PushStatus("running", "Running");

            try
            {
                var directory = ResolveProjectDirectory();
                var config = XstunitConfig.Load(directory);
                var runner = new XstunitProcessRunner();
                var result = await runner.RunAsync(config, directory, this._runCts.Token, suiteNames).ConfigureAwait(true);

                if (!string.IsNullOrEmpty(result.Error))
                {
                    this.ShowError("Error: " + result.Error);
                    this.PushStatus("error", "Error: " + result.Error);
                    // The early-exit error shape carries no Suites, so there is nothing to
                    // recompute _lastFailedSuiteNames from; it keeps its previous value.
                    // Inert either way, since PushBeginRun already disabled the page's
                    // rerun button until a real result renders again.
                }
                else
                {
                    this._lastFailedSuiteNames = ComputeFailedSuiteNames(result);

                    if (this.Browser.CoreWebView2 != null && !string.IsNullOrEmpty(result.RawJson))
                    {
                        _ = this.Browser.CoreWebView2.ExecuteScriptAsync(BuildRenderResultScript(result.RawJson));
                    }

                    this.PushStatus("ready", "Ready");
                }
            }
            catch (OperationCanceledException)
            {
                // Stop was clicked; not a run failure. The tree stays cleared - a stale
                // tree is indistinguishable from a fresh one - and the finally block's
                // xstunitSetRunning(false) reverts the page to its empty state, since no
                // render happened on this path.
                this.SetStatusText("Stopped.");
                this.PushStatus("stopped", "Stopped.");
            }
            catch (Exception ex)
            {
                // Deliberately also covers ResolveProjectDirectory and XstunitConfig.Load,
                // which run inside this try: a missing or misplaced xstunit.json is
                // reported the same way a runner failure is.
                this.ShowError("Error: " + ex.Message);
                this.PushStatus("error", "Error: " + ex.Message);
            }
            finally
            {
                this._runCts = null;
                this.PushSetRunning(false);
            }
        }

        /// <summary>
        /// The suites rerun-failed will re-run next time. "Failed" must mean exactly what
        /// results.js's renderSuite paints as failed, or the button's label stops matching
        /// what the tree shows: a suite that never loaded (Error non-empty) or one with at
        /// least one test where Passed is false.
        /// </summary>
        private static List<string> ComputeFailedSuiteNames(XstunitRunResult result)
        {
            var suites = result?.Suites;
            if (suites == null)
            {
                return new List<string>();
            }

            return suites
                .Where(suite => !string.IsNullOrEmpty(suite.Error)
                    || (suite.Tests != null && suite.Tests.Any(test => !test.Passed)))
                .Select(suite => suite.Name)
                .Where(name => !string.IsNullOrEmpty(name))
                .ToList();
        }

        /// <summary>
        /// Only requests cancellation - the child process tree is actually killed by
        /// XstunitProcessRunner's CancellationToken.Register callback. A "stop" with no
        /// run in flight is a no-op.
        /// </summary>
        private void StopRun()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            this._runCts?.Cancel();
        }

        /// <summary>
        /// Fire-and-forget ExecuteScriptAsync behind the "CoreWebView2 might not be ready
        /// yet" null check every push into the page needs, so the Push* methods below
        /// only build their call string.
        /// </summary>
        private void PushScript(string script)
        {
            if (this.Browser.CoreWebView2 != null)
            {
                _ = this.Browser.CoreWebView2.ExecuteScriptAsync(script);
            }
        }

        private void PushSetRunning(bool running)
        {
            this.PushScript(
                "(function(){if(window.xstunitSetRunning){window.xstunitSetRunning(" + (running ? "true" : "false") + ");}})();");
        }

        /// <summary>
        /// Clears the page's previously-rendered rows and counts and swaps in its running
        /// state, so the panel can never be mistaken for showing complete results while a
        /// run is still in flight.
        /// </summary>
        private void PushBeginRun()
        {
            this.PushScript("(function(){if(window.xstunitBeginRun){window.xstunitBeginRun();}})();");
        }

        /// <summary>
        /// Drives the page's own status line, as opposed to
        /// <see cref="ShowError"/>/<see cref="SetStatusText"/>, which drive the native WPF
        /// fallback. Both arguments are JSON-serialized rather than concatenated (contrast
        /// <see cref="PushSetRunning"/>'s bool) because text is free-form - an exception
        /// message, or the CLI's own error output - and would otherwise be able to break
        /// out of the script's string literal.
        /// </summary>
        private void PushStatus(string state, string text)
        {
            var stateLiteral = JsonSerializer.Serialize(state ?? string.Empty);
            var textLiteral = JsonSerializer.Serialize(text ?? string.Empty);
            this.PushScript(
                "(function(){if(window.xstunitSetStatus){window.xstunitSetStatus(" + stateLiteral + "," + textLiteral + ");}})();");
        }

        /// <summary>
        /// Pushes one run's results into the page. ExecuteScriptAsync rather than
        /// PostWebMessageAsJson because this is a one-shot push after a completed run,
        /// not an ongoing bidirectional stream.
        /// </summary>
        /// <remarks>
        /// rawJson is the CLI's own stdout, embedded directly as a JS object literal:
        /// valid JSON is valid JS expression syntax, so no JSON.parse round-trip or
        /// escaping is needed.
        /// </remarks>
        private static string BuildRenderResultScript(string rawJson)
        {
            return "(function(){function apply(){if(window.xstunitRenderResult){window.xstunitRenderResult(" + rawJson + ");}}"
                + "if(document.readyState!=='loading'){apply();}else{document.addEventListener('DOMContentLoaded',apply);}})();";
        }

        /// <summary>
        /// The one seam every host-level failure is reported through, as opposed to
        /// <see cref="SetStatusText"/>'s benign uses (clearing the line, "Stopped."). It
        /// forwards for now; the point is that changing how errors surface stays a
        /// one-place change.
        /// </summary>
        private void ShowError(string message)
        {
            this.SetStatusText(message);
        }

        /// <summary>
        /// Collapses the status row when there is nothing to show: Row 0 is Auto-height,
        /// but an empty TextBlock with a Margin still reserves a line of dead space above
        /// the WebView2.
        /// </summary>
        private void SetStatusText(string text)
        {
            this.StatusText.Text = text ?? string.Empty;
            this.StatusText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        // IVsSolution, not EnvDTE: _DTE.Solution throws MissingMethodException on x64
        // TwinCAT XAE Shell (interop type mismatch on that isolated shell), and catching it
        // leaves only Environment.CurrentDirectory, which on XAE is C:\Windows\System32 -
        // useless. IVsSolution.GetSolutionInfo is the native service EnvDTE.Solution wraps,
        // so it works even where the DTE automation layer is trimmed or broken. The
        // CurrentDirectory fallback is for having no solution open at all.
        private static string ResolveProjectDirectory()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (Package.GetGlobalService(typeof(SVsSolution)) is IVsSolution solution)
                {
                    solution.GetSolutionInfo(out string solutionDirectory, out _, out _);
                    if (!string.IsNullOrEmpty(solutionDirectory))
                    {
                        return solutionDirectory;
                    }
                }
            }
            catch (Exception)
            {
                // Fall through to Environment.CurrentDirectory below.
            }

            return Environment.CurrentDirectory;
        }

        // Shape of results.js's postMessage envelopes; FilePath is simply absent for
        // {type:'rerunFailed'}. A typed class rather than a Dictionary<string, object>
        // because System.Text.Json fills the latter with boxed JsonElement values, not
        // plain strings.
        private sealed class MessageEnvelope
        {
            public string Type { get; set; }

            public string FilePath { get; set; }
        }
    }
}
