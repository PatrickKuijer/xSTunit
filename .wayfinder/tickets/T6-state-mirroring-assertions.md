---
id: T6
title: State-mirroring assertion helper API
type: grilling
status: closed
assignee: wayfinder-work-session
blocked_by: [T2]
---

## Question

Nearly every integration-style test with a master/proxy pair needs "these
two state-machine instances converge to the same state within N cycles"
rather than each feature hand-rolling its own polling/assert loop.

Decide the assertion helper's API shape, building on the scan-cycle
stepping primitive from T2 (scan-cycle stepping primitive API design):
what does "same state" mean generically (compare specific declared
`VAR_OUTPUT`s? a whole struct? a caller-supplied predicate?), what happens
if convergence never happens within N cycles (does it report the last
divergent field, per the "assert on the observable contract, not internal
wiring" philosophy in this map's Notes), and does it also need to assert a
"latches and stays converged" property (per the handoff's "flips exactly
once and stays latched" example) as a first-class variant, not just
one-shot convergence.

## Resolution

- **API shape**: `AssertConverges(FbInstance master, FbInstance proxy,
  string[] fieldNames, int maxCycles)`. Field selection is an explicit
  list of field names (not a caller predicate, not a whole-struct diff)
  — matches the map's "assert the observable contract, not internal
  wiring" testing philosophy and needs no `STRUCT`/`Cell` design work
  since it's just string-keyed lookup through existing `FbInstance`
  field access.
- **Who steps**: the helper owns the stepping loop internally —
  `for i in 1..maxCycles { master.StepCycles(1); proxy.StepCycles(1);
  compare(fieldNames); }`, fixed master-then-proxy order per iteration.
  Callers don't hand-roll the polling loop; that's the whole point of
  the ticket. This resolves T2's deferred "step a group in deterministic
  order" question for the two-instance pair case: no separate scheduler
  primitive is needed, the fixed order lives inside the assertion
  helper. Larger groups (3+ instances) or non-fixed orderings remain
  out of scope until a concrete test needs them (see map's Not yet
  specified).
- **Failure reporting**: on non-convergence within `maxCycles`, throws
  with a per-field diff of master vs proxy final values (not just
  pass/fail, not full per-cycle history) — gives the test author an
  actionable diagnosis without extra tooling.
- **Latch variant**: `AssertConvergesAndLatches(master, proxy,
  fieldNames, maxCycles)` is a separate first-class method (not a
  caller-composed wrapper around `AssertConverges`), covering the
  handoff's "flips exactly once and stays latched" barrier case
  directly.
