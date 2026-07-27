# Array indexing, pointer arithmetic, MEMCPY/MEMSET/MEMMOVE

Status: **built**. Source: `Engine.cs` (`EvaluateBinary`/
`EvaluatePointerArithmetic`, `MemCopy`/`MemSet`, index-expression
handling), `ArrayIndexingTests.cs`, `PointerArithmeticTests.cs` in
`tests/TcXunit.Interpreter.Tests`. Landed as one arc: array indexing
(`TcXunit-sej.1`) -> pointer arithmetic (`TcXunit-sej.2`) ->
MEMCPY/MEMSET/MEMMOVE (`TcXunit-sej.3`), each a prerequisite for the
next.

## Array indexing

`arr[i]` / `arr[i,j]` read and write, honoring the array's declared
lower bound (not always 0):

```
buf[2] := 99;
grid[2,1] := 42;
```

Out-of-bounds read/write throws `IndexOutOfRangeException`.

## Pointer arithmetic: `ADR(x) +/- offset`

```
p := ADR(buf) + 2;
p^ := 99;      (* writes through to buf *)
q := p - 1;
```

- `ADR(arr)` decays to a pointer at element 0; `ADR(arr[i])` points at
  element `i` directly — either supports further `+`/`-` offset.
- Offset moves in whole array elements, not raw bytes — correct as
  literal byte arithmetic when the pointee is a `BYTE`/`SINT`/`USINT`
  array (the buffer-packing case `MEMCPY`/`MEMSET`/`MEMMOVE` exist for);
  an approximation for wider element types.
- Only pointers whose target is an array element support arithmetic — a
  pointer to a scalar or a whole `STRUCT` throws `NotSupportedException`
  (`ADR(count) + 1` where `count : INT` is unsupported; there's no
  byte-level `STRUCT` layout model).
- Moving the index out of `[0..Elements.Length-1]` throws
  `IndexOutOfRangeException`.
- Pointer-minus-pointer is not supported.

## MEMCPY / MEMSET / MEMMOVE

```
MEMCPY(destAddr := ADR(dest), srcAddr := ADR(src), n := 4);
MEMMOVE(destAddr := ADR(buf) + 1, srcAddr := ADR(buf), n := 3);
MEMSET(destAddr := ADR(buf), value := 0, n := 4);
```

- `dest`/`src` targeting an array element (`ADR(buf)` or `ADR(buf[i])`)
  address into the array's own backing storage directly.
- `dest`/`src` targeting a plain scalar or `STRUCT`/struct-field `Cell`
  (`ADR(scalarVar)` or `ADR(structVar.field)`, `TcXunit-4vn`) are also
  supported: the `Cell`'s current value is packed into a same-size byte
  buffer (natural-alignment layout, reusing the `SIZEOF` byte-size math),
  copied into/out of like a real array, then unpacked back into the
  `Cell` once the copy completes (dest only — src is read-only). A
  pointer with neither shape (e.g. targeting a `Cell` with no declared
  type) throws `InvalidOperationException` (wrong argument type) or
  `NotSupportedException` (byte-addressing not modeled for that target).
- `n` counts elements/bytes, which equals bytes for a `BYTE`/`SINT`/
  `USINT`-element array (the buffer-packing case these intrinsics exist
  for) or for the byte-serialized size of a scalar/`STRUCT` target.
  Negative `n` throws `ArgumentOutOfRangeException`.
- `MEMCPY` copies forward regardless of overlap, same as the C intrinsic
  it mirrors — an overlapping forward copy can clobber source elements
  before they're read.
- `MEMMOVE` detects a forward overlap (dest inside `[src, src+n)` on the
  *same backing array*) and copies backward instead, so "shift buffer
  down after consuming its head" doesn't clobber unread source elements.
  Non-overlapping or backward-overlapping copies behave like `MEMCPY`.
- `MEMSET` writes `value & 0xFF` (low byte only) to `n` elements starting
  at `dest`.
- Out-of-range access throws naturally via the backing array's indexer.
