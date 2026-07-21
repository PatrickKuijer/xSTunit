---
id: T3
title: Simulated clock for TON/TOF/FB_Pulse design
type: grilling
status: closed
assignee: claude
blocked_by: [T1, T2]
---

## Question

Tests need to fast-forward elapsed time (e.g. a 500 ms pulse + 3 s drop
timeout) without waiting real time per test, and this needs to compose with
scan-cycle stepping as "advance N cycles at simulated Δt per cycle."

Decide: how does the interpreter model elapsed time for `TON`/`TOF`/
`FB_Pulse` — is there a virtual clock object injected into `Frame`/`Engine`
that these timer FBs read instead of a real system clock, or is elapsed time
passed explicitly into the stepping primitive from T2 (scan-cycle stepping
primitive API design)? Decide the `TIME` value representation this relies
on (from T1, type-system gap prioritization & sequencing) and how a test
asserts "did not fire just short of the timeout" vs "fired exactly at/after
it" without off-by-one scan-cycle ambiguity.

## Resolution

- **Composition with stepping**: separate primitive, not bundled into
  `StepCycles`. `Engine.Clock.Advance(dt)` is called independently;
  `FbInstance.StepCycles(n)` stays exactly as T2 decided (no `dt` param).
  Test authors sequence them explicitly (`clock.Advance(dt);
  instance.StepCycles(1);`), which also fixes the boundary-assertion
  ordering: a delta is only reflected in a timer's `ET` on the step that
  runs *after* the `Advance` call that produced it.
- **Clock scope**: one shared clock on `Engine`, process-wide for the whole
  suite run. Not per-`FbInstance`, not per-`Frame` (Frame doesn't persist
  across calls, so it can't hold timer state anyway). Per-instance clock
  skew is out of scope unless a later ticket proves it's needed (mirrors
  T2's deferred multi-instance scheduler).
- **TON/TOF/FB_Pulse implementation**: native C# stubs, same native-stub
  boundary precedent as `TcUnitSuiteHost`/`FB_TestSuite` — not interpreted
  ST bodies. Avoids inventing a new ST-level intrinsic just to expose the
  clock, and keeps timer edge-detection logic directly testable in C#.
- **Clock shape**: monotonic absolute running total, not a one-shot
  "delta since last consumed" value. Each timer instance stores its own
  last-observed total and computes its own delta whenever it runs. This is
  what makes the design immune to T2's caller-controlled multi-instance
  stepping order — a one-shot delta would let whichever instance runs
  first "steal" it.
- **State storage**: each timer instance gets a dedicated native host
  object (e.g. `TonHost`), analogous to `NativeSuiteHost`/
  `TcUnitSuiteHost` — plain C# fields for internal bookkeeping
  (last-observed clock total, `IN` edge-detect state), publishing `Q`/`ET`
  back into the instance's normal `Cell` fields after each invocation so
  ST code reads them unchanged. Not synthetic entries in
  `FbInstance.Fields` — keeps native bookkeeping out of a dictionary ST
  code and test assertions can otherwise freely enumerate.
- **Firing/overshoot semantics**: internal raw elapsed time accumulates
  unclamped and drives the firing check (`Q = IN AND rawElapsed >= PT`);
  the *published* `ET` clamps to `min(rawElapsed, PT)`, matching real
  TON's documented contract (Beckhoff Tc2_Standard: "Q is TRUE, if IN =
  TRUE and ET = PT"). This resolves the off-by-one ambiguity for tests:
  advance to `PT - 1` unit then step to assert "not yet fired"; advance to
  `PT` or beyond then step to assert "fired" — and it correctly handles a
  single large fast-forward jump overshooting `PT` in one `Advance` call,
  which is expected given the whole point of simulated time is skipping
  multi-second real waits in one jump.
- **Clock accumulator width**: `long` ms internally (matching `LTIME`'s
  width from T1), even though individual `TIME` values (`PT`, published
  `ET`) stay 32-bit `int` per T1. Removes clock overflow as a concern for
  long-running or looped suites at negligible cost.
