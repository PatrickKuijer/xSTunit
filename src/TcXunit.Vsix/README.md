# TcXunit.Vsix

VSIX tool window for TwinCAT XAE Shell that shells out to `tcxunit run <path>
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
TcXunit.sln` from the CLI will skip or fail on this project; build it via
`devenv.exe`/MSBuild with the VS SDK targets, same as `TcAgentPlugin`.

- `TcXunitVsixPackage.cs` / `ResultsToolWindow.cs` — the `AsyncPackage` +
  `ToolWindowPane`.
- `Commands/ShowResultsToolWindowCommand.cs` + `VSCommandTable.vsct` — menu
  command ("TcXunit Results") that shows the tool window.
- `TestRunner/TcxunitConfig.cs` — reads `tcxunit.json` (schema is a first
  guess — see `tcxunit.json.sample`).
- `TestRunner/TcxunitProcessRunner.cs` + `TcxunitModels.cs` — shells out to
  `tcxunit run <path> --format json` via `Process.Start` and deserializes the
  JSON with `JavaScriptSerializer` (System.Web.Extensions).
- `ResultsToolWindowControl.xaml(.cs)` — "Run tests" button + status line
  (native WPF) plus a `Microsoft.Web.WebView2.Wpf.WebView2` that loads
  `Resources/results.html`. Per TcXunit-1tt (rebuild as WebView2 per
  `docs/design-system.html`), the results tree itself now renders as HTML/CSS
  inside the WebView2 rather than a WPF `TreeView` — as of TcXunit-1tt.1 that
  page is still an empty shell (no tree render yet; see TcXunit-1tt.2+). No
  click-to-navigate into the `.TcPOU` editor yet either — deferred to
  TcXunit-1tt.4 (source file path is already in the JSON output as of
  TcXunit-8gj).
- `Resources/results.html`, `Resources/vsix-tokens.css`,
  `Resources/vsix-shell.css`, `Resources/results.css` — the WebView2 page and
  its shared/TcXunit-only stylesheets. `vsix-tokens.css`/`vsix-shell.css` are
  copy-sourced from `TcAgentPlugin/src/TcAgent/Resources/chat.css` per
  `docs/design-system.html` section 8's file-split plan and should be kept in
  sync with it by hand (no shared CSS package between the two repos yet).
  `results.css` is TcXunit-only and currently an empty skeleton.

## Building on Windows

1. Open `TcXunit.sln` in VS2017 (or the XAE Shell's own devenv). Restore
   NuGet packages (`packages.config` — pulls `Microsoft.VSSDK.BuildTools`
   etc.).
2. Build. Not yet compiled since migration — expect first-build friction:
   missing VS SDK components (`Microsoft.VisualStudio.MPF.15.0`, VSSDK
   BuildTools), or `VSToolsPath`/`Microsoft.VsSDK.targets` import failing if
   the VS SDK isn't registered the way `TcAgentPlugin` expects.
3. Deploy locally (`F5` / `/rootsuffix Exp`, or into XAE Shell directly) and
   verify: the "TcXunit Results" tool window shows up (Other Windows menu),
   its WebView2 area loads (empty shell page, dark/light matching the XAE
   Shell theme — toggle Tools > Options > Environment > General to confirm it
   updates live), and "Run tests (tcxunit)" shells out successfully (needs a
   real `tcxunit.json` — copy `tcxunit.json.sample` and fill in
   `testProjectPath`, plus the `tcxunit` CLI on PATH or `cliPath` set to a
   full path) and updates the status line with a pass/fail count. The results
   tree itself doesn't render into the WebView2 yet — see TcXunit-1tt.2.
