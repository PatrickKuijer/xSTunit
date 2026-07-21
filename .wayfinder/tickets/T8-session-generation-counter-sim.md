---
id: T8
title: Session/generation-counter simulation design
type: grilling
status: open
assignee:
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
