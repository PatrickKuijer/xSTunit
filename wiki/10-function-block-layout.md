# FUNCTION_BLOCK byte size (`SIZEOF`)

`SIZEOF(FB_Type)` and `SIZEOF(fbInstance)` return the same number: the byte
size of the instance data on the selected `--target` (`x86` or `x64`). The
rules live in `TypeLayout.SizeOfFunctionBlock` and `TypeLayout.FunctionBlockFields`.

## Size rules

- **Vtable pointer first.** An FB instance starts with a hidden pointer to its
  virtual method table, one address wide (4 bytes on `x86`, 8 on `x64`). It
  counts once per instance, however deep the `EXTENDS` chain.
- **One more pointer for one implemented interface.** An FB with a single
  `IMPLEMENTS` interface carries a second hidden address-wide pointer directly
  after the vtable pointer, so its first member sits at byte 8 on `x86` and
  byte 16 on `x64`. A member-less FB implementing an interface is two addresses
  wide.
- **Members are flattened in declaration order, base type first.** Inherited
  members precede the derived type's own, sections (`VAR_INPUT`, `VAR_OUTPUT`,
  `VAR`) in the order declared within each type. The whole chain is laid out
  as one sequence, so a derived member can land in the padding at the end of
  the base's members.
- **`VAR CONSTANT` is counted** like any other member (see Evidence below).
  Top-level `VAR_TEMP` is not.
- **Natural alignment.** Each member is aligned to its own alignment (scalar:
  its size; `STRUCT`/`ARRAY`: as `SIZEOF` of that type; address: address width;
  a nested FB: address width). `LREAL` aligns to 8 on `x86` too. The instance
  size is padded to its largest alignment, never less than the address width.
- **`VAR_IN_OUT` is an address.** It is stored as a reference, so it costs one
  address regardless of the referenced type.
- **Nested FBs** are sized recursively, including their own header.

`SIZEOF(FB_Type)` is legal in a `VAR CONSTANT` initializer inside `FB_Type`
itself (`SETTING_SIZE : UDINT := SIZEOF(FB_GadgetSetting);`). The size comes
from the declarations, not from an instance, so it does not recurse into
construction.

## Evidence

The `.tmc` layout oracle (`tests/Fixtures/LayoutOracleFixture`,
`tests/Fixtures/LayoutChecklistFixture`) scores every function block inside the
model, on both targets, with zero disagreements; each fixture's committed
`.diff.txt` lists what it skips and why. What that does and does not verify:

- **Verified:** the vtable pointer at offset 0, the extra pointer for one
  implemented interface, size padded to at least the address width, nested FB
  alignment, `LREAL` alignment on `x86`, and declaration order across
  `VAR_INPUT`, `VAR_OUTPUT` and `VAR`.
- **Not verified by the oracle:** which sections count. That constants count
  is an inference from TcUnit member names that appear at instance offsets in
  the `.tmc`. `VAR_TEMP`, `VAR_IN_OUT` and `EXTENDS` are not covered by any
  scored block.

## Not modelled

Refused with a `NotSupportedException` naming the FB, rather than sized by
guesswork:

- FBs implementing more than one interface. No compiler output in the fixtures
  shows how the pointers are laid out.
- Members typed as an interface (`i : I_Foo`), and a base type or member that is
  a native library FB (`TON`, `R_TRIG`, ...) or otherwise not a loaded FB.
  `EXTENDS` naming an unloaded type is an error, not a silently smaller size.
- An FB that contains itself, or an `EXTENDS` chain that loops.
- `SIZEOF` of a `PROGRAM` or `FUNCTION` name: only a `FUNCTION_BLOCK` has an
  instance size.
- **Method `VAR_INST` cells.** TwinCAT stores them in the instance, at its tail
  (compiler-generated members named `__<FB>__<METHOD>__<VAR>`, sometimes also
  flagged `implicit_inst_var`). xStunit does not count them, so `SIZEOF` of a
  block whose own or inherited methods declare `VAR_INST` is refused, naming
  the method. Lifting the refusal is part of bead `xstunit-2o9.48`. The oracle
  skips any block carrying such a cell, recognised by either signal.

Silently ignored, not refused:

- `{attribute 'pack_mode'}` on an FB.

## Unverified

- **Tail-padding reuse across `EXTENDS`.** Because the chain is flattened, a
  derived member that fits in the padding after the base's last member is placed
  there: a base holding one `DINT` and a derived FB holding one `BYTE` give 16
  bytes on `x64`. If TwinCAT lays the derived part out after the whole base
  instance instead, the answer would be 24. No fixture has an FB with an
  `EXTENDS` clause, so this is not checked; the inheritance unit test pins the
  current behaviour only (bead `xstunit-xdbw`).
- Where an interface pointer sits when the interface is implemented by a
  derived FB rather than the root of the chain.
