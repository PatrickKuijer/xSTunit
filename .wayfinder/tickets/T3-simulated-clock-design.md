---
id: T3
title: Simulated clock for TON/TOF/FB_Pulse design
type: grilling
status: open
assignee:
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
