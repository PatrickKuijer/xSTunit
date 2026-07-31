# xStunit.Vsix

VSIX tool window for TwinCAT XAE Shell that shells out to `xstunit run <path>
--format json`, parses the JSON, and renders a pass/fail tree. Replaces XAE
Shell's missing Test Explorer, sidestepping XAE Shell stripping non-TwinCAT
csproj entries from the native `.sln` on every save (see TcXunit-6nt).

Graduated from spike to maintained project per TcXunit-iyd.1. Lives here as
its own project, not a separate repo, not folded into TcAgent. Release
process (versioning, how it ships to XAE Shell users) is not decided yet.

## Project shape

Classic (non-SDK-style) `packages.config` VSSDK project targeting `net472`,
AnyCPU, VS2017 SDK 15.0 — mirrors the working scaffolding pattern from
`TcAgentPlugin/src/TcAgent`, since XAE Shell is an Isolated Shell built on
VS2017. This means it builds only on a Windows machine with VS2017 (or the
XAE Shell's own devenv) and the VS SDK installed — `dotnet build
xStunit.sln` from the CLI will skip or fail on this project; build it via
`devenv.exe`/MSBuild with the VS SDK targets, same as `TcAgentPlugin`.

- `XStunitVsixPackage.cs` / `ResultsToolWindow.cs` — the `AsyncPackage` +
  `ToolWindowPane`.
- `Commands/ShowResultsToolWindowCommand.cs` + `VSCommandTable.vsct` — menu
  command ("xStunit Results") that shows the tool window.
- `TestRunner/XstunitConfig.cs` — reads `xstunit.json` (schema is a first
  guess — see `xstunit.json.sample`).
- `TestRunner/XstunitProcessRunner.cs` + `XstunitModels.cs` — shells out to
  `xstunit run <path> --format json` via `Process.Start` and deserializes the
  JSON with `System.Text.Json` (swapped off `JavaScriptSerializer`/
  `System.Web.Extensions` per TcXunit-cmp, so `XstunitConfig.cs` and
  `XstunitModels.cs` can be unit-tested under net8.0 in
  `tests/xStunit.Vsix.Tests`). `RunAsync` (added by TcXunit-1tt.3) is
  async/cancellable — cancelling kills the whole child process tree
  (`taskkill /T`, since a plain `Process.Kill()` on net472 only kills the
  immediate `cmd.exe` wrapper, not the `xstunit.exe` it launched).
- `ResultsToolWindowControl.xaml(.cs)` — a `Microsoft.Web.WebView2.Wpf.WebView2`
  that loads `Resources/results.html`, plus a `StatusText` line for host-level
  errors the page itself can't show (WebView2 failing to initialize,
  `xstunit.json` missing/invalid). Per TcXunit-1tt (rebuild as WebView2 per
  `docs/design-system.html`), the results tree renders as HTML/CSS inside the
  WebView2 rather than a WPF `TreeView` (TcXunit-1tt.2). As of TcXunit-1tt.3
  the toolbar's `#runButton` inside the WebView2 page is the only "Run
  tests"/"Stop" action — the native WPF button TcXunit-1tt.1/.2 left in place
  is retired. A click posts `'run'`/`'stop'` to
  `ResultsToolWindowControl.xaml.cs` over `window.chrome.webview.postMessage`
  (`CoreWebView2.WebMessageReceived`), which drives `XstunitProcessRunner.RunAsync`
  and pushes `window.xstunitSetRunning(bool)` back in so the button/`.prog`
  sweep always reflect whether a process is actually running. As of
  TcXunit-1tt.4, double-clicking a suite row or a failed test row (which
  inherits its parent suite's `filePath` — no per-test file granularity
  exists) posts `{type:'openFile', filePath}` the same way, and
  `OnWebMessageReceived` opens it via `EnvDTE.DTE.ItemOperations.OpenFile`. No
  per-test/per-suite "currently executing" granularity either — the CLI emits
  one JSON blob at the end of a run, not an incremental stream, so there's no
  data to show which suite is running, only that a run is or isn't in flight.
- `Resources/results.html`, `Resources/vsix-tokens.css`,
  `Resources/vsix-shell.css`, `Resources/results.css` — the WebView2 page and
  its shared/TcXunit-only stylesheets. `vsix-tokens.css`/`vsix-shell.css` are
  copy-sourced from `TcAgentPlugin/src/TcAgent/Resources/chat.css` per
  `docs/design-system.html` section 8's file-split plan and should be kept in
  sync with it by hand (no shared CSS package between the two repos yet).
  `results.css` is TcXunit-only (`.toolbar`/`.tree`/`.node`/`.assert`/`.banner`/
  `.count`/`.prog`); `.empty-state` lives in `vsix-shell.css` since it's a
  shared "Components — Core" element per the design doc, not results-specific.

## Building on Windows

1. Open `xStunit.sln` in VS2017 (or the XAE Shell's own devenv). Restore
   NuGet packages (`packages.config` — pulls `Microsoft.VSSDK.BuildTools`
   etc.).
2. Build. Not yet compiled since migration — expect first-build friction:
   missing VS SDK components (`Microsoft.VisualStudio.MPF.15.0`, VSSDK
   BuildTools), or `VSToolsPath`/`Microsoft.VsSDK.targets` import failing if
   the VS SDK isn't registered the way `TcAgentPlugin` expects.
3. Deploy locally (`F5` / `/rootsuffix Exp`, or into XAE Shell directly) and
   verify: the "xStunit Results" tool window shows up (Other Windows menu),
   its WebView2 area loads (dark/light matching the XAE Shell theme — toggle
   Tools > Options > Environment > General to confirm it updates live), and
   shows the empty-state copy ("No results yet...") before any run. Clicking
   "Run tests" (needs a real `xstunit.json` — copy `xstunit.json.sample` and
   fill in `testProjectPath`, plus the `xstunit` CLI on PATH or `cliPath` set
   to a full path; optionally set `plugins` to a directory of
   `ITcXunitNativeFunction` plugin assemblies — forwarded to the CLI as
   `--plugins <dir>`, resolved relative to `xstunit.json`'s own directory the
   same way `cliPath` is) swaps the button to "Stop", shows the `.prog` sweep above
   the tree, and on completion renders the pass/fail tree and swaps the
   button back to "Run tests". Clicking "Stop" mid-run should kill the
   `xstunit` process (verify via Task Manager) and return the button to "Run
   tests" without touching whatever tree was already rendered from a prior
   run.
