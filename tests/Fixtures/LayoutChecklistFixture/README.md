# Layout checklist fixtures

ST source for the Grade A layout oracle. One construct per byte-layout rule
xStunit's `TypeLayout` claims to implement, each shaped so that reading the
rule the other way moves a member's offset or a type's size — a fixture that
lands on the same bytes under both readings measures nothing.

Nothing here needs TwinCAT to author or review.
`LayoutChecklistFixtureTests` pins what xStunit's own math makes of every
fixture, and says in prose what the rival reading would have produced. The
compiler's answer arrives separately, as a `.tmc` built from this same source
and diffed against those numbers by `LayoutOracle`.

## Rule to fixture

| Checklist rule | Fixture | What moves if the rule is wrong |
| --- | --- | --- |
| Pointer width | `ST_PointerWidth` | `trailer` at 8 (32-bit) or 16 (64-bit) |
| `REFERENCE TO` width | `ST_ReferenceWidth` | `trailer` at 8, not the 4 an INT referent would give |
| 8-byte scalar alignment | `ST_WideAlignment` | `wideFloat` at 8, not 4 |
| Trailing padding in a type's size | `ST_WideAlignment`, `ST_TrailingPadding` | sizes 32 and 8, not 25 and 5 |
| `pack_mode` caps alignment | `ST_PackedToTwo` | `wide` at 2 — not 4 (pragma ignored), not 1 (read as byte-packing) |
| Explicit `pack_mode` `'0'` packs like `'1'` | `ST_PackedToZero` | `wide` at 1 — not 4 (read as no pragma) |
| `pack_mode` does not inherit into a nested type | `ST_NestedNatural` + `ST_PackedOuter` | `trailer` at 9, not 6 |
| Array-of-struct stride | `ST_ArrayStride` | `sentinel` at 24, not 15 |
| `STRING(n)` / `WSTRING(n)` size and alignment | `ST_StringSizes` | `guard` at 6, `wide` at 8 |
| Enum base width | `E_DefaultBase`, `E_ByteBase`, `E_DintBase` + `ST_EnumWidths` | `guardOne` at 1, `guardTwo` at 4 |
| `BOOL` is a byte | `ST_BoolWidth` | `guard` at 2, not 1 |
| `BIT` members carry sub-byte offsets | `ST_BitPacking` | same shape as `ST_BoolWidth`, declared in `BIT` |
| Union layout | `U_OverlaidScalars` + `ST_UnionHolder` | every member at offset 0, size 8; `trailer` at 16 reports the alignment it imposes |

`ST_BitPacking` is a gap rather than a disagreement — xStunit refuses to size a
`BIT` member of a struct, and the tests pin the refusal. It is here so the
compiler states an answer that can be read when a model for it is written.

## What the .tmc cannot settle

A `.tmc` says how wide a type is, not which bytes a packing write lands on, so
no golden `.tmc` can close the second half of the trailing-padding rule: whether
a `MEMCPY` of `SIZEOF` bytes touches the padding inside a type, and with what.
Getting that wrong is a buffer overrun rather than a wrong value, so the row is
answered here — from measurement inside xStunit, plus one named carve-out —
rather than left open.

`LayoutChecklistPackWriteTests` pins pack and `SIZEOF` in agreement on all three
trailing-padding fixtures, on both targets: a write of `ST_TrailingPadding`,
`ST_WideAlignment` or `ST_ArrayStride` fills exactly the 8, 32 and 28 bytes
`SIZEOF` claims, and a poisoned destination keeps its poison from there on.
xStunit's padding is zero by construction rather than by luck — the pack buffer
is a zero-filled array and `PackValue` only ever writes at a field placement,
never at a padding offset — so a whole-type copy zeroes the destination's
padding instead of carrying the source's across. The target agrees for any
struct that has only ever been written field-wise, because CODESYS
zero-initialises variable memory.

That leaves one divergence, and it is nameable: a source struct whose padding is
*non-zero*. On the target that is reachable through a `MEMSET` over the struct, a
union overlay, a pointer write, or a struct filled from a fieldbus buffer, and a
`MEMCPY` there copies those padding bytes verbatim; in xStunit the whole class is
unreachable, there being no storage for a padding byte to hold anything but a
zero. Probing that class is what a Grade B runtime comparison still owes this
rule — narrowed from checking `MEMCPY` at large. That CODESYS `MEMCPY` copies
source padding verbatim is asserted from the specification and not measured on a
box, so the Grade B trial confirms it rather than assumes it.

