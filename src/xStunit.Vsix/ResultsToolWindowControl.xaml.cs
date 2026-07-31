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
    /// The WebView2 page's #runButton (Resources/results.html/results.js) drives a run:
    /// a click posts 'run'/'stop' over window.chrome.webview.postMessage, handled here by
    /// OnWebMessageReceived, which calls StartRunAsync/StopRun. StartRunAsync shells
    /// out via XstunitProcessRunner.RunAsync (TcXunit-1tt.3 -- async + cancellable, so the
    /// UI thread isn't blocked for a run's duration and Stop has something to cancel) and
    /// pushes the result into the page via window.xstunitRenderResult(...); StopRun cancels
    /// the in-flight run, which kills the child xstunit process. window.xstunitSetRunning(bool)
    /// is pushed in around the run so the page's button/prog-sweep state always reflects
    /// whether a process is actually running, never an optimistic guess made on click.
    /// The former native WPF "Run tests" Button is retired -- see ResultsToolWindowControl.xaml's
    /// comment. StatusText remains for host-level errors the page itself can't show (WebView2
    /// failing to initialize, xstunit.json missing/invalid).
    ///
    /// TcXunit-1tt.4 (click-to-navigate) reuses OnWebMessageReceived for a second message
    /// shape: a double-clicked suite/failed-test row posts a JSON object
    /// ({type:'openFile', filePath}) instead of a plain string, mirroring TcAgentPlugin's
    /// Browser_WebMessageReceived envelope pattern (ChatToolWindowControl.xaml.cs). It is
    /// distinguished from 'run'/'stop' by TryGetWebMessageAsString throwing for a non-string
    /// payload; the catch falls back to parsing WebMessageAsJson and opens the file via
    /// VsShellUtilities.OpenDocument (see OpenFile below).
    ///
    /// TcXunit-1tt.8 (rerun failed) adds a third message under the same JSON envelope:
    /// {type:'rerunFailed'}, posted by results.js's #rerunFailedButton. Unlike openFile it
    /// carries no payload -- this host, not the page, is the one tracking which suites
    /// failed (_lastFailedSuiteNames, refreshed from every completed run's deserialized
    /// XstunitRunResult in StartRunAsync), so the page only has to ask. Handling it re-runs
    /// StartRunAsync with that suite list, which threads through to
    /// XstunitProcessRunner.RunAsync's suiteNames parameter and becomes a repeated
    /// --suite &lt;name&gt; (TcXunit-6fb.3) on the CLI invocation -- restricting the run to
    /// just those suites and replacing (not merging into) the displayed tree via the exact
    /// same window.xstunitRenderResult(...) path a normal run already uses.
    ///
    /// TcXunit-qjt (reset stale results, live run-state feedback) adds two more pushes
    /// around every StartRunAsync run: PushBeginRun (window.xstunitBeginRun(), right
    /// before the CLI process starts) clears #tree/counts and shows a running
    /// placeholder instead of leaving a prior run's rows on screen for the new run's
    /// duration, and PushStatus (window.xstunitSetStatus(state, text), alongside every
    /// status-line branch below) drives the page's own .tw-statusbar dot/#statusText --
    /// previously hardcoded to "Ready" and never updated. This supersedes TcXunit-1tt.3's
    /// original "results already rendered from a prior run stay visible/updating during
    /// a subsequent run" decision, per explicit user feedback.
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
        private const string VirtualHostName = "xstunit.results";

        // results.js's postMessage envelopes use camelCase keys ("type", "filePath").
        private static readonly JsonSerializerOptions MessageEnvelopeSerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

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

        // TcXunit-1tt.8: the failed suite names from the most recently *completed* run,
        // refreshed at the end of every successful StartRunAsync (including a rerun-failed
        // one) and left untouched by a stopped/errored run -- StatusText already reports
        // those, and there is nothing to update this list from since no new
        // XstunitRunResult exists in that case. A suite counts as failed if it never loaded
        // (Error set) or has at least one failing test, matching results.js's own
        // suite-status logic (renderSuite's hasError/anyFail). Starts empty: "no run yet"
        // and "last run had zero failures" both correctly leave rerunFailed with nothing to
        // do.
        private List<string> _lastFailedSuiteNames = new List<string>();

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

        private const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;

        // WebView2Loader.dll ships as two arch-specific copies (x86\ and x64\, see the csproj
        // comment) because devenv.exe's bitness varies by host -- VS2017/older XAE Shell is
        // 32-bit, VS2022/XAE x64 is 64-bit. Microsoft.Web.WebView2.Core's DllImport just names
        // "WebView2Loader.dll" with no path, so whichever copy the OS loader finds first via
        // the default search order wins; explicitly preloading the correct one here by full
        // path means that implicit load resolves to the already-loaded module instead of
        // guessing. Loading the wrong arch fails as a BadImageFormatException
        // (HRESULT 0x8007000B) surfaced from CoreWebView2Environment.CreateAsync.
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
                // click-to-navigate (TcXunit-1tt.4: {type:'openFile', filePath}) and
                // rerun-failed (TcXunit-1tt.8: {type:'rerunFailed'}). Anything that isn't
                // one of those shapes either is genuinely foreign/unexpected and is safely
                // ignored, same as before this ticket.
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

        /// <summary>Handles the non-string postMessage shapes: click-to-navigate's
        /// {type:'openFile', filePath} and rerun-failed's {type:'rerunFailed'}. Any parse
        /// failure or unrecognized/missing "type" is ignored rather than surfaced as an
        /// error: a malformed or future/foreign message from the page is not a host-level
        /// failure worth alarming the user over. Async (rather than the old sync
        /// HandleJsonMessage) because rerunFailed has to await StartRunAsync -- openFile
        /// stays synchronous internally, just called from this now-async method.</summary>
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
                // No suite names travel in the envelope -- this host already knows them
                // from the last completed run (_lastFailedSuiteNames). A click that somehow
                // arrives with nothing to rerun (button should be disabled in that case,
                // but a stray/late message is not impossible) is a no-op rather than
                // falling through to a full unfiltered run, which would surprise a user who
                // asked for "just the failed ones".
                if (this._lastFailedSuiteNames.Count > 0)
                {
                    await this.StartRunAsync(this._lastFailedSuiteNames);
                }
            }
        }

        /// <summary>Opens filePath (a suite's, or -- for a failed leaf test row -- its
        /// parent suite's, per the epic's explicit "no per-test file granularity" design
        /// decision) in the XAE Shell editor, the interaction xStunit.Vsix/README.md flagged
        /// as deferred pending exactly this data. Uses VsShellUtilities.OpenDocument (a thin
        /// wrapper over the native IVsUIShellOpenDocument shell service), not EnvDTE --
        /// EnvDTE._DTE.ItemOperations threw the same MissingMethodException on x64 TwinCAT
        /// XAE Shell that _DTE.Solution did (see ResolveProjectDirectory), so EnvDTE
        /// automation is unreliable there across the board.</summary>
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

        /// <summary>Starts one run: pushes window.xstunitSetRunning(true) in (which flips
        /// #runButton to its Stop state and shows the .prog sweep -- see PushSetRunning),
        /// awaits XstunitProcessRunner.RunAsync, then pushes either the result into the
        /// tree or an error into StatusText, and finally pushes
        /// window.xstunitSetRunning(false) back in. A stray second 'run' message while
        /// _runCts is already non-null (the button should already read Stop, so this is
        /// defensive, not an expected path) is a no-op -- one run at a time, per the
        /// acceptance criteria's "unambiguous about which action is live".
        ///
        /// suiteNames is null for a normal #runButton-driven run (every suite under
        /// config.Paths) and non-null/non-empty for TcXunit-1tt.8's rerun-failed
        /// (_lastFailedSuiteNames from the prior completed run) -- threaded straight through
        /// to XstunitProcessRunner.RunAsync, which turns it into a repeated --suite &lt;name&gt;.
        /// Either way the result REPLACES #tree via the same window.xstunitRenderResult(...)
        /// call below; a rerun-failed result is not merged into the existing tree.</summary>
        private async Task StartRunAsync(IReadOnlyList<string> suiteNames = null)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (this._runCts != null)
            {
                // A run is already in flight -- the button should already read Stop, so this
                // is defensive rather than an expected path (see class-level remarks above).
                // Still worth a status line rather than a bare no-op: a silently-ignored click
                // looks identical to a dead button from the user's side.
                this.SetStatusText("A run is already in progress.");
                return;
            }

            this.SetStatusText(string.Empty);
            this._runCts = new CancellationTokenSource();
            // TcXunit-qjt: clears #tree/counts and swaps in the running placeholder
            // before anything else -- including PushSetRunning(true), so the "unmistakably
            // mid-run" cues all land together -- so the CLI process (below) never starts
            // while a prior run's rows are still on screen.
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
                    // No new XstunitRunResult worth trusting (early-exit error shape has no
                    // Suites) -- leave _lastFailedSuiteNames exactly as it was rather than
                    // clearing it. This is now inert either way: PushBeginRun already reset
                    // the page's own hasFailures to false for this run, so #rerunFailedButton
                    // stays disabled until a real result renders again regardless of what
                    // this list holds.
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
                // Stop was clicked -- not a run failure. #tree was already cleared by
                // PushBeginRun above and stays that way: TcXunit-qjt supersedes
                // TcXunit-1tt.3's original "results already rendered ... stay visible ...
                // during a subsequent run" decision, per explicit user feedback (a stale
                // tree was indistinguishable from a fresh one). xstunitSetRunning(false),
                // pushed from the finally block below, reverts the page from
                // #runningState back to #emptyState since no new render happened here.
                this.SetStatusText("Stopped.");
                this.PushStatus("stopped", "Stopped.");
            }
            catch (Exception ex)
            {
                // Covers XstunitConfig.Load/ResolveProjectDirectory failures too (e.g. a
                // missing/misplaced xstunit.json) -- both happen inside this try block, above,
                // so their exceptions land here same as a runner failure. Routed through
                // ShowError, not a direct SetStatusText call, for consistency with every other
                // host-level error path in this file (see ShowError's remarks below).
                this.ShowError("Error: " + ex.Message);
                this.PushStatus("error", "Error: " + ex.Message);
            }
            finally
            {
                this._runCts = null;
                this.PushSetRunning(false);
            }
        }

        /// <summary>Computes the failed suite names from a completed run's result, for
        /// TcXunit-1tt.8's rerun-failed to hand back to XstunitProcessRunner.RunAsync as
        /// its suiteNames filter next time. Mirrors results.js's own per-suite status logic
        /// exactly (renderSuite there): a suite counts as failed if it never loaded
        /// (Error non-empty, "no tests were run") or has at least one test with
        /// Passed == false. result.Suites is null for the early-exit error shape, but
        /// callers only reach here once result.Error is confirmed empty, so that case does
        /// not need special-casing beyond the null-conditional below.</summary>
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

        /// <summary>Cancels the in-flight run, if any -- XstunitProcessRunner.RunAsync's
        /// CancellationToken.Register callback is what actually kills the child process
        /// tree (see KillProcessTree there); this just requests it. A 'stop' message with
        /// no run in flight (button already reads "Run tests") is a no-op.</summary>
        private void StopRun()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            this._runCts?.Cancel();
        }

        /// <summary>Fire-and-forget ExecuteScriptAsync, guarded by the same
        /// "CoreWebView2 might not be ready yet" null-check every push into the page
        /// needs (mirrors ChatToolWindowControl's own pattern). Shared by
        /// PushSetRunning/PushBeginRun/PushStatus below so each only has to build its
        /// own `window.xstunitXxx(...)` call string, not repeat the guard.</summary>
        private void PushScript(string script)
        {
            if (this.Browser.CoreWebView2 != null)
            {
                _ = this.Browser.CoreWebView2.ExecuteScriptAsync(script);
            }
        }

        /// <summary>Pushes window.xstunitSetRunning(running) into the page (see
        /// Resources/results.js) so #runButton's label/class and #prog's visibility are
        /// always a direct reflection of whether XstunitProcessRunner.RunAsync actually
        /// has a process in flight, never an optimistic guess made purely from the
        /// click.</summary>
        private void PushSetRunning(bool running)
        {
            this.PushScript(
                "(function(){if(window.xstunitSetRunning){window.xstunitSetRunning(" + (running ? "true" : "false") + ");}})();");
        }

        /// <summary>TcXunit-qjt: pushes window.xstunitBeginRun() into the page (see
        /// Resources/results.js), called from StartRunAsync right before the CLI process
        /// starts. Clears #tree's previously-rendered rows/counts and swaps in
        /// #runningState so the panel can never be mistaken for showing fresh/complete
        /// results while this run is in flight -- the fix for this ticket's reported gap
        /// (a prior run's rows sat untouched for a new run's full duration, per
        /// TcXunit-1tt.3's original, now-superseded, design).</summary>
        private void PushBeginRun()
        {
            this.PushScript("(function(){if(window.xstunitBeginRun){window.xstunitBeginRun();}})();");
        }

        /// <summary>TcXunit-qjt: pushes window.xstunitSetStatus(state, text) into the page
        /// (see Resources/results.js), so the WebView2 page's own .tw-statusbar dot/
        /// #statusText -- previously hardcoded to "Ready" and never updated by any
        /// code -- reflects Running while a run is in flight and Ready/Stopped/Error once
        /// it ends, mirroring StartRunAsync's own status-line branches immediately below
        /// each call site. Distinct from ShowError/SetStatusText, which drive the native
        /// WPF StatusText fallback for host-level errors the page itself can't show (see
        /// that method's remarks) -- this always targets the page's own status line.
        /// text is JSON-serialized rather than concatenated directly (contrast
        /// PushSetRunning's plain bool-to-string above) because it carries free text --
        /// an exception message or the CLI's own error output -- that may itself contain
        /// quotes/backslashes and would otherwise break out of the script's string
        /// literal.</summary>
        private void PushStatus(string state, string text)
        {
            var stateLiteral = JsonSerializer.Serialize(state ?? string.Empty);
            var textLiteral = JsonSerializer.Serialize(text ?? string.Empty);
            this.PushScript(
                "(function(){if(window.xstunitSetStatus){window.xstunitSetStatus(" + stateLiteral + "," + textLiteral + ");}})();");
        }

        /// <summary>Pushes one run's results into the page by calling
        /// window.xstunitRenderResult(result) (see Resources/results.js) -- same
        /// ExecuteScriptAsync mechanism as BuildApplyThemeScript above, chosen over
        /// PostWebMessageAsJson because this is a single one-shot push after a
        /// completed run, not an ongoing bidirectional stream (contrast with
        /// TcAgentPlugin's chat panel, which does need that). rawJson is the CLI's
        /// own `xstunit ... --format json` stdout text (XstunitProcessRunner stashes
        /// it verbatim on XstunitRunResult.RawJson) embedded directly as a JS object
        /// literal -- valid JSON is valid JS expression syntax, so no JSON.parse
        /// round-trip or escaping is needed here.</summary>
        private static string BuildRenderResultScript(string rawJson)
        {
            return "(function(){function apply(){if(window.xstunitRenderResult){window.xstunitRenderResult(" + rawJson + ");}}"
                + "if(document.readyState!=='loading'){apply();}else{document.addEventListener('DOMContentLoaded',apply);}})();";
        }

        /// <summary>Canonical entry point for reporting a host-level failure (as opposed to
        /// SetStatusText's non-error uses: clearing the line at the start of a run, or the
        /// benign "Stopped." message). Currently just forwards to SetStatusText, but keeping
        /// error reporting behind this one seam -- rather than every call site formatting and
        /// calling SetStatusText directly -- means a future change to how errors are surfaced
        /// (e.g. a distinct visual style, logging, telemetry) only has one place to change.</summary>
        private void ShowError(string message)
        {
            this.SetStatusText(message);
        }

        /// <summary>Sets StatusText's content and collapses its row when there is nothing to
        /// show, so an empty status line doesn't leave dead space above the WebView2 (Row 0
        /// is Auto-height, but an empty TextBlock with Margin still reserves a line).</summary>
        private void SetStatusText(string text)
        {
            this.StatusText.Text = text ?? string.Empty;
            this.StatusText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        // Resolves the open solution's directory via IVsSolution -- a core shell service,
        // not the EnvDTE automation model. EnvDTE._DTE.Solution threw MissingMethodException
        // on x64 TwinCAT XAE Shell (interop type mismatch on that isolated shell), and even
        // caught, the only fallback left was Environment.CurrentDirectory, which on XAE is
        // C:\Windows\System32 -- useless. IVsSolution.GetSolutionInfo is the native shell
        // service EnvDTE.Solution wraps, so it works even where the DTE automation layer is
        // trimmed/broken. Falls back to Environment.CurrentDirectory only if no solution is
        // open at all (e.g. a quick manual test) or the service is unavailable.
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

        // Shape of results.js's postMessage JSON envelopes -- {type:'openFile', filePath}
        // or {type:'rerunFailed'} (filePath simply absent/null for the latter). A typed
        // class rather than Dictionary&lt;string, object&gt; because System.Text.Json hands
        // back boxed JsonElement values for the latter, not plain strings -- this avoids
        // the JsonElement-vs-string mismatch entirely.
        private sealed class MessageEnvelope
        {
            public string Type { get; set; }

            public string FilePath { get; set; }
        }
    }
}
