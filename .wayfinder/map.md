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
- [Scan-cycle stepping primitive API design](tickets/T2-scan-cycle-stepping-api.md) — `FbInstance.StepCycles(int n)` re-invokes the FB's top-level body `n` times, reusing existing `CallMethod`/`Cell` persistence (no new scheduler); no blessed body-method name; multi-instance ordering is caller-controlled for now (group-step scheduler deferred, see Not yet specified); stays time-agnostic — T3 owns clock composition.
- [Simulated clock for TON/TOF/FB_Pulse design](tickets/T3-simulated-clock-design.md) — `Engine.Clock.Advance(dt)` as a separate primitive from `StepCycles` (test sequences both explicitly); one shared clock on `Engine`, not per-instance; TON/TOF/FB_Pulse are native C# stubs (`TcUnitSuiteHost`-style boundary) with a dedicated host object per instance holding its own last-observed absolute clock total (monotonic running total, not a one-shot delta — immune to multi-instance stepping order); `ET` clamps to `min(rawElapsed, PT)` for display while firing checks the unclamped raw elapsed, giving tests an unambiguous boundary recipe; clock accumulator is `long` ms internally regardless of `TIME`'s 32-bit width.
- [In-process transport loopback design](tickets/T4-loopback-transport-design.md) — copy-based transport (not Cell-aliasing) via an explicit `Transmit()` call, no auto-copy tied to `StepCycles`; fields selected via ordinary ST `REFERENCE TO` parameter passing (`Frame.Ref`), no naming/attribute convention; payload copy stays generic (`sink.Value = source.Value`) with struct/array copy semantics deferred to whenever `STRUCT`'s `Cell` shape is designed; dispatched via the same native-stub-boundary pattern as `TcUnitSuiteHost` (`Loopback` FB type, `NativeMethodBridge` case, host class); one `Loopback` instance = one fixed link, holding per-link state for fault injection (T5) to use.
- [Fault injection hooks on loopback design](tickets/T5-fault-injection-design.md) — fault state/methods live directly on the `Loopback` instance (no decorator, no separate fault object), dispatched via the same `NativeMethodBridge` pattern as `Transmit`; one active fault mode at a time (no composable stack); vocabulary is `Drop`/`Restore`, `Freeze` (value stops changing, link stays up — this *is* "stale but connected"), `SetDelay(n)` (FIFO over `Transmit()` calls), `Duplicate()` and `Corrupt(value)` (one-shot flags); assertion layer distinguishes hard drop from staleness via two native properties, `LinkUp: BOOL` (false only under `Drop`) and `LastUpdateTime` (an `Engine.Clock` snapshot per T3's clock model), so watchdog tests assert elapsed-since-update against `LinkUp` rather than reaching into internal wiring.
- [State-mirroring assertion helper API](tickets/T6-state-mirroring-assertions.md) — `AssertConverges(master, proxy, string[] fieldNames, int maxCycles)`: helper owns the stepping loop itself (`master.StepCycles(1); proxy.StepCycles(1);` per iteration, fixed master-then-proxy order, up to `maxCycles`), comparing an explicit field-name list by string lookup through existing `FbInstance` field access — no whole-struct diff, no caller predicate. On non-convergence, throws with a per-field diff of master vs proxy final values (not just pass/fail). A second first-class variant, `AssertConvergesAndLatches(...)`, covers the handoff's "flips exactly once and stays latched" case as its own method rather than a caller-composed loop. This resolves the group-step-scheduler question T2 deferred, at least for the two-instance master/proxy case: no separate scheduler primitive — the fixed-order loop lives inside the assertion helper itself.
- [Struct/record test-data builder & boundary-generation design](tickets/T7-struct-boundary-builders.md) — pure introspection over `STRUCT`'s declared fields, no per-struct builder registration; `STRUCT` `Cell` value reuses `FbInstance.Fields`'s `Dictionary<string, Cell>` shape, which means `Transmit` needs a per-field clone for structs (not `sink.Value = source.Value`) to keep T4's copy-not-alias contract; boundary = numeric type min/max, `STRING` empty/max-length (both need small type-metadata groundwork not yet built), array count boundaries only for an explicit count-field pairing (not auto-inferred); combination API is single-field sweep by default, explicit per-field overrides for simultaneous boundaries; no shared serialization layer with the loopback transport — both operate at the `Cell` level, not bytes.
- [Session/generation-counter simulation design](tickets/T8-session-generation-counter-sim.md) — counter is an ordinary `VAR` field on the FB under test (not `Loopback`/fault-injection state), set directly via ST assignment; the two reconnect scenarios compose from T5's existing `Drop()`/`Restore()` vocabulary (brief blip = Restore with no field change, restart = Restore plus test bumps the counter field) rather than a new native "reconnect" operation; assertion targets the FB-under-test's own observable reconnect-path output, not the counter field or loopback wiring.

## Not yet specified

- First-class multi-instance "step a group in deterministic order"
  scheduler for groups larger than a master/proxy pair (3+ instances,
  or orderings other than fixed master-then-proxy) — the two-instance
  pair case is now covered by State-mirroring assertion helper API's
  own internal stepping loop; only worth a ticket if a concrete
  capability test needs more than a pair or a non-fixed order.
- How the new primitives (cycle stepping, simulated clock, loopback) get
  surfaced to test authors at the fixture/DSL level (attributes? special
  VAR sections? runner config?) — depends on the API shapes decided in the
  cycle-stepping, simulated-clock, and loopback tickets first.
- Composable/simultaneous fault stacking on one loopback link (e.g. delay
  *and* corrupt at once) — deliberately deferred by T5's resolution; only
  worth a ticket if a concrete capability test needs it.
- How this spec's decisions get formatted for `/to-spec` handoff — a
  process question for whoever runs `/to-spec`, not a design decision this
  map needs to resolve.

## Out of scope

- Building the actual multi-controller remote-pair FBs — that's
  `b_beckhoff_framework/PLC`'s job, not TcXunit's, per the handoff doc.
- Hardware-in-the-loop or real dual-controller commissioning support — the
  handoff is specifically about in-runtime loopback/simulation testing.
