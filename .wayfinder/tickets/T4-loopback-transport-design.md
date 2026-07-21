---
id: T4
title: In-process transport loopback design
type: grilling
status: open
assignee:
blocked_by: [T1]
---

## Question

Most of the framework's own hand-rolled test scaffolding is a per-feature
version of wiring one FB's transmit buffer directly into another FB's
receive buffer, in-process, with no real socket or second controller. The
handoff calls this the single biggest unblock if TcXunit provides it as a
first-class primitive.

Decide the API/contract shape: what does "wire FB A's TX into FB B's RX"
look like from a test author's perspective, does it require payload structs
(depends on T1, type-system gap prioritization & sequencing, for
`STRUCT`/`ARRAY` support) to already be representable as typed values rather
than raw bytes, and how does the loopback primitive relate to the FB's own
declared transport-buffer variables (does it need a naming/attribute
convention, or generic reflection over `VarDecl`s?). This ticket's
resolution unblocks both fault injection (T5) and session/generation-counter
simulation (T8), which both hook into this same loopback wiring.
