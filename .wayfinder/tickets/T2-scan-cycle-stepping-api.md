---
id: T2
title: Scan-cycle stepping primitive API design
type: grilling
status: closed
assignee: wayfinder-work-session
blocked_by: []
---

## Question

The engine today runs a suite's statements exactly once per `RunSuite` /
`CallMethod` call — there's no notion of "advance the code under test by N
scan cycles" the way a real TwinCAT task loop does. This is the capability
the handoff calls the single most blocking one: "any test involving a state
machine or multi-cycle convergence needs this before anything else works."

Decide the API shape for a deterministic scan-cycle stepping primitive:
what does a test case call (e.g. something like `instance.StepCycles(n)`),
what runs on each step (just the FB's main body method, or also nested FB
calls / sub-instances?), what's the ordering guarantee across multiple FB
instances stepped together, and how do side effects (assignments, method
dispatch) behave identically to a real TwinCAT scan cycle from the test's
point of view. Also decide how this composes with the simulated-clock
ticket's "advance N cycles at simulated Δt per cycle" requirement, since
that ticket is blocked on this one's shape.

## Resolution

- **Primitive shape**: `FbInstance.StepCycles(int n)` — repeatedly re-invokes
  the FB's body `n` times, reusing existing `CallMethod`/`Cell` machinery.
  `Cell` fields on `FbInstance` already persist across calls, which is what
  makes repeated invocation behave like successive scans (mirrors real
  TwinCAT retain-by-default local VARs surviving between cycles). No new
  scheduler/loop abstraction needed for the single-instance case.
- **What runs each step**: the FB's own top-level body — the same
  `ImplementationText` that `RunSuite` already executes directly via
  `ExecuteStatements(Parser.ParseStatements(def.ImplementationText), frame)`.
  No blessed method name (no required "MAIN"/"Cycle" convention) — avoids
  forcing a naming convention onto FBs under test that don't have one today.
- **Multi-instance ordering**: no built-in multi-instance scheduler yet.
  Tests stepping multiple instances together (e.g. master + mirrored slave)
  call `StepCycles` per instance explicitly and control ordering themselves
  (`a.StepCycles(1); b.StepCycles(1);`). A first-class "step a group in
  deterministic order" primitive is **deliberately deferred, not dropped** —
  tracked in the map's Not yet specified so it isn't lost, since T6
  (state-mirroring assertions) will likely need it.
- **Composition with simulated clock (T3)**: `StepCycles` stays
  time-agnostic — no `dt` parameter added now. T3 owns deciding how
  simulated Δt composes with cycle stepping (separate overload or wrapper
  around `StepCycles`), keeping this ticket scoped to cycle-count stepping
  only.
