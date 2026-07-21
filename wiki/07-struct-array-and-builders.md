# STRUCT/ARRAY types + struct boundary builder

Status: **pending** — `TcXunit-w5x.15.6` (STRUCT/ARRAY type) and
`TcXunit-w5x.15.10` (boundary builder + `Transmit` struct clone) are both
open. `.15.10` is blocked on `.15.6`. Neither exists in the interpreter
today (no `TYPE ... STRUCT ... END_STRUCT`, no `ARRAY[..]`, no struct
literals) — fixtures needing multi-field wire payloads can't be written
yet. This page documents the design so you can plan fixture shape now.

## STRUCT/ARRAY (15.6)

Intended grammar:

```
TYPE ST_Payload :
STRUCT
    Id      : UDINT;
    Value   : REAL;
    Flags   : ARRAY[0..3] OF BOOL;
END_STRUCT
END_TYPE
```

Struct literal: `(Id := 1, Value := 3.5, Flags := [TRUE, FALSE, FALSE, FALSE])`.
Array literal: `[v0, v1, ...]`, with `[n(v)]` repeat shorthand for `n`
copies of `v`.

Runtime representation: a struct value is a `Dictionary<string, Cell>`
mirroring `FbInstance.Fields` — same field-access pattern as an FB
instance, just without a method table.

## Struct boundary builder (15.10)

Intended shape:

```
Build(structTypeName, overrides);
```

- Pure introspection over the `STRUCT` type's declared fields — no
  per-struct registration.
- Every field defaults to an in-range value; only fields named in
  `overrides` get pushed to a boundary value. Single-field sweep is the
  default; simultaneous multi-field boundaries are explicit opt-in via
  `overrides`.
- Boundary meaning per field kind:
  - Numeric: IEC 61131-3 documented min/max for the declared type.
  - `STRING`: empty and max-length (`STRING(n)` declared length, or
    TwinCAT's 80-char default when undeclared).
  - `ARRAY`: no runtime-variable size in IEC 61131-3, so "empty"/"max
    count" boundaries only apply to an *explicit* array/count-field
    pairing you declare yourself — never inferred from naming.

## Transmit struct clone (also 15.10)

Today's [`Loopback.Transmit`](04-loopback-and-faults.md) does
`sink.Value = source.Value` — fine for scalars, but that would alias a
struct's backing dictionary rather than copy it. Once 15.10 lands,
`Transmit` switches to a per-field clone for `STRUCT` payloads (new `Cell`
per field, values copied) to preserve the loopback's copy-not-alias
contract. Don't transmit struct-typed fields through `Loopback` until this
ticket closes — scalar fields work today, struct fields don't yet.

## Until these land

Wire-payload fixtures are limited to individual scalar fields per
`Transmit` call (as in `LoopbackFaultTests`'s `FB_Payload { Buffer : INT }`
example) — model a payload as several scalar `VAR` fields plus several
`Transmit` calls rather than one struct field, until 15.6/15.10 close.
