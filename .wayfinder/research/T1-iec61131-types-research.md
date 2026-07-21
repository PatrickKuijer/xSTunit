# T1 — IEC 61131-3 / TwinCAT Type System Research

Research for deciding the minimal, ordered set of type-system additions to
`src/TcXunit.Interpreter` needed to unblock: (a) a simulated clock for
TON/TOF/TP timers, (b) in-process transport loopback of wire-format payload
structs, (c) struct/record boundary test-data builders.

Primary source: Beckhoff TwinCAT 3 "Beckhoff Information System" (InfoSys),
the official Beckhoff/TwinCAT documentation portal (infosys.beckhoff.com).
The full IEC 61131-3 standard text is a paid IEC/ISO document not available
for direct citation here; Beckhoff's InfoSys pages are the primary,
first-party documentation of record for the TwinCAT ST dialect this
interpreter targets, and are used as the citation source throughout.

---

## 1. TIME literal syntax, representation, and TON/TOF/TP consumption

**Literal grammar (`T#...`, exact BNF from Beckhoff docs):**

```
<time keyword> # <length of time>
<time keyword>   : TIME | time | T | t
<length of time> : ( <number of days>d )? ( <number of hours>h )?
                    ( <number of minutes>m )? ( <number of seconds>s )?
                    ( <number of milliseconds>ms )?
```

- The order of units is fixed (d, h, m, s, ms) — you may omit units but not
  reorder them, and overflow in the *highest* used unit is allowed (e.g.
  `T#100s12ms` is legal) but overflow in a *lower* unit is not (e.g.
  `T#5m68s` is illegal because 68s > 59s).
