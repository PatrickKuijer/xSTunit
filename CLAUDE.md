# Project Instructions for AI Agents

This file provides instructions and context for AI coding agents working on this project.

<!-- BEGIN BEADS INTEGRATION v:1 profile:minimal hash:970c3bf2 -->
## Beads Issue Tracker

This project uses **bd (beads)** for issue tracking. Run `bd prime` to see full workflow context and commands.

### Quick Reference

```bash
bd ready              # Find available work
bd show <id>          # View issue details
bd update <id> --claim  # Claim work
bd close <id>         # Complete work
```

### Rules

- Use `bd` for ALL task tracking — do NOT use TodoWrite, TaskCreate, or markdown TODO lists
- Run `bd prime` for detailed command reference and session close protocol
- Use `bd remember` for persistent knowledge — do NOT use MEMORY.md files

**Architecture in one line:** issues live in a local Dolt DB; sync uses `refs/dolt/data` on your git remote; `.beads/issues.jsonl` is a passive export. See https://github.com/gastownhall/beads/blob/main/docs/SYNC_CONCEPTS.md for details and anti-patterns.

## Agent Context Profiles

The managed Beads block is task-tracking guidance, not permission to override repository, user, or orchestrator instructions.

- **Conservative (default)**: Use `bd` for task tracking. Do not run git commits, git pushes, or Dolt remote sync unless explicitly asked. At handoff, report changed files, validation, and suggested next commands.
- **Minimal**: Keep tool instruction files as pointers to `bd prime`; use the same conservative git policy unless active instructions say otherwise.
- **Team-maintainer**: Only when the repository explicitly opts in, agents may close beads, run quality gates, commit, and push as part of session close. A current "do not commit" or "do not push" instruction still wins.

## Session Completion

This protocol applies when ending a Beads implementation workflow. It is subordinate to explicit user, repository, and orchestrator instructions.

1. **File issues for remaining work** - Create beads for anything that needs follow-up
2. **Run quality gates** (if code changed) - Tests, linters, builds
3. **Update issue status** - Close finished work, update in-progress items
4. **Handle git/sync by active profile**:
   ```bash
   # Conservative/minimal/default: report status and proposed commands; wait for approval.
   git status

   # Team-maintainer opt-in only, unless current instructions forbid it:
   git pull --rebase
   bd dolt push
   git push
   git status
   ```
5. **Hand off** - Summarize changes, validation, issue status, and any blocked sync/commit/push step

**Critical rules:**
- Explicit user or orchestrator instructions override this Beads block.
- Do not commit or push without clear authority from the active profile or the current user request.
- If a required sync or push is blocked, stop and report the exact command and error.
<!-- END BEADS INTEGRATION -->



## Project Git Policy (overrides Conservative profile above)

On closing a bead (`bd close <id>`), commit the resulting changes with a Conventional Commits message (`feat:`, `fix:`, `chore:`, etc., semver-relevant type). Do NOT push — commit only.

## Agent skills

### Issue tracker

Issues tracked in beads (`bd`), a local Dolt DB synced via `refs/dolt/data`. See `docs/agents/issue-tracker.md`.

### Domain docs

No CONTEXT.md or docs/adr/ exist yet in this repo. See `docs/agents/domain.md` for how to consume them once present.

## Build & Test

Requires .NET SDK (`net8.0` for CLI, `netstandard2.0` for interpreter/runner/parser libs).

```bash
dotnet build TcXunit.sln
dotnet test TcXunit.sln
dotnet run --project src/TcXunit.Cli -- <path-to-POUs-directory>
```

## Architecture Overview

xUnit-style test runner for TwinCAT/IEC 61131-3 PLC code (TcUnit-inspired). Parses `.TcPOU` XML, interprets ST bodies, reports pass/fail — no TwinCAT runtime needed.

```text
src/
  TcXunit.Parser        Parses .TcPOU XML into POU/method AST (TcPouParser)
  TcXunit.Interpreter    Lexer/Parser/Engine executing ST over Cell-based value model;
                         TypeRegistry + SuiteDiscovery find suites via EXTENDS ancestry
                         to TcUnit.FB_TestSuite
  TcXunit.Runner         TcUnit native-method stub boundary (assertions, suite host)
  TcXunit.Cli            `tcxunit <path>` entry point (CliRunner is testable core)
tests/                  xUnit tests per project, mirroring src/
```

Exit codes: `0` all pass, `1` any fail, `2` usage/discovery error. Unloadable files are skipped and reported per file (never exit `2`). `--help`/`-h` also exits `0` (a successful, explicitly requested action, not a run).

## Conventions & Patterns

Interpreter is hand-rolled, scoped to what fixtures actually need — extended incrementally, not built to full IEC 61131-3 grammar up front (see comments in `Engine.cs`/`Lexer.cs`). Early/grow-on-demand status; check `wiki/` for in-progress design decisions.
