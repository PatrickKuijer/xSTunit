# Handoff: build/validate the TcXunit VSIX spike on a VS2017 machine

## Context

This is a **throwaway spike**, not production code. Full background/decision
trail: `C:\Users\BPA\AppData\Local\Temp\tcxunit-xae-vsix-handoff.md` (may not
exist on this machine — ask the user to hand it over if you need it, or just
rely on this file plus the repo's own README).

Repo: `TcUnit-XUnit-Runner` (currently at `C:\Git\TcUnit-XUnit-Runner` on the
authoring machine, single local commit `f14ef7b`, **no remote configured
yet** — the user will need to get the repo onto this machine some other way:
`git remote add`+push/pull, a shared drive copy, or a zip. Ask the user how
they want to transfer it if it isn't already here.)

Read [README.md](../../../Git/TcUnit-XUnit-Runner/README.md) in that repo
first — it states the spike's question and status. Short version: does a
VSIX tool window running inside TwinCAT XAE Shell (VS2017-based Isolated
Shell) successfully shell out to `tcxunit run <path> --format json`, parse
the JSON, and render a pass/fail tree? (XAE Shell having no built-in Test
Explorer is already confirmed — that part is settled, not something to
re-check.)

## What's already scaffolded (commit `f14ef7b`)

`src/TcXunitResultsSpike/` — a classic (non-SDK-style) `packages.config`
VSSDK project targeting `net472`/AnyCPU/VS SDK 15.0, mirroring the confirmed-
working scaffolding pattern from `C:\Git\TcAgentPlugin\src\TcAgent` (that repo
may also need to be handed over/available on this machine as a reference —
ask the user if it's not already here):

- `TcXunitResultsSpikePackage.cs` / `ResultsToolWindow.cs` — the
  `AsyncPackage` + `ToolWindowPane`.
- `Commands/ShowResultsToolWindowCommand.cs` + `VSCommandTable.vsct` — menu
  command ("TcXunit Results") that shows the tool window.
- `TestRunner/TcxunitConfig.cs` — reads `tcxunit.json` (schema is a first
  guess — see `tcxunit.json.sample` at repo root).
- `TestRunner/TcxunitProcessRunner.cs` + `TcxunitModels.cs` — shells out to
  `tcxunit run <path> --format json` via `Process.Start` and deserializes the
  JSON with `JavaScriptSerializer` (System.Web.Extensions).
- `ResultsToolWindowControl.xaml(.cs)` — button + `TreeView` rendering
  suites/tests as pass/fail, red/green. **No click-to-navigate into the
  `.TcPOU` editor yet** — intentionally deferred.
- `TcXunitResultsSpike.sln` at repo root references the csproj.

None of this has been built or run yet — it was authored on a machine without
VS2017/the VS SDK installed, so it's untested. **That's your job.**

## What to do on this machine

1. Confirm the repo is present (or get it here per the transfer note above),
   confirm `C:\Git\TcAgentPlugin` is available too (optional, just a
   reference for scaffolding conventions if something doesn't build).
2. Open `TcXunitResultsSpike.sln` in VS2017 (or via the XAE Shell's devenv,
   whichever this machine has). Restore NuGet packages
   (`packages.config` — should pull `Microsoft.VSSDK.BuildTools` etc.).
3. Build. Expect first-build friction — this scaffold has not been compiled
   even once. Likely trouble spots, in rough order of likelihood:
   - Missing VS SDK components (`Microsoft.VisualStudio.MPF.15.0`, VSSDK
     BuildTools) not installed on this machine yet.
   - `VSToolsPath`/`Microsoft.VsSDK.targets` import failing if the VS SDK
     isn't registered the way `TcAgentPlugin`'s working project expects it.
   - `System.Web.Extensions` reference (used for `JavaScriptSerializer`) not
     resolving — it's a Framework reference, should be fine on a normal
     VS2017 install, but worth checking first if `JavaScriptSerializer` is
     unresolved.
4. Once it builds, deploy locally (`F5` / `/rootsuffix Exp`, or actually into
   the XAE Shell if this machine has it) and manually verify:
   - The "TcXunit Results" tool window shows up (menu: Other Windows, per
     the VSCT's `IDG_VS_WNDO_OTRWNDWS1` placement).
   - Clicking "Run tests (tcxunit)" shells out successfully — needs a real
     `tcxunit.json` (copy `tcxunit.json.sample`, fill in a real
     `testProjectPath`) and the `tcxunit` CLI on PATH (or set `cliPath` in
     the config to a full path).
   - Results render as a tree, red/green per pass/fail.
5. Note whatever breaks — this is expected. Fix forward if the fix is small
   and obvious (e.g. a missing package version, a path typo); if it's a
   structural problem (VSSDK just won't load in this XAE Shell at all),
   that's the actual answer to the spike's question — stop and report it,
   don't fight it into submission.
6. **Do not fold this into `TcAgentPlugin` yet.** Per the `prototype` skill:
   validate here first, then the *decision* (not this code verbatim) gets
   carried into `TcAgentPlugin` separately. This repo/branch stays as the
   throwaway primary source.

## Answering the open question

Once you've gotten as far as you can, report back explicitly:
- Did the VSIX load and the tool window appear inside XAE Shell (not just
  plain VS2017)? These can behave differently — Isolated Shell sometimes
  restricts what loads.
- Did `Process.Start` succeed unrestricted from inside XAE Shell's process?
- Was the JSON from `tcxunit run --format json` sufficient to render
  something useful, or did you need more CLI flags/fields?

## Don't re-derive

- Don't re-litigate VSIX-vs-companion-.sln-vs-CI — settled, see the original
  handoff doc.
- Don't re-implement `tcxunit run --format json` — it already exists in
  `C:\Git\TcXunit` (`src/TcXunit.Cli/CliRunner.cs`), just consume it.
- Don't add click-to-navigate or polish yet — prove the core loop first.
- No git push anywhere without asking the user first (repo policy elsewhere
  in this project is commit-on-close, no auto-push — assume the same here
  unless told otherwise).
