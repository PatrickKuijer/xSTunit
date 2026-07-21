---
id: T4
title: In-process transport loopback design
type: grilling
status: closed
assignee: claude
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

## Resolution

- **Copy-based transport, not Cell-aliasing.** Loopback holds no live
  aliasing between A's TX and B's RX (unlike `REFERENCE TO`/`Pointer`,
  which already alias a `Cell` directly). Instead it's a discrete copy —
  matching a real transport's serialization boundary and giving fault
  injection (T5) an actual call site to intercept.
- **Explicit `Transmit()` call, no auto-copy tied to `StepCycles`.** Same
  philosophy as the scan-cycle-stepping ticket's ordering decision: no
  implicit engine-wide wiring, no scheduler tracking which instances are
  linked. Test author calls `Transmit` explicitly wherever the test needs
  a message to move, exactly like they already control `StepCycles`
  ordering themselves.
- **Field selection via ordinary ST parameter passing — no naming
  convention, no attribute/pragma.** `VarBlockParser` parses no `{attribute}`
  pragmas today, so an attribute-based convention would need new parser
  work; a fixed-name convention (`TX`/`RX`) would be magic and fragile.
  Instead: `Transmit(source, sink)` declares its parameters `REFERENCE TO
  <payload-type>`, and the interpreter's existing `REFERENCE TO` binding
  (`Frame.Ref`) aliases the callee's `Cell` directly to whatever field the
  test author passes — e.g. `fbLink.Transmit(source := fbA.TxBuffer, sink
  := fbB.RxBuffer);`. The test author names real fields in ordinary ST
  syntax; no reflection, no convention on the FB under test.
- **Payload copy is generic now; struct/array copy semantics deferred.**
  `Transmit` does `sink.Value = source.Value` — correct as-is for today's
  scalar `Cell.Value` types. `STRUCT`/`ARRAY` (type-system gap prioritization
  & sequencing's item 3) hasn't landed and hasn't picked a `Cell`-level
  representation yet, so this ticket does not commit to a clone/deep-copy
  contract. Flagged as a follow-on: whoever designs `STRUCT`'s `Cell` shape
  needs to also decide what `Transmit` does with it.
- **Dispatch via the existing native-stub-boundary pattern, not a new call
  mechanism.** Same shape as `TcUnit.FB_TestSuite`/`TcUnitSuiteHost`: a
  native FB type (e.g. `Loopback`) registered in `TypeRegistry`; a test
  suite declares an instance (`fbLink : Loopback;`) and calls
  `fbLink.Transmit(...)` in ST; `NativeMethodBridge` gets a new
  `case "Transmit"` routing to a native host class (parallel to
  `TcUnitSuiteHost`) that performs the copy. No new interpreter mechanism
  needed beyond a `TypeRegistry` entry, a bridge case, and the host class.
- **One `Loopback` instance = one fixed link, not a generic stateless
  call.** Instance holds per-link state going forward — this is where T5's
  fault-injection state (drop flag, delay queue, corruption toggle, "stale
  but connected" marker) lives, keyed to nothing else. Rejected a fully
  generic `Transmit(source, sink)` with no instance identity, since that
  would force T5 to invent its own way to key state to "which pair of
  fields," duplicating the per-feature scaffolding the handoff wants
  eliminated.
