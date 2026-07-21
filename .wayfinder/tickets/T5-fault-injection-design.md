---
id: T5
title: Fault injection hooks on loopback design
type: grilling
status: closed
assignee: claude
blocked_by: [T4]
---

## Question

Watchdog/resync-class tests (common in any master/slave or redundant-link
design) need mid-test hooks to: drop the link entirely, delay/duplicate/
corrupt one message, freeze one side's transmit, or hold a value "stale but
still connected."

Decide how these fault primitives attach to the loopback wiring designed in
T4 (in-process transport loopback design) — as a decorator/proxy sitting
between the two wired buffers, as flags on the loopback object itself, or
as a separate fault-injection object the test controls independently of the
wiring call? Decide the minimal fault vocabulary (drop / delay / duplicate /
corrupt / freeze-one-side / stale-but-connected) and how "stale but
connected" is distinguished from "hard drop" at the assertion layer, since
the handoff explicitly calls out that distinction as load-bearing for
watchdog tests.

## Resolution

- **Flags/state on the `Loopback` instance itself — no decorator, no
  separate fault-injection object.** T4 already committed to this: "one
  `Loopback` instance = one fixed link... this is where T5's fault-injection
  state... lives." A wrapping decorator/proxy was rejected there because it
  would force a second way to key state to "which pair of fields," duplicating
  the per-feature scaffolding the handoff wants gone. So fault control is a
  set of native methods/properties on the same `Loopback` FB, dispatched
  through the same `NativeMethodBridge` `case` pattern as `Transmit` (new
  cases: `Drop`, `Restore`, `SetDelay`, `Duplicate`, `Corrupt`, `Freeze`,
  `Unfreeze`), backed by the same host class that already holds per-link
  state.
- **Minimal fault vocabulary — one active fault mode at a time, not a
  composable stack.** The handoff's watchdog/resync tests exercise one fault
  condition per scenario (link drops, *or* one message corrupts, *or* the
  link goes stale) — not multiple faults layered simultaneously. Designing
  for composition now is speculative; if a later capability needs to combine
  faults, that's a fresh ticket once a concrete test demands it (noted below
  under Not yet specified). Vocabulary and effect on `Transmit()`:
  - `Drop()` — sets `LinkUp := FALSE`. While set, `Transmit()` is a no-op:
    sink untouched, `LastUpdateTime` does not advance. This is the "hard
    drop" case — the link itself is reported down.
  - `Restore()` — clears `Drop`/`Freeze`, sets `LinkUp := TRUE`. Test calls
    this to simulate reconnection.
  - `Freeze()` — sink stops updating (payload freezes at its current value)
    but `LinkUp` stays `TRUE`. This *is* "stale but still connected": the
    link reports up, the value just stops moving. Distinguishing it from
    `Drop` doesn't need a separate fault type — same "value stops changing"
    mechanism, differing only in whether `LinkUp` flips.
  - `SetDelay(n: INT)` — next `n` `Transmit()` calls enqueue the source value
    into a per-link FIFO instead of copying immediately; the sink receives
    the oldest queued value once the queue reaches depth `n`. Delivers
    "delay" without a scheduler — it rides the test's own `Transmit()` call
    sequence, consistent with T4's "no auto-copy, test author drives every
    call" stance.
  - `Duplicate()` — one-shot flag. On the *next* `Transmit()` call, the sink
    is set to the last successfully transmitted value again (source's
    current value is ignored for that call), simulating a resent/duplicate
    message.
  - `Corrupt(value)` — one-shot flag carrying a test-supplied replacement
    value. On the next `Transmit()` call, the sink is set to `value` instead
    of `source.Value`. (No generic bit-flip/mutation-function primitive —
    ST has no lambdas to express "mutate this payload"; the test just states
    the corrupted value it wants observed, which is sufficient for asserting
    "receiver saw bad data.")
- **Assertion-layer signal for staleness: `LinkUp` and `LastUpdateTime` as
  readable native properties, not internal fields.** Per the map's testing
  philosophy (assert the observable contract, not private wiring), `Loopback`
  exposes:
  - `LinkUp: BOOL` — `FALSE` only under `Drop`; distinguishes hard drop from
    every other fault.
  - `LastUpdateTime` — an absolute snapshot of `Engine.Clock`'s running total
    (T3's simulated clock) taken on every successful `Transmit()` copy.
    Watchdog tests assert elapsed time as `Engine.Clock.Total - fbLink.LastUpdateTime`
    against their timeout threshold, exactly the same clock-delta pattern T3
    established for TON/TOF. This is what makes "stale but connected"
    observable: `LinkUp = TRUE` and payload unchanged, but the elapsed-since-
    update delta exceeds the watchdog threshold — versus hard drop, where
    `LinkUp = FALSE` regardless of elapsed time.
  - These two properties are exposed the same way TON's `Q`/`ET` are exposed
    today (native FB output properties via the existing host-class pattern) —
    no new interpreter mechanism.

## Not yet specified

- Composable/simultaneous fault stacking (e.g. delay *and* corrupt on the
  same link at once) — deliberately out of scope for this ticket; revisit
  only if a concrete capability test needs it.
- `SetDelay`'s FIFO interacting with multi-instance step ordering (the
  group-scheduler deferred in T2/flagged in the map's "Not yet specified") —
  not a problem yet since delay is keyed to `Transmit()` call count on one
  link, not wall-clock or cross-instance ordering, but worth a second look
  once/if the group-step scheduler lands.
