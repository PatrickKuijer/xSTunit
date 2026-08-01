# xStunit.Vsix

VSIX tool window for TwinCAT XAE Shell that shells out to the `xstunit` CLI,
parses its JSON output, and renders a pass/fail tree. It stands in for XAE
Shell's missing Test Explorer, and sidesteps XAE Shell stripping non-TwinCAT
csproj entries from the native `.sln` on every save.

It lives here as its own project rather than a separate repo or a fold-in to
TcAgent. The release process — versioning, how it reaches XAE Shell users — is
not decided yet.

## Project shape

Classic (non-SDK-style) `packages.config` VSSDK project targeting `net472`,
AnyCPU, VS2017 SDK 15.0, mirroring the scaffolding of
`TcAgentPlugin/src/TcAgent`, since XAE Shell is an Isolated Shell built on
VS2017. It therefore builds only on a Windows machine with VS2017 (or the XAE
Shell's own devenv) and the VS SDK installed: `dotnet build xStunit.sln` skips
it, so build it via `devenv.exe`/MSBuild with the VS SDK targets.

- `xStunitVsixPackage.cs` / `ResultsToolWindow.cs` — the `AsyncPackage` +
  `ToolWindowPane`.
- `Commands/ShowResultsToolWindowCommand.cs` + `VSCommandTable.vsct` — menu
  command ("xStunit Results") that shows the tool window.
- `TestRunner/XstunitConfig.cs` — reads `xstunit.json` (schema is a first
  guess — see `xstunit.json.sample`).
- `TestRunner/XstunitArgumentBuilder.cs`, `XstunitProcessRunner.cs`,
  `XstunitEventStream.cs`, `XstunitModels.cs` — shell out to `xstunit <path>
  --stream` via `Process.Start`, fold the NDJSON event lines back into one
  run result, and fall back to `--format json` for a CLI predating `--stream`.
  Deserialization is `System.Text.Json`, not `JavaScriptSerializer`/
  `System.Web.Extensions`, so these types can be unit-tested under net8.0 in
  `tests/xStunit.Vsix.Tests`. `RunAsync` is async and cancellable; cancelling
  kills the whole child process tree with `taskkill /T`, since a plain
  `Process.Kill()` on net472 only kills the immediate `cmd.exe` wrapper, not
  the `xstunit.exe` it launched.
- `ResultsToolWindowControl.xaml(.cs)` — a `Microsoft.Web.WebView2.Wpf.WebView2`
  loading `Resources/results.html`, plus a `StatusText` line for host-level
  errors the page cannot show (WebView2 failing to initialize, `xstunit.json`
  missing or invalid). The results tree renders as HTML/CSS inside the
  WebView2 rather than as a WPF `TreeView`, per `docs/design-system.html`, and
  the page's `#runButton` is the only "Run tests"/"Stop" action. A click posts
  `'run'`/`'stop'` over `window.chrome.webview.postMessage`
  (`CoreWebView2.WebMessageReceived`), which drives
  `XstunitProcessRunner.RunAsync` and pushes `window.xstunitSetRunning(bool)`
  back in, so the button and `.prog` sweep always reflect whether a process is
  actually running. Double-clicking a suite row or a failed test row (which
  inherits its parent suite's `filePath` — no per-test file granularity
  exists) posts `{type:'openFile', filePath}` the same way, and
  `OnWebMessageReceived` opens it via `EnvDTE.DTE.ItemOperations.OpenFile`.
  The page has no per-suite "currently executing" state: the host consumes the
  CLI's stream itself and renders once at the end of a run, so the page knows
  only that a run is or is not in flight.
- `Resources/results.html`, `Resources/vsix-tokens.css`,
  `Resources/vsix-shell.css`, `Resources/results.css` — the WebView2 page and
  its stylesheets. `vsix-tokens.css`/`vsix-shell.css` are the shared layer,
  copy-sourced from `TcAgentPlugin/src/TcAgent/Resources/chat.css` per
  `docs/design-system.html` section 8's file-split plan, and kept in sync with
  it by hand — there is no shared CSS package between the two repos.
  `results.css` is the results layer alone (`.toolbar`/`.tree`/`.node`/
  `.assert`/`.banner`/`.count`/`.prog`); `.empty-state` lives in
  `vsix-shell.css` since the design doc makes it a shared "Components — Core"
  element rather than a results-specific one.

## Building on Windows

1. Open `xStunit.sln` in VS2017 (or the XAE Shell's own devenv). Restore
   NuGet packages (`packages.config` — pulls `Microsoft.VSSDK.BuildTools`
   etc.).
2. Build. Expect first-build friction: missing VS SDK components
   (`Microsoft.VisualStudio.MPF.15.0`, VSSDK BuildTools), or
   `VSToolsPath`/`Microsoft.VsSDK.targets` failing to import if the VS SDK
   isn't registered the way `TcAgentPlugin` expects.
3. Deploy locally (`F5` / `/rootsuffix Exp`, or into XAE Shell directly) and
   verify: the "xStunit Results" tool window shows up (Other Windows menu),
   its WebView2 area loads (dark/light matching the XAE Shell theme — toggle
   Tools > Options > Environment > General to confirm it updates live), and
   shows the empty-state copy ("No results yet...") before any run. Clicking
   "Run tests" (needs a real `xstunit.json` — copy `xstunit.json.sample` and
   fill in `testProjectPath`, plus the `xstunit` CLI on PATH or `cliPath` set
   to a full path; optionally set `plugins` to a directory of
   `IXstunitNativeFunction` plugin assemblies — forwarded to the CLI as
   `--plugins <dir>`, resolved relative to `xstunit.json`'s own directory the
   same way `cliPath` is) swaps the button to "Stop", shows the `.prog` sweep
   above the tree, and on completion renders the pass/fail tree and swaps the
   button back to "Run tests". Clicking "Stop" mid-run should kill the
   `xstunit` process (verify via Task Manager) and return the button to "Run
   tests", leaving the panel in its empty state rather than a stale tree.
