---
id: T6
title: State-mirroring assertion helper API
type: grilling
status: open
assignee:
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
