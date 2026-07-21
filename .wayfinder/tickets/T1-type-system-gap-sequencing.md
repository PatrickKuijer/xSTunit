---
id: T1
title: Type-system gap prioritization & sequencing
type: research
status: closed
assignee: wayfinder-charting-session
blocked_by: []
---

## Question

The interpreter today only really supports `INT`-shaped whole numbers, `BOOL`,
`STRING` literals, and FB instances (see `Engine.EvaluateBinary`'s hard cast
to `(int)`, `Lexer`'s digit-only/no-float literal support, and
`VarBlockParser`'s regex which explicitly rejects `ARRAY`/`STRUCT`/comma-list
declarations). Several handoff capabilities need types this interpreter
doesn't have yet: `TIME` (simulated clock / TON/TOF), `STRUCT`/`ARRAY`
(wire-format payload structs, boundary test-data builders), and likely
`REAL`/`LREAL` and boolean/arithmetic operators (`AND OR NOT MOD * /`) as
general groundwork.

Decide: what is the minimal ordered set of type-system additions needed to
unblock the capability tickets (simulated clock, loopback, struct builders),
and in what order should they land? For each type/operator addition, note
which capability ticket(s) it unblocks, and flag any that turn out to need
more than a "grow-on-demand" addition to `Lexer`/`Expr`/`Engine.EvaluateBinary`/
`Engine.DefaultValue`/`VarBlockParser` (i.e. would need a Cell/value-model
rework) rather than a narrow extension.

Use `/research` if reference facts about IEC 61131-3 `TIME`/`STRUCT` literal
syntax or arithmetic promotion rules are needed to make this call
confidently, rather than guessing at TwinCAT's actual grammar.

## Resolution

Full findings (cited against Beckhoff InfoSys, the primary first-party
source for the TwinCAT ST dialect): [T1-iec61131-types-research.md](../research/T1-iec61131-types-research.md).

**Decision — ordered sequence:**

1. **`REAL`/`LREAL` first.** IEEE 754 single/double, decimal/exponent
   literals, map 1:1 to C# `float`/`double`. Smaller→larger conversions
   (`INT`→`REAL`, `REAL`→`LREAL`) are implicit in TwinCAT; larger→smaller
   needs explicit `X_TO_Y` casts. `MOD` is integer-only; `AND/OR/XOR/NOT`
   are documented as bitstring operators, not real-valued. Cheapest,
   self-contained addition — no dependency on anything else. Unblocks
   general arithmetic groundwork and is a prerequisite field type for
   struct payloads (item 3).
2. **`TIME` literal + elapsed-delta clock model second.** `T#`/`TIME#`
   grammar is fixed-order `d h m s ms` (`LTIME#` adds `us`/`ns`), `TIME` is
   a plain 32-bit ms unsigned value (`LTIME` 64-bit ns) — no exotic
   encoding, trivial to represent as a C# `int`/`long` under the hood. TON
   confirmed to consume `PT` as scan-cycle-accumulated elapsed time since
   the `IN` rising edge (`ET`), **not** an absolute clock read — so the
   simulated-clock ticket (Simulated clock for TON/TOF/FB_Pulse design)
   only needs to feed a per-call time delta, not model wall-clock time at
   all. Depends on nothing new beyond a literal/value addition; unblocks
   the simulated-clock ticket and the scan-cycle-stepping ticket's "advance
   N cycles at simulated Δt" composition.
3. **`STRUCT`/`ARRAY` last.** Grammar confirmed: `TYPE ... STRUCT ...
   END_STRUCT END_TYPE`, `ARRAY[lo..hi] OF type` (multi-dim supported),
   struct literal `(field := val, ...)`, array literal `[v0, v1, ...]` with
   repeat shorthand `[2(10)]`. Structs use **8-byte alignment since
   TwinCAT 3** — Beckhoff explicitly flags this as relevant when exchanging
   a struct "as a complete memory block with other controllers," which is
   exactly the loopback-transport use case, so the loopback ticket's wire
   serialization must account for alignment padding, not tight-pack fields.
   This is the largest parser/`VarBlockParser`/`Cell`-model change (comma
   lists, nested type declarations, initializer parsing) and benefits from
   `REAL`/`TIME` already being valid field types first. Unblocks the
   loopback-transport ticket and the struct-boundary-builder ticket.

**Flag:** none of the three additions look like they *require* a
Cell/value-model rework beyond the existing "grow-on-demand" pattern
(`Lexer`/`Expr`/`Engine.EvaluateBinary`/`Engine.DefaultValue`/
`VarBlockParser`) — `REAL`/`TIME` are just new literal/value C# primitive
types; `STRUCT`/`ARRAY` is the one with real complexity (alignment-aware
serialization, nested initializers, comma-list parsing) but doesn't force
replacing the plain-object `Cell.Value` design.
