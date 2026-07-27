# TcUnit-XUnit-Runner — THROWAWAY VSIX SPIKE

This repo is a **throwaway prototype**, not a product. It exists to answer one
question before any of this gets folded into the real `TcAgent` VSIX:

> Can a VSIX tool window running inside TwinCAT's XAE Shell (Isolated Shell,
> VS2017-based) shell out to `tcxunit run <path> --format json`, parse the
> result, render a navigable pass/fail list, and jump to the failing
> `.TcPOU`/method in TwinCAT's own editor?

See `c:\Users\BPA\AppData\Local\Temp\tcxunit-xae-vsix-handoff.md` for the full
background/decision trail (TcXunit repo, TcAgent repo, why in-solution csproj
wiring was rejected, why this is a separate VSIX instead).

## What's here

`src/TcXunitResultsSpike/` — a minimal VSIX (mirrors the working scaffolding
pattern from `C:\Git\TcAgentPlugin\src\TcAgent`: classic `packages.config`
VSSDK project, not SDK-style, targeting VS2017's Isolated Shell) with:

- A tool window ("TcXunit Results") that reads `tcxunit.json` next to the
  open `.plcproj`/solution for the test-project path.
- A button that shells out to `tcxunit run <path> --format json`, parses the
  JSON, and renders suites/tests as a simple tree with pass/fail state.
- No click-to-navigate-into-editor yet — first cut just proves the
  process-exec + JSON + tool-window-render loop works inside XAE Shell.

## Status / open questions this spike is checking

- [ ] Does XAE Shell actually load a VSIX tool window at all? (Confirmed:
      XAE Shell has no built-in Test Explorer — that part of the question is
      already answered, see handoff doc.)
- [ ] Does `Process.Start` work unrestricted from inside XAE Shell's process?
- [ ] Is the JSON produced by `tcxunit run --format json` sufficient to
      render a useful tree without additional CLI flags?

## Not done here (by design — this is a spike)

- No click-to-navigate into `.TcPOU` editor yet.
- No polish, no tests beyond what's needed to prove the loop works.
- `tcxunit.json` schema is a first guess, not finalized (see
  `tcxunit.json.sample`).

## Capture step (per the `prototype` skill)

Once this answers the question, the validated decision gets folded into
`C:\Git\TcAgentPlugin` for real. This repo/branch stays as the primary
source of the spike itself — it does not get merged into TcAgent's `main`.

## Building

Needs a TwinCAT XAE Shell (or VS2017 w/ VS SDK) install. See
`C:\Git\TcAgentPlugin`'s `build.ps1` for the reference build pipeline this
project's scaffolding was copied from — this spike doesn't have its own
build script yet.
