---
id: T8
title: Session/generation-counter simulation design
type: grilling
status: closed
assignee: claude
blocked_by: [T4]
---

## Question

Tests need to simulate "the same process restarted" (counter bumped) vs.
"brief link blip" (counter unchanged) to exercise divergent reconnect code
paths (e.g. skip-reregistration vs full-reregistration).

Decide how this composes with the loopback design from T4 (in-process
transport loopback design): is the generation/session counter a field the
test sets directly on the FB instance before re-establishing the loopback,
or does the loopback/fault-injection layer (T5, fault injection hooks on
loopback design) need an explicit "reconnect with counter bump" vs
"reconnect same session" operation as part of its fault vocabulary? Decide
what the test asserts to confirm the two code paths actually diverged
where expected, consistent with this map's "assert observable contract"
philosophy.

## Resolution

- **Counter is an ordinary field on the FB under test, not Loopback/fault-
  injection state.** T4 draws the line at the transport boundary — `Loopback`
  holds per-*link* state (drop flag, delay queue, `LinkUp`, `LastUpdateTime`).
  A session/generation counter is per-*protocol-endpoint* state belonging to
  whichever FB implements the reconnect logic (the multi-controller
  framework's own FB, out of scope per this map's "Out of scope" — that's
  `b_beckhoff_framework/PLC`'s FB, TcXunit only has to make it testable). No
  new native concept needed: it's a `VAR` the test author declares on the FB
  same as any other field, set directly via ordinary ST assignment
  (`fbA.Generation := fbA.Generation + 1;`) before re-establishing the link —
  consistent with T4's "no naming/attribute convention, ordinary ST field
  access" stance and T2/T4's general pattern of the test author driving
  every state transition explicitly rather than the harness inferring intent.
- **No new fault-vocabulary primitive on `Loopback`.** T5's vocabulary
  (`Drop`/`Restore`/`Freeze`/`SetDelay`/`Duplicate`/`Corrupt`) already covers
  "link goes down, link comes back" as a transport-level fact independent of
  what the endpoint FB does with it. The two scenarios compose from existing
  primitives instead of needing a `ReconnectSameSession`/`ReconnectNewSession`
  case added to `NativeMethodBridge`:
  - **Brief link blip** (counter unchanged): `fbLink.Drop(); ... fbLink.Restore();`
    — test never touches the counter field.
  - **Same process restarted** (counter bumped): same `Drop()`/`Restore()`
    pair, plus the test sets the counter field directly on the FB instance
    between them (mirroring a real restart re-initializing that VAR).
  Adding a dedicated Loopback operation for this would duplicate state the
  FB under test already owns, which is exactly the per-feature scaffolding
  duplication T4's design was chosen to eliminate.
- **What the test asserts**: per the map's "assert the observable contract"
  philosophy, the test does not assert on the counter field itself or on any
  internal loopback wiring — it asserts on whatever *output* the FB under
  test's own reconnect logic produces to signal which path it took (e.g. a
  `ReregistrationCount`/`Status` output field that increments only on full
  reregistration). TcXunit's job stops at making the two preconditions
  (`Drop`/`Restore` with vs. without a counter bump) reliably reproducible;
  distinguishing the two code paths at the assertion layer is a property of
  the FB under test's own observable outputs, not a new TcXunit primitive.
- **Nothing new needed from T1/T3.** The counter is a plain integer field —
  no new `Cell` type, no clock interaction. If a future capability needs the
  counter to be part of a `STRUCT` payload transmitted over the link, that's
  covered by T7's per-field struct copy, not a T8 concern.
