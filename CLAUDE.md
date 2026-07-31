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
dotnet build xStunit.sln
dotnet test xStunit.sln
dotnet run --project src/xStunit.Cli -- <path-to-POUs-directory>
```

## Architecture Overview

xUnit-style test runner for TwinCAT/IEC 61131-3 PLC code (TcUnit-inspired). Parses `.TcPOU` XML, interprets ST bodies, reports pass/fail — no TwinCAT runtime needed.

```text
src/
  xStunit.Parser        Parses .TcPOU XML into POU/method AST (TcPouParser)
  xStunit.Interpreter    Lexer/Parser/Engine executing ST over Cell-based value model;
                         TypeRegistry + SuiteDiscovery find suites via EXTENDS ancestry
                         to TcUnit.FB_TestSuite
  xStunit.Runner         TcUnit native-method stub boundary (assertions, suite host)
  xStunit.Cli            `xstunit <path>` entry point (CliRunner is testable core)
tests/                  xUnit tests per project, mirroring src/
```

Exit codes: `0` all pass, `1` any fail, `2` usage/discovery error. Unloadable files are skipped and reported per file (never exit `2`). `--help`/`-h` also exits `0` (a successful, explicitly requested action, not a run).

## Conventions & Patterns

Interpreter is hand-rolled, scoped to what fixtures actually need — extended incrementally, not built to full IEC 61131-3 grammar up front (see comments in `Engine.cs`/`Lexer.cs`). Early/grow-on-demand status; check `wiki/` for in-progress design decisions.

## Comment Standard

The house style for comments in `.cs` files. This is the definition of done for
any comment work, and the bar new code is held to.

**Good code doesn't need comments.** Self-documenting names and small,
well-shaped methods are the default; a comment is the exception that earns its
place, not the norm. When in doubt, delete rather than reformat — a wrong or
stale comment is worse than no comment, and a deleted one can't rot.

**XML doc `<summary>` only when it adds information beyond the signature.**
Skip it entirely for a member whose name and type already say everything (a
`Name` property of type `string`, a `Parse(string xml)` method with an obvious
return). Write it when the name doesn't tell the whole story: what a
non-obvious return value means, what state a type represents, why a type
exists at all.

- `<param>` / `<returns>` wherever the name alone does not tell the caller what
  to pass or what comes back (units, ranges, null/empty semantics, ownership).
  Skip when the parameter name already says it — but note `<param>` is
  all-or-nothing per member: documenting some and not others raises CS1573, so
  once one parameter needs a tag, give the rest a short one too.
- `<exception>` for anything thrown that a caller must handle — including what
  distinguishes it from neighbouring exception types.
- `<remarks>` only for a real invariant, ordering requirement, or gotcha: what
  must stay true, what breaks if it doesn't, why the obvious alternative was
  rejected. Not a place to restate the summary in more words.
- `<see cref="..."/>` for cross-references, so renames don't rot the prose.

**Inline comments state WHY, not WHAT.** A comment earns its place by carrying
what the code cannot: the invariant being held, the case being guarded against,
the non-obvious interaction with another component. A comment that restates the
line below it is deleted, not reworded.

**When reviewing or overhauling existing comments, delete before you reformat.**
A comment that fails the tests above (restates the code, is stale, hedges, or
just repeats the type signature in prose) gets removed, not polished into
better-formatted junk. Reformatting a bad comment gives it a second life it
didn't earn.

**Never encode ticket IDs, agent reasoning, or change history in a comment.**
No bead IDs, no "fixed the bug where...", no "as requested". That belongs in the
commit message, the `bd close` reason, or the PR description. Comments describe
the code as it stands; history lives in git.

**Test comments state the invariant the test pins, in prose.** Say what would be
broken if the test went red — not what the arrange/act/assert lines do. A
regression test whose reason for existing is written down survives the refactor
that would otherwise delete it as redundant.

## Naming Constraints (rename in progress)

Product name is `xStunit`; repo/package identity is renaming away from TwinCAT/Beckhoff-adjacent naming to avoid trademark confusion (epic TcXunit-tbih). `src/` namespaces, project names, and the solution file (`xStunit.sln`) are already renamed. Still open: `samples/` projects (TcXunit-tbih.6), a naming decision for our own classes that mirror Beckhoff file-format names like `TcPouParser` (TcXunit-tbih.3), and the repo directory/GitHub repo rename itself, done last (TcXunit-tbih.10).

Do NOT reuse the `Tc`/`TC` namespace or Beckhoff's own naming conventions (`Tc2_*`, `TcPOU`, `Tc*` prefixes, etc.) in any NEW identifier — namespaces, project names, class names, file names, CLI flags, config keys. If a new type needs to reference a TwinCAT/Beckhoff concept, name it after the IEC 61131-3 or domain concept instead (e.g. `BistableLatchHost`, not `TcBistableHost`).
