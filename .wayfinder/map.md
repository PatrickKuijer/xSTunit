---
label: wayfinder:map
tracker: local-markdown
---

# Map: TcXunit simulated-PLC-testing capability spec

> **Tracker note:** `bd`/beads is unavailable in this workspace, so this map
> uses the local-markdown tracker fallback described in the wayfinder skill.
> Conventions:
> - This file (`map.md`) is the map. Tickets are files under `tickets/`,
>   named `<id>-<slug>.md`.
> - Each ticket file has YAML frontmatter: `id`, `title`, `type` (one of
>   `research`/`prototype`/`grilling`/`task`), `status` (`open`/`closed`),
>   `assignee` (empty = unclaimed), `blocked_by` (list of ticket ids).
> - **Claim** a ticket by setting `assignee` before starting work.
> - **Blocking**: a ticket is unblocked when every id in its `blocked_by`
>   list points at a ticket with `status: closed`.
> - **Frontier** = open + unblocked + unclaimed tickets. To find it, grep
>   `tickets/*.md` for `status: open` and check each one's `blocked_by`
>   against the closed set.
> - On resolution: add a `## Resolution` section to the ticket body, set
>   `status: closed`, and append a line to this map's "Decisions so far".

## Destination

A written TcXunit capability spec: every design decision needed to build
deterministic scan-cycle stepping, simulated time, in-process transport
loopback + fault injection, state-mirroring assertions, struct/record test
data builders, session/generation-counter simulation, and the type-system
groundwork (REAL/arithmetic/arrays/structs) they depend on — locked well
enough to run through `/to-spec` and then `/to-tickets` for implementation.
No implementation happens on this map; it produces decisions only.

## Notes

- Domain: PLC/TwinCAT structured-text test tooling (TcXunit interpreter:
  `src/TcXunit.Interpreter`), driven by a capability ask from a *different*
  repo (`b_beckhoff_framework/PLC`, multi-controller framework epic
  `PLC-dxz`), captured in
  [tcxunit-framework-test-requirements-handoff.md](../.handoff/tcxunit-framework-test-requirements-handoff.md).
  Do not duplicate or edit that handoff doc — it's an input, not tracked here.
- Scope explicitly includes the type-system gaps identified separately
  (REAL/LREAL, arithmetic/boolean operators, TIME, arrays, structs) as
  in-scope groundwork the capability work depends on — not just the 7
  handoff capabilities verbatim.
- Build on the current hand-rolled `Engine`/`Lexer`/`Parser`/`Cell` design,
  grow-on-demand, per existing project convention (see comments in
  `Engine.cs`/`Lexer.cs`). Redesign of the interpreter core is not assumed,
  but isn't ruled out if a ticket's resolution shows it's necessary — if
  that happens, note it as a decision rather than silently reworking scope.
- Testing philosophy to carry over into every capability decision: assert
  the *observable contract* at the highest reasonable seam, not private
  internal wiring. Treat any capability API that nudges toward asserting
  internal state as a design smell worth flagging in that ticket's
  resolution.
- Skills to consult while working tickets: `/grilling`, `/domain-modeling`,
  `/research` (used already for the type-system prioritization ticket;
  reuse for any later ticket that needs an IEC 61131-3/TwinCAT reference
  check rather than guessing at grammar).

## Decisions so far

- [Type-system gap prioritization & sequencing](tickets/T1-type-system-gap-sequencing.md) — add `REAL`/`LREAL` first (self-contained, IEEE754), then `TIME` literal + elapsed-delta clock model (TON's `ET` is scan-accumulated since last rising edge, not an absolute clock, so simulated time only needs a per-call delta), then `STRUCT`/`ARRAY` last (8-byte-aligned since TwinCAT 3 — matters for the loopback wire format). None of the three force a `Cell`-value-model rework; see [research notes](research/T1-iec61131-types-research.md).

## Not yet specified

- How the new primitives (cycle stepping, simulated clock, loopback) get
  surfaced to test authors at the fixture/DSL level (attributes? special
  VAR sections? runner config?) — depends on the API shapes decided in the
  cycle-stepping, simulated-clock, and loopback tickets first.
- Watchdog/staleness semantics beyond generic fault injection (e.g. exact
  "stale but connected" timestamp-age model) — depends on the fault
  injection and simulated clock tickets; may graduate into its own ticket
  once those land.
- Whether/how struct-boundary test-data builders and the loopback transport
  share a wire-format serialization layer, or are independent — depends on
  the loopback and struct-builder ticket resolutions.
- How this spec's decisions get formatted for `/to-spec` handoff — a
  process question for whoever runs `/to-spec`, not a design decision this
  map needs to resolve.

## Out of scope

- Building the actual multi-controller remote-pair FBs — that's
  `b_beckhoff_framework/PLC`'s job, not TcXunit's, per the handoff doc.
- Hardware-in-the-loop or real dual-controller commissioning support — the
  handoff is specifically about in-runtime loopback/simulation testing.
