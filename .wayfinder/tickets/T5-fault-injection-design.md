---
id: T5
title: Fault injection hooks on loopback design
type: grilling
status: open
assignee:
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
