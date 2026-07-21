---
id: T2
title: Scan-cycle stepping primitive API design
type: grilling
status: open
assignee:
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
