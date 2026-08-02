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

`ST_BitPacking` and `ST_UnionHolder` are gaps rather than disagreements —
xStunit refuses to size either, and the tests pin the refusal. They are here so
the compiler states an answer that can be read when a model for them is
written.

## What the .tmc cannot settle

One half of the trailing-padding rule stays out of reach: a `.tmc` says how
wide a type is, not whether a packing write touches the padding bytes inside
it. That half is a `MEMCPY` question, and getting it wrong is a buffer overrun
rather than a wrong value — it needs a runtime comparison, not a layout one.

A `.tmc` also records the layout a struct ended up with and never the
`{attribute 'pack_mode'}` pragma that produced it. The two packed fixtures are
therefore only readable as packed if the oracle is given this source alongside
the `.tmc`; measured against the `.tmc` alone they will be compared as if
naturally aligned, and reported as disagreements where xStunit is right.

## Building the golden .tmc

The measurement step needs TwinCAT XAE once:

1. New TwinCAT XAE project, one standard PLC project inside it.
2. Copy every `.TcDUT` here into the project's `DUTs` folder and
   `LayoutChecklistInstances.TcGVL` into `GVLs`, then include them in the
   project so they compile.
3. Build for **x86**, save the generated `.tmc`, then switch the target
   platform to **x64** and build again. The pointer-width rules are only
   visible as the difference between the two.
4. Commit both `.tmc` files here.

The global variable list carries `{attribute 'linkalways'}` and instantiates
every type on purpose: a DUT nothing references can be dropped from the symbol
set, which would quietly remove a checklist rule from the comparison. If a
type is added here, add it to that list too — a test enforces this.
