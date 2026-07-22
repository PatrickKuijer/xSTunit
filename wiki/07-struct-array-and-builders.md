# STRUCT/ARRAY types + struct boundary builder

Status: **built**. `TcXunit-w5x.15.6` (STRUCT/ARRAY type) and
`TcXunit-w5x.15.10` (boundary builder + `Transmit` struct clone) both
landed. Source: `StructDeclParser.cs`, `ArrayTypeInfo.cs`,
`StructBoundaryBuilder.cs`, `IecNumericBounds.cs`, `StringTypeInfo.cs`,
`CellCloner.cs` in `src/TcXunit.Interpreter`.

## STRUCT/ARRAY (15.6)

```
TYPE ST_Payload :
STRUCT
    Id      : UDINT;
    Value   : REAL;
    Flags   : ARRAY[0..3] OF BOOL;
END_STRUCT
END_TYPE
```

- Struct literal: `(Id := 1, Value := 3.5)` — evaluates to a
  `StructInstance`; unlisted fields keep their type default.
- Array literal: `[v0, v1, ...]`, with `[n(v)]` repeat shorthand for `n`
  copies of `v` (e.g. `[2(10), 2(20)]` -> `[10, 10, 20, 20]`). Array
  literals used as a `VAR` default overlay the given values and leave the
  rest at the element type's default (`buf : ARRAY[1..4] OF INT :=
  [10, 20];` -> `[10, 20, 0, 0]`).
- Multi-dim: `ARRAY[1..2,1..3] OF INT` — indexed `grid[2,1]`, flattened
  row-major internally.
- Field/element access: `pos.x`, `pos.x := 7`, `buf[2]`, `buf[2] := 99`,
  `grid[2,1] := 42` — both read and write, including nested (`ARRAY OF
  ST_Point` defaults each element to the struct's own default).
- Runtime representation: a struct value is a `StructInstance` wrapping a
  `Dictionary<string, Cell>` mirroring `FbInstance.Fields` — same
  field-access pattern as an FB instance, just without a method table. An
  array value is an `ArrayValue` with `Dimensions` + a flat `Elements[]`.

## Struct boundary builder (15.10)

```csharp
var builder = new StructBoundaryBuilder(registry);
var instance = builder.Build("ST_Payload", (Field: "Id", Boundary: Boundary.Max));
```

Not exposed as an ST-callable native method yet — it's a C#-side helper
(`StructBoundaryBuilder.Build`) for building test-data `StructInstance`s
from a struct type name plus field overrides.

- Pure introspection over the `STRUCT` type's declared fields via
  `TypeRegistry.GetStruct` — no per-struct registration.
- Every field defaults to an in-range value (`InRangeDefault`); only
  fields named in `overrides` get pushed to `Boundary.Min`/`Boundary.Max`.
  Single-field sweep is the default; simultaneous multi-field boundaries
  are explicit opt-in via multiple `overrides` entries.
- Boundary meaning per field kind:
  - Numeric: IEC 61131-3 documented min/max for the declared type, from
    `IecNumericBounds` (`SINT`/`USINT`/`BYTE`/`INT`/`UINT`/`WORD`/`DINT`
    as C# `int`; `UDINT`/`DWORD`/`LINT`/`ULINT`/`LWORD`/`REAL`/`LREAL` as
    their natural wider CLR type).
  - `STRING`: `Boundary.Min` -> `""`; `Boundary.Max` -> a string of the
    declared `STRING(n)` length (or TwinCAT's 80-char default when
    undeclared), via `StringTypeInfo.ParseLength`.
  - Nested `STRUCT`/`ARRAY` fields and unnamed fields recurse into their
    own in-range default — arrays fill every element at its element
    type's in-range default.
  - `ARRAY` fields themselves have no scalar boundary (fixed size at
    declaration) — targeting an array field in `overrides` throws
    `NotSupportedException`. Target a paired count/length field instead
    if your struct models that pattern explicitly.
- Unknown struct type or unknown field name in `overrides` throws
  `InvalidOperationException`.

## Transmit struct clone (also 15.10)

[`Loopback.Transmit`](04-loopback-and-faults.md) does a per-field clone
(`CellCloner`) for `STRUCT`/`ARRAY` payloads — new `Cell`s, values
copied — rather than aliasing the source's backing dictionary/array, so
the loopback's copy-not-alias contract holds for struct- and
array-typed fields too, not just scalars.
