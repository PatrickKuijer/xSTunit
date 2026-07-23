# Issue tracker: beads (bd)

Issues and PRDs for this repo live in beads, a local Dolt database. Use the `bd` CLI for all operations — do not use GitHub Issues, TodoWrite, or markdown TODO lists.

Run `bd prime` at session start for full workflow context and the session-close protocol.

## Conventions

- **Create an issue**: `bd create --title="..." --description="..." --type=task|bug|feature --priority=2`
- **Read an issue**: `bd show <id>`
- **List issues**: `bd list --status=open`, `bd ready` (unblocked work)
- **Claim**: `bd update <id> --claim`
- **Comment / update**: `bd update <id> --notes="..."` (do not use `bd edit`, it opens `$EDITOR` and blocks agents)
- **Close**: `bd close <id> --reason="..."`
- **Dependencies**: `bd dep add <issue> <depends-on>`, `bd blocked`

## Sync

- `bd dolt push` / `bd dolt pull` — sync issue DB with the git remote (`refs/dolt/data`)
- `.beads/issues.jsonl` is a passive export, not the source of truth
- Conservative git/sync policy applies by default: do not push or sync without explicit authority (see `## Agent Context Profiles` in CLAUDE.md)

## When a skill says "publish to the issue tracker"

Create a beads issue with `bd create`.

## When a skill says "fetch the relevant ticket"

Run `bd show <id>`.

## Pull requests as a triage surface

Not applicable — this repo has no external-PR triage flow through beads.

## Historical planning artifacts (not live trackers)

This repo previously used a `.wayfinder/` planning map (`/grilling` + `/domain-modeling`
workflow) to design the simulated-PLC-testing capability spec. That map is closed and
deleted — its full content lives in beads epic `TcXunit-w5x.15` (`bd show TcXunit-w5x.15`).
Point-in-time cross-repo briefing docs are archived under `docs/handoff-archive/` (also
superseded by beads). If you spot a `.wayfinder/` or `.handoff/` folder again, check beads
first — do not treat either as a second live tracker.
