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

One half of the trailing-padding rule stays out of reach: a `.tmc` says how
wide a type is, not whether a packing write touches the padding bytes inside
it. That half is a `MEMCPY` question, and getting it wrong is a buffer overrun
rather than a wrong value — it needs a runtime comparison, not a layout one.

The `{attribute 'pack_mode'}` pragma does survive, as a `pack_mode` property on
the type, so the packed fixtures are measured under the cap the compiler
applied without the oracle ever seeing this source.

## The golden .tmc

`LayoutChecklist.x86.tmc` and `LayoutChecklist.x64.tmc` are one TwinCAT build
of the source in this folder, once per target platform. Two targets is what
makes an address width visible at all: identical source, different bytes.
`LayoutChecklistOracleTests` reads them and needs no TwinCAT.

Regenerating them does. The fixtures are built inside the XAE solution at
`C:\Git\p_twincat_test_project\XAE-TestSolution`, whose PLC project carries
them as `DUTs`/`GVLs` entries, by `build-layout-tmc.ps1` in that repository —
a scripted, headless XAE build over the automation interface that harvests both
platforms back into this folder. It builds only, and never activates a
configuration, so no runtime and no license are involved.

The global variable list carries `{attribute 'linkalways'}` and instantiates
every type on purpose: a DUT nothing references can be dropped from the symbol
set, which would quietly remove a checklist rule from the comparison. If a
type is added here, add it to that list too — a test enforces this.

## What the compiler settled

Every rule above came back as xStunit already had it, over 59 types and 199
members with zero disagreements on the x86 module. On x64 the only
disagreements are address widths, `POINTER TO` and `REFERENCE TO` alike:
xStunit hardcodes four bytes.

One type disagrees on both targets without any rule being at stake.
`PlcTaskSystemInfo` is described with a hole: its declared members stop at byte
32 and `TaskName` is declared at byte 64, the 32 bytes between them being a
reserved array TwinCAT keeps out of the `.tmc`. A member past a hole is
displaced by definition, and the type's size with it, so what those rows measure
is how completely the compiler described the type. `PlcAppSystemInfo` has the
same hole and never reaches it, being refused earlier at `DT` — the abbreviated
spelling of `DATE_AND_TIME`, which xStunit sizes only spelled out.

Two answers are worth stating outright, being the ones a reader is most likely
to guess the other way:

- `LREAL` and `LINT` align to 8 on **both** targets — a 32-bit build does not
  drop them to 4 — and a type ending in a single byte behind them is padded out
  to a multiple of 8.
- `pack_mode` caps a field's alignment rather than flattening it, and does not
  reach into a nested struct type, which keeps its own internal padding.

`U_OverlaidScalars` came back at 8 bytes, every member at offset 0, imposing
that 8-byte alignment on `ST_UnionHolder`, which is 24 bytes with its trailer
at 16 — the answer the union model is now built to, along with the byte a `BIT`
member of a union carries where the same member inside a struct would not.

One fixture stays a gap rather than an agreement — xStunit refuses to size it,
and the compiler's answer is on record for whoever writes the model:

- `ST_BitPacking` — `BIT` members carry sub-byte offsets, at bits 0 and 1, with
  the guard byte at bit 8 and the type 2 bytes wide.