The `{attribute 'pack_mode'}` pragma does survive, as a `pack_mode` property on
the type, so the packed fixtures are measured under the cap the compiler
applied without the oracle having to read the pragma out of this source.

One thing the `.tmc` deliberately does not settle is which of these types is a
`UNION`. It marks a union no differently from a struct — shared offsets are the
only tell, and those are the outcome the layout math exists to predict, so the
oracle would be scoring its own answer. `LayoutChecklistOracleTests` hands the
oracle the `.TcDUT` declarations in this folder instead and lets
`StructDeclParser` decide, which is what puts xStunit's own `UNION` recognition
inside the conformance run.

## The golden .tmc

`LayoutChecklist.x86.tmc` and `LayoutChecklist.x64.tmc` are one TwinCAT build
of the source in this folder, once per target platform. Two targets is what
makes an address width visible at all: identical source, different bytes.
`LayoutChecklistOracleTests` reads them and needs no TwinCAT.

Regenerating them does. The fixtures are built inside the XAE solution at
`C:\Git\p_twincat_test_project\TestSolution`, whose `PLC1` project carries
them as `DUTs`/`GVLs` entries alongside a TcUnit reference. A build under the
`TwinCAT RT (x86)` solution platform and another under `TwinCAT RT (x64)` each
leave a `PLC1.tmc` behind, which is copied here as the matching file. It builds
only, and never activates a configuration, so no runtime and no license are
involved. After replacing them, the two `.diff.txt` files are rewritten from
`LayoutReport.ToText()` so `Compare_ReproducesTheCommittedDiff` pins the new
build.

The global variable list carries `{attribute 'linkalways'}` and instantiates
every type on purpose: a DUT nothing references can be dropped from the symbol
set, which would quietly remove a checklist rule from the comparison. If a
type is added here, add it to that list too — a test enforces this.

## What the compiler settled

Every rule above came back as xStunit already had it, over 60 types and 212
members with zero disagreements, on the x64 module as much as the x86 one.
Addresses are sized from the target rather than hardcoded, `POINTER TO` and
`REFERENCE TO` alike, so the 64-bit module's wider addresses and everything they
displace land where the compiler put them.

One type is described with a hole rather than compared to the end.
`PlcTaskSystemInfo`'s declared members stop at byte 32 and `TaskName` is
declared at byte 64, the 32 bytes between them being a reserved array TwinCAT
keeps out of the `.tmc`. A member past a hole is displaced by definition, and
the type's size with it, so the oracle stops at `TaskName` and reports it as
`NotCompared`. The eleven members in front of the hole are compared like any
others — a hole costs the rows it displaces and no more. `PlcAppSystemInfo` has
the same hole and never reaches it, being refused earlier at `TComSrvPtr`: an
`ITComObjectServer`, a COM interface pointer the `.tmc` declares 32 bits wide on
x86 and 64 on x64, named as a plain type rather than marked as a pointer.

Two answers are worth stating outright, being the ones a reader is most likely
to guess the other way:

- `LREAL` and `LINT` align to 8 on **both** targets — a 32-bit build does not
  drop them to 4 — and a type ending in a single byte behind them is padded out
  to a multiple of 8.
- `pack_mode` caps a field's alignment rather than flattening it, and does not
  reach into a nested struct type, which keeps its own internal padding. An
  explicit `'0'` is not "no cap": it packs without gaps, exactly like `'1'`.

`U_OverlaidScalars` came back at 8 bytes, every member at offset 0, imposing
that 8-byte alignment on `ST_UnionHolder`, which is 24 bytes with its trailer
at 16 — the answer the union model is now built to, along with the byte a `BIT`
member of a union carries where the same member inside a struct would not.

One fixture stays a gap rather than an agreement — xStunit refuses to size it,
and the compiler's answer is on record for whoever writes the model:

- `ST_BitPacking` — `BIT` members carry sub-byte offsets, at bits 0 and 1, with
  the guard byte at bit 8 and the type 2 bytes wide.