- `T#` is mandatory; a bare `15ms` is not a valid TIME literal.
  (Source: [TIME/LTIME constants](https://infosys.beckhoff.com/content/1033/tc3_plc_intro/2529294987.html))
- `LTIME#...` extends the same grammar with additional `us` (microseconds)
  and `ns` (nanoseconds) suffixes after `ms`, e.g.
  `LTIME#1000d15h23m12s34ms2us44ns`.
  (Source: same page as above)

**Underlying representation:**

- `TIME` is handled internally like `UDINT` — a **32-bit** unsigned value,
  resolution **milliseconds**, range `0 .. 4294967295` ms
  (≈ 49 days 17h 2m 47s 295ms).
- `LTIME` is handled internally like `ULINT` — a **64-bit** unsigned value,
  resolution **nanoseconds**.
  (Source: [TIME/LTIME](https://infosys.beckhoff.com/content/1033/tc3_plc_intro/12189021579.html))
- For comparison, `DATE`/`DATE_AND_TIME`/`TIME_OF_DAY` are also handled as
  32-bit (`UDINT`, seconds/milliseconds resolution) and their 64-bit
  counterparts `LDATE`/`LDATE_AND_TIME`/`LTIME_OF_DAY` as `ULINT` with
  nanosecond resolution — same pattern as TIME/LTIME, requiring TwinCAT
  ≥ 3.1.4026.0.
  (Source: [Date and time data types](https://infosys.beckhoff.com/content/1033/tc3_plc_intro/2529415819.html))

**How TON consumes PT (elapsed-since-last-edge, not absolute clock read):**

The Beckhoff `TON` (Tc2_Standard library) documentation states verbatim:

> "If IN = FALSE, the Q output has the value FALSE and the ET output has the
> value zero. As soon as IN is set to TRUE, the time in milliseconds is
> **counted up** in ET until the setpoint PT is reached. Q is TRUE, if
> IN = TRUE and ET = PT."
>
> ET: "Elapsed time **since the rising edge** at the IN input."

(Source: [TON — Tc2_Standard](https://infosys.beckhoff.com/content/1033/tcplclib_tc2_standard/74406539.html))

This confirms `ET` is accumulated per PLC scan cycle relative to the edge on
`IN` — the function block internally tracks elapsed time (effectively
`ET += (now - lastCallTimestamp)` each call while `IN=TRUE`), not a snapshot
of an absolute wall-clock value compared against a stored deadline. This
matches the standard IEC 61131-3 on-delay timer semantics (scan-cycle-based
accumulation) rather than reading an absolute system clock. `PT` is simply
the target duration; `TOF`/`TP` follow the analogous pattern (documented on
sibling InfoSys pages under Tc2_Standard "Timer").

---

## 2. REAL/LREAL literal syntax and arithmetic promotion

**Literal forms:**

- `REAL` (32-bit, IEEE 754 single) and `LREAL` (64-bit, IEEE 754 double).
- Decimal point form: `3.402823E+38`, `-1.0E-44`; exponential form is `E` or
  `e` followed by an optionally-signed integer exponent.
- Range: REAL `-3.402823e+38 .. 3.402823e+38` (smallest abs `1.4013e-45`,
  32-bit storage); LREAL `-1.7976931348623158e+308 .. 1.7976931348623158e+308`
  (smallest abs `4.94065645841247e-324`, 64-bit storage).
- If the input value of a type conversion or an integer literal exceeds the
  target's range, either a decimal point must be added or an explicit
  typecast (e.g. `REAL#3400000000000000000000`) must be used, otherwise
  information can silently be lost.
  (Source: [REAL/LREAL](https://infosys.beckhoff.com/content/1033/tc3_plc_intro/2529405067.html))

**Implicit vs explicit promotion rules:**

Beckhoff's "Type conversion operators" page states the general TwinCAT ST
rule:

> "Conversions from a 'smaller' type to a 'larger' type, such as from BYTE to
> INT or from WORD to DINT, can be carried out explicitly — but they are also
> possible **implicitly** without calling a conversion operator."
>
> "If a larger data type is converted to a smaller data type, information may
> be lost" (must be explicit / a cast is expected).

Explicit conversions use the naming convention `<from>_TO_<to>` (typed,
e.g. `INT_TO_REAL`) or the overloaded form `TO_<to>` (e.g. `TO_REAL`).
(Source: [Type conversion operators](https://infosys.beckhoff.com/content/1033/tc3_plc_intro/3998090635.html))

So: INT → REAL/LREAL, REAL → LREAL are auto-widening ("smaller to larger")
and don't require a cast function; DINT/LINT → REAL, or REAL → INT
(narrowing / different representation family) are the ones where an
explicit `..._TO_..` cast is the documented/expected path, and the result of
an out-of-range conversion is documented as **undefined/platform-dependent**
(possible runtime exception).

**Arithmetic operators (existence, not exhaustively specified per-type
promotion tables in InfoSys):** ADD, SUB, MUL, DIV, MOD are documented as the
IEC "Arithmetic operators"
(Source: [Arithmetic operators](https://infosys.beckhoff.com/content/1033/tc3_plc_intro/3998062475.html)).
`MOD`: "integer remainder of the division," permitted only for the integer
family (`BYTE, WORD, DWORD, LWORD, SINT, USINT, INT, UINT, DINT, UDINT, LINT,
ULINT`) — **not** defined for REAL/LREAL — and division/MOD by zero is
explicitly called out as platform-dependent/possibly-exception-raising
behavior.
(Source: [MOD](https://infosys.beckhoff.com/content/1033/tc3_plc_intro/2528880779.html))

Boolean operators `AND`, `OR`, `XOR`, `NOT` exist as the IEC "Bitstring
Operators" group (they operate bitwise on BIT/BOOL/BYTE/WORD/DWORD/LWORD
operands, with BOOL as the single-bit case).
(Source: [Bitstring Operators list](https://infosys.beckhoff.com/content/1033/tcplccontrol/925520011.html))

---

## 3. STRUCT and ARRAY declaration syntax

**STRUCT:**

```
TYPE <structure name> :
STRUCT
    (<variable declaration optional with initialization>)+
END_STRUCT
END_TYPE
```

- At least one member; each member may have its own initializer.
- Structures can be **nested** (a member's type is another STRUCT type);
  the only restriction called out is that members inside a STRUCT cannot
  have an `AT` address declaration.
- Extension: `TYPE <name> EXTENDS <base struct> : STRUCT ... END_STRUCT END_TYPE`
  adds fields on top of an existing struct's fields.
- Struct **literal initializer** syntax: `(field1 := val1, field2 := val2, ...)`,
  e.g.
  `stPolygon : ST_POLYGONLINE := (aStart:=[1,1], aPoint1:=[5,2], ...);`
  Array-typed struct members are initialized with `[v1, v2, ...]` inside the
  parenthesized field list.
- Component access: `<variable name>.<component name>` (dotted path, chains
  through nested structs/arrays, e.g. `stPolygon.aPoint1[1]`).
  (Source: [Structure](https://infosys.beckhoff.com/content/1033/tc3_plc_intro/2529481355.html))
- **8-byte alignment**: "An 8-byte alignment was introduced with TwinCAT 3.
  Make sure the alignment is correct if data are exchanged as a complete
  memory block with other controllers or software components." This is the
  direct byte-layout warning relevant to wire-format loopback (§4 below).
  (Source: same Structure page)

**ARRAY:**

```
<variable name> : ARRAY[<lo>..<hi>] OF <data type> := [<initialization>];
```

- Multi-dimensional: `ARRAY[<dim1>, <dim2>, ...] OF <type>`, comma-separated
  dimensions, each with its own `lo..hi` bounds (bounds are integers up to
  `DINT`).
- Array literal initializer: `[v0, v1, v2, ...]`; a repeat-count shorthand is
  supported: `[2(10), 2(20)]` means `[10, 10, 20, 20]`.
- Arrays of structs: `ARRAY[1..3,1..3,1..10] OF ST_Data`, initialized with a
  list of struct literals `[(n1:=1, n2:=10, n3:=16#00FF), (n1:=2, ...), ...]`;
  elements not explicitly initialized get the default (zero) value of the
  base type — partial initialization is legal.
- Arrays of function block instances are also supported:
  `ARRAY[1..4] OF FB_Object`.
  (Source: [Array with fixed length](https://infosys.beckhoff.com/content/1033/tc3_plc_intro/8825253771.html),
  [ARRAY overview](https://infosys.beckhoff.com/content/1033/tc3_plc_intro/2529464203.html))

---

## 4. Memory/byte-layout guidance relevant to wire-format loopback

- `TIME`/`DATE`/`TOD`/`DT` = 32-bit (`UDINT`-equivalent); `LTIME`/`LDATE`/
  `LTOD`/`LDT` = 64-bit (`ULINT`-equivalent). This means a TIME field in a
  wire-format struct is a plain 4-byte little-endian unsigned integer count
  of milliseconds, and LTIME an 8-byte unsigned integer count of
  nanoseconds — no special encoding beyond the elementary integer type's
  standard binary layout.
  (Sources: [TIME/LTIME](https://infosys.beckhoff.com/content/1033/tc3_plc_intro/12189021579.html),
  [Date and time data types](https://infosys.beckhoff.com/content/1033/tc3_plc_intro/2529415819.html))
- `REAL`/`LREAL` are explicitly specified as IEEE 754 single/double
  precision — directly mappable to C# `float`/`double` bit-for-bit.
  (Source: [REAL/LREAL](https://infosys.beckhoff.com/content/1033/tc3_plc_intro/2529405067.html))
- STRUCT layout: Beckhoff explicitly documents **8-byte alignment as of
  TwinCAT 3**, and calls out that this matters specifically "if data are
  exchanged as a complete memory block with other controllers or software
  components" — i.e. this is Beckhoff's own guidance for the wire-transport
  scenario TcXunit needs. Any in-process struct-serialization loopback that
  aims to mimic real TwinCAT ADS/wire behavior should pad struct fields to
  8-byte alignment boundaries (not just tightly pack them), or the byte
  offsets won't match what a real TwinCAT controller/tool would produce.
  (Source: [Structure](https://infosys.beckhoff.com/content/1033/tc3_plc_intro/2529481355.html))
- No InfoSys page found that specifies ARRAY element packing beyond "collection
  of elements of the same data type" — arrays of elementary types are
  presumed densely packed at the element's natural size; arrays of STRUCTs
  inherit the struct's own (8-byte-aligned) layout per element.

---

## Implications for TcXunit (ordering)

1. **REAL/LREAL literals + arithmetic (auto-widen INT→REAL) unblock nothing
   time-related directly, but are the cheapest, most self-contained addition**
   (single literal grammar, IEEE754 float/double map directly onto C#
   `float`/`double`, and implicit-widen-only/no-narrow-without-cast keeps the
   type-checking simple). This is a good first step because it's low risk and
   is a prerequisite for FB_init-style struct/timer fields that mix INT and
   REAL (e.g. `PT` calculations derived from a REAL rate, or general test
   data with decimal values).

2. **TIME literal + 32-bit millisecond representation unblocks (a) the
   simulated clock for TON/TOF/TP.** Since `ET` is scan-cycle-accumulated
   elapsed time (not an absolute-clock snapshot), a `SimulatedClock`
   abstraction only needs to feed the interpreter a per-call "delta since
   last call" duration (in ms, fits a plain `uint`/`long`), and `TIME`
   literals need only the documented `T#`/`TIME#` grammar (a straightforward
   regex/parser extension) plus a 32-bit unsigned int internal
   representation. This should be implemented **before** timer FB support,
   since TON/TOF/TP's `PT`/`ET` are literally typed `TIME`.

3. **STRUCT + ARRAY declarations unblock both (b) in-process wire-format
   loopback and (c) struct/record test-data builders**, and should be done
   together since ARRAY-of-STRUCT and STRUCT-with-ARRAY-members are both
   explicitly supported/expected patterns in real TwinCAT code (and the
   VarBlockParser regex currently rejects both). The STRUCT literal
   initializer syntax `(field := val, ...)` is exactly the shape needed for
   test-data builders. For (b) specifically, the 8-byte alignment rule is a
   **must-have implementation detail**, not an optional nicety — a
   loopback that packs bytes without 8-byte alignment will silently produce
   wrong field offsets relative to what a real controller/ADS client would
   see, defeating the purpose of a "wire-format" test.

**Suggested order:** REAL/LREAL (+ arithmetic operators MOD/AND/OR/NOT as a
low-cost follow-on, since they're independent of the type-system shape) →
TIME literal + simulated-clock elapsed-time model → STRUCT/ARRAY declaration
and literal-initializer support (with 8-byte-aligned layout) last, since it
is the largest parser/VarBlockParser change and depends on having REAL/TIME
as valid field types to be useful for realistic wire-format structs.
