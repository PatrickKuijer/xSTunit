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
- `ResultsToolWindowControl.xaml(.cs)` — button + `TreeView` rendering
  suites/tests as pass/fail, red/green. No click-to-navigate into the
  `.TcPOU` editor yet — deferred (needs TcXunit-8gj, source file path in the
  JSON output).

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
   "Run tests (tcxunit)" shells out successfully (needs a real
   `tcxunit.json` — copy `tcxunit.json.sample` and fill in
   `testProjectPath`, plus the `tcxunit` CLI on PATH or `cliPath` set to a
   full path), and results render red/green per pass/fail.
