---
id: T7
title: Struct/record test-data builder & boundary-generation design
type: grilling
status: closed
assignee: claude
blocked_by: [T1]
---

## Question

Tests need helpers to construct wire-format structs/records at boundary
sizes (empty, max-count, max-string-length) rather than hand-writing
literals per test, generically for any payload/schema test.

Decide the builder API shape once `STRUCT`/`ARRAY` support lands per T1
(type-system gap prioritization & sequencing): does it introspect a
`STRUCT` `PouAst`'s declared fields to generate boundary variants
automatically, or require a per-struct builder registration? Decide what
"boundary" means per field type (numeric min/max, string max-length,
array empty/max-count) and how a test asks for a specific boundary
combination vs "give me all boundary permutations." Also note in the
resolution whether this shares a serialization/wire-format layer with the
loopback transport (T4, in-process transport loopback design) or is
independent — flagged in this map's "Not yet specified" for now.

## Resolution

**Decision — pure introspection, no per-struct builder registration.** The
ticket's own framing ("generically for any payload/schema test") rules out
hand-written per-struct builders — that's exactly the per-feature scaffolding
the handoff wants eliminated (same rationale T4 used to reject naming-convention
magic). Once `STRUCT` lands (type-system gap prioritization & sequencing item 3)
and has a declared-field AST (a `StructAst`-equivalent to `PouAst`, since
`PouAst` today models FB/program POUs, not `TYPE ... STRUCT` declarations), the
builder walks that field list — types and any declared length/width metadata —
to compute each field's available boundary values automatically. No
registration step, no attribute/pragma.

**Cell shape for `STRUCT` values: reuse `FbInstance.Fields`'s
`Dictionary<string, Cell>` pattern, not a new value type.** An FB instance is
already "a named-field container of `Cell`s" — a `STRUCT` value is the same
shape without the method table. This directly answers the follow-on T4's
resolution flagged ("whoever designs `STRUCT`'s `Cell` shape needs to also
decide what `Transmit` does with it"): because `Dictionary<string, Cell>` is a
mutable reference type, `Transmit`'s current `sink.Value = source.Value` would
alias the two struct instances instead of copying, breaking T4's "copy-based
transport, not `Cell`-aliasing" decision. `STRUCT` payload copy needs an
explicit per-field clone (new `Cell` per field, values copied) — flagging this
as an implementation note for whoever wires `STRUCT` into `Transmit`, not a
new open question.

**Boundary meaning per field type:**

- Numeric fields (`INT`/`DINT`/`REAL`/etc.): the declared type's documented
  min/max (e.g. `INT` -32768/32767). No such bounds table exists yet — today's
  interpreter treats all integers as unchecked C# `int` (`Engine.DefaultValue`
  just returns `0`) — so this needs a small IEC-61131-3 type-bounds lookup as
  implementation groundwork. Not a design gap, just unbuilt yet.
- `STRING` fields: empty string and max-length. Max-length also isn't
  representable today — `VarBlockParser` doesn't parse `STRING(n)` declared
  length — so this boundary category needs that parsing groundwork before the
  builder can compute it. Falls back to TwinCAT's default 80-char length
  when no `(n)` is declared.
- `ARRAY` fields: IEC 61131-3 array bounds (`ARRAY[lo..hi]`) are fixed at
  declaration, not runtime-variable, so there's no runtime "empty" array size
  to generate. Where a wire-format struct pairs a fixed-size buffer with a
  separate count field (a common protocol pattern — the handoff's use case),
  "empty" and "max-count" boundaries apply to that count field, not the array
  itself. The builder cannot discover that count/array pairing by
  introspection alone (nothing marks two fields as related) — this is the one
  boundary category that needs the test author's explicit input, not
  automatic derivation, mirroring T4's stance against inferring relationships
  from naming.

**Combination API: single-field sweep by default, explicit override for
specific combinations.** `Build(structTypeName, params (string field, Boundary)[]
overrides)` — every field starts at a normal/default in-range value; named
overrides push just those fields to a requested boundary. "Give me all
boundary permutations" means one field at a time at its boundary while every
other field stays default (independent sweep), not a full cross-product over
all fields — avoids combinatorial blowup and matches how boundary tests are
normally read (each case isolates one dimension). A test that wants two
fields at their boundaries simultaneously asks for it explicitly via
`overrides`.

**No shared wire-format/serialization layer with the loopback transport.**
T4's resolution already settled `Transmit` as a `Cell`-level copy
(`sink.Value = source.Value`, soon a per-field clone for structs per above),
not a byte-encoding step — there is no serialization boundary in the loopback
path for a builder to share. The builder produces `Cell`-level struct values
directly (a `Dictionary<string, Cell>` matching the field list), the same
representation `Transmit` already copies. `STRUCT`'s 8-byte TwinCAT alignment
(flagged in Type-system gap prioritization & sequencing) only matters if a
real byte-level wire protocol is added later (an actual socket transport
outside the in-process loopback) — out of scope for both T4 and this ticket.
