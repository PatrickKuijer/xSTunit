> **ARCHIVED — historical snapshot, not a live tracker.** This doc's requirements were
> fully absorbed into beads epic `TcXunit-w5x.15` (closed) — run `bd show TcXunit-w5x.15`
> for current status. Do not treat this file as an open issue list; beads is the single
> source of truth. Moved here from `.handoff/` on 2026-07-23.

# Handoff: xStunit capability requirements from PLC framework repo

**Date:** 2026-07-21
**From repo:** `b_beckhoff_framework/PLC` (framework/machine-code development — NOT the xStunit tool itself)
**To:** xStunit team (separate repo/tool — the xUnit-style TCP test runner, TcUnit-inspired, currently in development)
**Purpose:** hand the xStunit team a capability spec, derived from a concrete upcoming framework feature, so they can scope their own tickets. This is an *input* to their planning, not a ticket in their tracker — do not paste it in verbatim; run it through their own `/to-spec` if they want it tracked.

## Origin / do-not-duplicate

The feature driving this is already tracked in this repo's issue tracker (`bd`), do not re-describe it here — reference by ID if you need the detail:

- Epic: `PLC-dxz` — "[Spec] Multi-controller framework (strategy 3 — Keba/Beckhoff combination remote pair)" — contains a full "Testing Decisions" section already.
- Children (`PLC-dxz.1` through `PLC-dxz.8`) — each an implementation slice (transport table, payload structs, RemotePpar convergence, value sync barrier, machine/unit pairing, resync ordering, watchdog staleness).

Key fact from the epic: **this repo has no automated PLC test harness today.** Everything is currently validated by loopback simulation FBs hand-built per feature, or by commissioning on real controllers. That's the gap xStunit is meant to close.

## Why this matters to xStunit (not just this feature)

The multi-controller framework work is the first framework feature being built test-first. What it needs from a test runner is a reasonable proxy for what *most* future framework features will need, because the underlying problem is generic to PLC/TwinCAT code: **scan-cycle-based state machines, simulated time, and simulated transport**, not just this one feature's specifics.

## Capability requirements (the actual ask)

These are runner/fixture capabilities, ordered by how blocking they are to writing any test at all for this class of feature:

1. **Deterministic scan-cycle stepping** — ability to advance the code under test by exactly N scan cycles from a test case, with no reliance on wall-clock/real-time waits. Blocking: any test involving a state machine or multi-cycle convergence needs this before anything else works.

2. **Simulated clock for `TON`/`TOF`/`FB_Pulse`** — tests need to fast-forward elapsed time (e.g. a 500 ms pulse + 3 s drop timeout) without waiting 3 real seconds per test. Needs to compose with (1) — i.e. "advance N cycles at simulated Δt per cycle."

3. **In-process transport loopback support** — a way to wire one FB's transmit buffer directly into another FB's receive buffer within a single test runtime (no real socket, no second controller/runtime). This is the single biggest unblock — most of the framework's own test cases are built around a hand-rolled version of this per-feature; a first-class runner primitive for it would remove that duplicated effort.

4. **Fault injection on the loopback** — hooks to, mid-test: drop the link entirely, delay/duplicate/corrupt one message, freeze one side's transmit, or hold a value "stale but still connected." Needed for watchdog/resync-class tests, which are common in any master/slave or redundant-link design.

5. **State-mirroring assertion helpers** — a reusable assertion for "these two state-machine instances converge to the same state within N cycles," rather than each feature hand-rolling its own polling/assert loop. Cuts across nearly every integration-style test in this codebase (anything with a master/proxy pair).

6. **Struct/record test-data builders with boundary generation** — helpers to construct wire-format structs/records at boundary sizes (empty, max-count, max-string-length) rather than hand-writing literals per test. Useful generically for any payload/schema test, not just this feature.

7. **Session/generation-counter simulation** — ability to simulate "the same process restarted" (counter bumped) vs. "brief link blip" (counter unchanged) from a test, to exercise divergent reconnect code paths.

## Test-case shapes to design the runner against

Concrete examples the runner needs to make *possible* (not existing tests yet — this feature's tests haven't been built, this repo is framework-only):

- Round-trip a wire payload through a loopback and assert byte-identical reconstruction, including boundary sizes.
- Drive a registration/convergence protocol to completion across many simulated messages, then assert a "synced" barrier flips exactly once and stays latched.
- Push time forward past a watchdog timeout and assert a state machine transitions to a Hold/Error state — and does **not** transition on an assertion that stops just short of the timeout.
- Simulate a value going "stale" (age computed from an echoed timestamp) even though the link is nominally still up, and assert this is distinguishable from a hard drop.
- Simulate reconnect twice — once with counter match, once with mismatch — and assert the two code paths under test diverge exactly where expected (skip-reregistration vs full-reregistration).
- Assert two independent state-machine instances (master + mirrored slave) stay in lock-step across a scripted sequence of commands.

## Testing philosophy to carry over

From the epic's own Testing Decisions section (worth internalizing, since it should generalize to how xStunit tests are written across the framework, not just this feature): assert on the *observable contract* at the highest reasonable seam, not on private internal wiring. If the runner's API design nudges people toward asserting internal state instead of black-box behavior, that's a runner design smell worth fixing early.

## Out of scope for xStunit (per this feature)

- Building the actual remote-pair FBs — that's this repo's job, not the test tool's.
- Any hardware-in-the-loop or real dual-controller commissioning support — this is specifically about the in-runtime loopback/simulation class of test.

## Suggested skills for the xStunit team's next session

- `/to-spec` — if they want this tracked as a real spec/ticket in their own repo's tracker, run it there (don't publish into this repo's `bd`, wrong tracker for a different codebase).
- `/grill-with-docs` — if any of the capability asks above are ambiguous or need scoping decisions before they can ticket them, interview against their own repo's `CONTEXT.md`.
- `/to-tickets` → `/implement` — once scoped, split into tracer-bullet tickets (likely: cycle-stepping primitive → simulated clock → loopback wiring → fault injection → assertion helpers → data builders), each buildable via TDD.
