# STruC++ audit vs TcXunit's parser/interpreter

- Issue: `TcXunit-229.2` (part of epic `TcXunit-229`, feeds `TcXunit-229.7` and
  `TcXunit-229.8`). This is a second, separate source alongside
  `docs/research/tf1140-audit.md` — do not conflate the two; see "Related
  research" cross-reference at the end of that file.
- Source audited: `https://github.com/Autonomy-Logic/STruCpp.git`, cloned
  shallow (`--depth 1`) into the scratchpad at commit HEAD of the default
  branch at audit time (README badge references a CI-passing `main`). Not
  vendored into this repo.

## Verdict: STruC++ is a full ST-to-C++17 *compiler*, not a PLC test-runtime peer — but its grammar/AST and test-file design are directly useful reference material for TcXunit-229.7/.8

Despite the name suggesting a C++ port of something ST-shaped, STruC++ is
**an independent, from-scratch IEC 61131-3 Structured Text compiler written in
TypeScript** (`src/`, Chevrotain-based, Node.js tooling — `package.json`,
`tsconfig.json`, `npm test`), that emits idiomatic C++17 source
(`class`/`operator()`/`IECVar<T>` templates) rather than interpreting ST
directly. It ships a bundled unit-testing framework (`strucpp source.st --test
tests.st`), an interactive REPL, and a CODESYS/`.stlib` library import system.
None of its runtime is TwinCAT- or `TcUnit`-shaped — it is its own
purpose-built test DSL (`TEST "name" ... END_TEST`, `ASSERT_EQ`/`ASSERT_TRUE`/
etc., `MOCK`/`MOCK_FUNCTION`, `ADVANCE_TIME`) compiled to a C++ binary and run
natively, not interpreted like TcXunit's `Engine`.

That means STruC++ is **not a compatibility target** the way TF1140 is (no
shared `.TcPOU`/XML format, no `TcUnit.FB_TestSuite` ancestry, no overlapping
assertion vocabulary to align with) — it's not something TcXunit could ever
"run" or be compared against for discovery/dispatch compatibility. Its value
to `TcXunit-229` is entirely as **reference architecture**: a mature,
test-covered (`npm test` runs "1400+ tests"), documented (`docs/
ARCHITECTURE.md`, `docs/IEC_COMPLIANCE.md`, `docs/TESTING.md`) example of (a)
how much of real-world IEC 61131-3 ST grammar a serious implementation ends up
needing, and (b) how a built-in ST test DSL can be designed independent of any
vendor's test framework. Concretely:

1. **Grammar coverage gap check (229.8 statement/branch semantics)**:
   STruC++'s documented control-flow set (`IF/ELSIF/ELSE`, `CASE`,
   `FOR/TO/BY`, `WHILE`, `REPEAT/UNTIL`, `EXIT`, `RETURN` —
   `docs/IEC_COMPLIANCE.md:93-103`) matches TcXunit's own control-flow set
   almost exactly (`wiki/09-control-flow-statements.md:1`, `Stmt.cs`/
   `Engine.Statements.cs`/`Parser.cs`) — good validation that TcXunit's
   grow-on-demand set already covers the IEC 61131-3 statement-level
   surface that matters for coverage/MC-DC evidence claims. The gaps are all
   at the *expression/operator* layer, not statements: STruC++ supports bit
   shift operators `SHL`/`SHR`/`ROL`/`ROR` (`docs/IEC_COMPLIANCE.md:79`) and
   TcXunit's `Lexer.cs`/`Parser.Expressions.cs` have no token or grammar rule
   for any of the four (confirmed by grep — zero matches for
   `SHL|SHR|ROL|ROR` anywhere under `src/TcXunit.Interpreter`). If
   `TcXunit-229.8`'s coverage-evidence thesis needs to claim branch coverage
   over bitwise-shift-guarded conditionals, that's a real, unaddressed gap,
   not a documentation gap.
2. **Test-DSL architecture (229.7 compatibility-target shape)**: STruC++'s
   test file design (`SETUP`/`TEARDOWN` blocks, `TEST "name" ... END_TEST`
   with local `VAR`, `ASSERT_EQ/NEQ/TRUE/FALSE/GT/LT/GE/LE/NEAR`, `MOCK`/
   `MOCK_FUNCTION ... RETURNS`, `ADVANCE_TIME(ns)`;
   `docs/TESTING.md:60-177`) is architecturally close in *spirit* to
   TcXunit's own model (one bracketed unit per test, assertion family,
   simulated-clock advance via `Engine.Clock.AdvanceMs`/`ADVANCE_TIME`) but
   diverges in every naming/structural specific — another data point (after
   TF1140) that there is no single "the" ST unit-test convention in the wild;
   TcUnit-style (`TEST()`/`TEST_FINISHED()` inline in a suite FB), TF1140/
   `Tc3_PlcTestFramework`-style (one FB per case, framework-driven), and
   STruC++-style (separate test-file DSL, not itself valid POU ST) are three
   different shapes. This strengthens the case that `229.7`'s "compatibility
   target" question is really "which *shape*, not which *vendor*" — see open
   questions below.
3. **Parser architecture worth learning from**: STruC++ uses a
   parser-generator (Chevrotain, LL(3) with CST→AST separation —
   `src/frontend/parser.ts` builds a CST, `src/frontend/ast-builder.ts`
   converts it to a typed discriminated-union AST) instead of TcXunit's
   hand-rolled recursive-descent (`Parser.cs`/`Parser.Expressions.cs`
   directly building `Expr`/`Stmt` nodes). This is a bigger structural
   choice than TcXunit-229 needs to revisit now (TcXunit's docs already
   commit to "hand-rolled, scoped to what fixtures actually need" — see
   `CLAUDE.md`/`Engine.cs` comments), but two smaller, adoptable patterns
   stood out:
   - **Case-insensitive normalization as a pre-lexer pass**
     (`uppercaseSource()`, `src/frontend/lexer.ts:993-1172`) that preserves
     string/comment literal case while uppercasing everything else, run once
     before tokenizing. TcXunit's `Lexer.cs` instead does ad-hoc per-keyword
     case handling implicitly via C#'s token matching (no explicit
     case-fold pass visible) — worth checking if TcXunit's identifier/keyword
     matching is fully case-insensitive per IEC 61131-3 §2.4 today, since ST
     is case-insensitive throughout, not just for keywords.
   - **A dedicated, separate lexer/token-set for test files**
     (`allTestTokens`/`TestLexer`, `src/frontend/lexer.ts:891-913`) built by
     splicing test-only keywords (`TEST`, `ASSERT_*`, `MOCK*`) into the
     normal token list, specifically so ordinary ST source can freely use
     `TEST` as an identifier while test files can't. TcXunit's `TEST`/
     `TEST_FINISHED`/`Assert*` are dispatched as ordinary method calls
     resolved at the *native-call* layer (`NativeMethodBridge.cs`), not at
     the lexer/keyword layer, so this doesn't directly apply — but it's a
     reminder that TcXunit's choice to keep `TEST` a plain identifier
     (never a reserved word) avoids the whole "two token sets" problem
     STruC++ has to solve, which is worth stating explicitly as a design
     property if `229.7` weighs alternate test syntaxes.

## Details

### What STruC++ actually is (verified from source, not the README's framing)

- `package.json`/`tsconfig.json`/`.eslintrc.cjs`/`vitest.config.ts` at repo
  root confirm this is a **TypeScript** project (Node 18+, built with `npm run
  build`), despite "C++" in the name and README's "IEC 61131-3 Structured
  Text to C++17 compiler" framing — the compiler itself runs on Node/V8; only
  its *output* is C++17. `README.md:274-279` credits Chevrotain (parser
  framework) and cites MatIEC (C target) and OpenPLC as prior art/motivation.
- Module map (`docs/ARCHITECTURE.md:37-89`, cross-checked against
  `find src -type f`): `src/frontend/` (lexer, Chevrotain LL(3) parser, AST,
  CST→AST builder), `src/semantic/` (3-pass symbol table → type resolution →
  validation), `src/backend/` (C++ codegen, including a dedicated
  `test-codegen.ts`/`test-main-gen.ts`), `src/testing/` (`test-model.ts`,
  `test-parser.ts` — thin re-export + CST→`TestFile` builder, 24 and 77
  lines respectively), `src/library/` (`.stlib` archive format + CODESYS
  V2.3/V3 import), `src/il/` (reserved IR, currently unused per
  `docs/ARCHITECTURE.md:81-82`), `src/runtime/` (header-only C++ runtime:
  `IECVar<T>`, standard functions, REPL).
- Confirmed by direct file read, not just README claims:
  `src/frontend/lexer.ts:743-883` (`allTokens`) and `:898-913`
  (`allTestTokens`/`TestLexer`) show two distinct token sets — the base ST
  lexer has no `TEST`/`ASSERT_*`/`MOCK*` tokens at all (so `TEST` is a legal
  identifier in ordinary ST source: `src/frontend/lexer.ts:358-363` comment
  "only active in test file lexing"); those tokens are spliced in only for
  `TestLexer` (`:898-907`).
- Confirmed the language surface claimed in `README.md:186-202` and
  `docs/IEC_COMPLIANCE.md` against the parser: `src/frontend/parser.ts:851-863`
  shows `caseStatement`/`forStatement`/`whileStatement`/`repeatStatement`
  rules gated by lookahead (`GATE: () => this.LA(1).tokenType === ...`), and
  `:1171-1264` implement each. `docs/IEC_COMPLIANCE.md:176-187` lists explicit
  non-goals: `UNION`, `FB_Init`/`FB_Exit` lifecycle, `__QUERYINTERFACE`, bit
  access (`var.%X0`), `ACTION` blocks, `TRY/CATCH/FINALLY`, generics,
  conditional compilation — i.e. even a "broad subset... Edition 3 plus
  CODESYS extensions" implementation (README's own description) draws a
  scope line; TcXunit's much narrower fixture-driven scope is directionally
  consistent with that same instinct, just drawn much tighter.

### STruC++'s test DSL, verified against its own parser/model

- `src/testing/test-model.ts:11-24` re-exports test-specific AST node types
  (`TestFile`, `SetupBlock`, `TeardownBlock`, `TestCase`, `AssertCall`,
  `AdvanceTimeStatement`, `MockFBStatement`, `MockFunctionStatement`,
  `MockVerifyCalledStatement`, `MockVerifyCallCountStatement`,
  `TestStatement`) from `src/frontend/ast.ts` — the test model is not a
  separate parallel type system, it's first-class in the same AST as regular
  ST, discriminated by `kind`.
- `src/testing/test-parser.ts:23-77` (`parseTestFile`) is a thin wrapper:
  tokenize+parse with the test lexer/parser (`parseTestSource`), then hand the
  CST to `buildTestAST` (in `ast-builder.ts`). Test files are parsed as their
  own top-level unit (`SETUP`/`TEARDOWN`/multiple `TEST` blocks), not as a
  method inside a POU FB the way TcXunit's `TEST()`/`TEST_FINISHED()` bracket
  a method body.
- Nine assertion kinds documented and present in the lexer
  (`ASSERT_EQ`/`ASSERT_NEQ`/`ASSERT_TRUE`/`ASSERT_FALSE`/`ASSERT_GT`/
  `ASSERT_LT`/`ASSERT_GE`/`ASSERT_LE`/`ASSERT_NEAR`,
  `src/frontend/lexer.ts:366-401`), each accepting an optional trailing
  message (`docs/TESTING.md:98-116`) — a strictly larger assertion vocabulary
  than TcXunit's current `AssertTrue`/`AssertFalse`/`AssertEquals`
  (`src/TcXunit.Interpreter/Hosts/NativeMethodBridge.cs:57-58`; no
  `AssertGreaterThan`/`AssertLessThan`/`AssertNear`-equivalent exists in
  TcXunit today, confirmed by grep of `NativeMethodBridge.cs` and
  `TcUnitSuiteHost.cs`).
- `ADVANCE_TIME(nanoseconds)` (`docs/TESTING.md:120-136`) is directly
  analogous to TcXunit's `Engine.Clock.AdvanceMs(dt)`
  (`wiki/03-simulated-clock-and-timers.md`) — same underlying idea (advance a
  simulated scan-cycle clock without wall-clock delay), different surface
  (STruC++ exposes it as a first-class test-DSL statement token; TcXunit
  exposes it as a native method call on the shared `Engine.Clock`).
- `MOCK`/`MOCK_FUNCTION ... RETURNS`/`MOCK_VERIFY_CALLED`/
  `MOCK_VERIFY_CALL_COUNT` (`docs/TESTING.md:138-177`,
  `src/frontend/lexer.ts:424-437`) has **no TcXunit analog at all** — TcXunit
  has no mocking/stubbing primitive for FB instances or free functions today
  (confirmed: no "mock" hits anywhere under `src/TcXunit.Interpreter` or
  `wiki/`). This is the single largest capability STruC++ has that TcXunit
  doesn't, and it is a plausible, concrete feature gap worth a future bead if
  fixture authors ever need to isolate a unit from a dependency FB's real
  behavior — currently the only isolation mechanism in TcXunit's own docs is
  `Loopback`'s fault-injection FB (`wiki/04-loopback-and-faults.md`), which is
  transport-specific, not general-purpose call mocking.

### TcXunit's own control-flow/expression surface, re-verified from code (not comments)

- `wiki/09-control-flow-statements.md:1-97` documents `FOR`/`WHILE`/`REPEAT`/
  `CASE`/`EXIT` plus unary minus as **built**, sourced to `Stmt.cs`/
  `Parser.cs`/`Engine.cs`. Verified directly: `Parser.Expressions.cs:96-110`
  (`ParseUnary`) handles both `NOT` and unary `Minus` — confirming the wiki's
  claim and, per this project's own "verify wiring, not doc comments"
  convention (`bd remember tcxunit-verify-wiring-not-doc-comments`), directly
  contradicting a **stale comment** at
  `src/TcXunit.Interpreter/Lexing/Lexer.cs:9`: `"No unary minus, no real/string
  escapes - grow-on-demand..."` — that comment predates
  `TcXunit-mym.3` landing unary minus and is now wrong; the lexer already
  emits a plain `Minus` token (`Lexer.cs:257`) and the parser already
  consumes it as a prefix operator. Worth a small doc-hygiene fix
  independent of this research task.
- No `SHL`/`SHR`/`ROL`/`ROR` tokens or grammar anywhere in
  `src/TcXunit.Interpreter` (grep across the whole project directory: zero
  matches). STruC++ supports all four (`docs/IEC_COMPLIANCE.md:79`,
  `README.md:195`). If no current fixture needs bit-shift operators this is
  fine under TcXunit's "grow on demand" philosophy — but it is a concrete,
  named gap that `229.8`'s coverage-evidence thesis should know about rather
  than discover mid-write.
- `EXTENDS` ancestry walking exists in TcXunit but only for two narrow
  purposes today: suite-root discovery (`SuiteDiscovery.IsSuiteType`, see
  `tf1140-audit.md`) and property/method resolution up a base-type chain
  (`Engine.Properties.cs:16`, comment "first declaring type in the EXTENDS
  chain"). There is no general OOP surface (`IMPLEMENTS`, `ABSTRACT`,
  `OVERRIDE`, `INTERFACE`, virtual dispatch) — grep for all of those across
  `src/TcXunit.Interpreter` returns nothing except loader/comment mentions
  that don't implement the semantics. STruC++ implements the full OOP
  extension set (`docs/IEC_COMPLIANCE.md:105-118`: methods, GET/SET
  properties, single inheritance, multiple interfaces, ABSTRACT/FINAL/
  OVERRIDE, access modifiers, `THIS`). This is expected and fine given
  TcXunit's narrower mission (interpreting fixture bodies, not compiling
  arbitrary OOP-heavy PLC codebases) but is worth naming explicitly if
  `229.7` ever considers "real-world project" compatibility as a target,
  since real TwinCAT codebases do use `EXTENDS`/`IMPLEMENTS`/methods more
  broadly than TcXunit's current suite-discovery-only usage.

## Open questions for TcXunit-229.7 (compatibility target shape)

1. STruC++ demonstrates a third distinct ST-test-DSL shape (separate
   `SETUP`/`TEARDOWN`/`TEST` file, not itself POU-shaped ST, parsed with a
   dedicated test-only token set) alongside TcUnit's (suite FB + `TEST()`/
   `TEST_FINISHED()` bracketing) and TF1140's (one FB per case, framework-
   registered). Does `229.7` want to survey the shape-space explicitly (at
   least these three) before committing to "the" compatibility target, or is
   TcUnit's shape locked in regardless of what other tools do?
2. Given STruC++ has no shared file format, base-type, or assertion name with
   TcXunit, is there any argument for treating it as a compatibility target
   at all — or is its only relevant contribution the assertion vocabulary
   (`ASSERT_GT`/`ASSERT_LT`/`ASSERT_GE`/`ASSERT_LE`/`ASSERT_NEAR`) and the
   mocking primitives, both of which could be adopted independent of any
   file-format compatibility question?
3. Is FB/function mocking (STruC++'s `MOCK`/`MOCK_FUNCTION ... RETURNS`/
   `MOCK_VERIFY_CALLED`/`MOCK_VERIFY_CALL_COUNT`) in scope for TcXunit at all,
   given it has no analog today and no open bead found for it? If fixture
   authors keep hitting the "how do I isolate this FB from its real
   dependency" problem, this is a candidate feature; if `Loopback`-style
   fault injection is judged sufficient for TcXunit's transport-testing
   focus, it can stay out of scope.

## Open questions for TcXunit-229.8 (statement/branch/MC-DC coverage evidence thesis)

1. Bit-shift operators (`SHL`/`SHR`/`ROL`/`ROR`) are absent from TcXunit's
   lexer/parser entirely. If the coverage-evidence argument needs to claim
   "we can measure branch coverage over any IEC 61131-3 conditional
   expression," this is a specific, citable gap (zero grep hits) rather than
   a theoretical one — worth deciding explicitly in/out of the 229.8 scope
   statement rather than leaving implicit.
2. TcXunit's statement-level control-flow set (`FOR`/`WHILE`/`REPEAT`/
   `CASE`/`EXIT`, plus `IF`/`ELSIF`/`ELSE` elsewhere) matches STruC++'s
   documented set closely at the *statement* level (both omit `TRY/CATCH`,
   `ACTION` blocks, and conditional compilation) — this is a positive
   signal that TcXunit's current statement/branch semantics are not
   missing an entire commonly-implemented control construct, which
   should make a narrower MC-DC evidence claim easier to state precisely
   (i.e. "MC-DC over IF/CASE/loop conditions, not over expressions using
   bit-shift operators" is an honest, currently-accurate boundary).
3. STruC++'s CASE grammar (`caseStatement`, `src/frontend/parser.ts:1171-
   1226`) and TcXunit's (`wiki/09-control-flow-statements.md:59-76`: label
   lists, `lo..hi` ranges, first-match-wins, no fallthrough) look
   semantically aligned from the docs alone — worth a follow-up
   line-by-line comparison of STruC++'s `caseStatement`/`caseElement`
   grammar against TcXunit's `ParseCase`/`ExecuteCase` if `229.8` needs to
   defend CASE-arm coverage claims (e.g. does TcXunit track "ELSE-arm-taken"
   as a distinct coverage fact the way a full MC-DC argument would need?)
   — not done in this pass, flagged as a concrete next step rather than
   asserted either way.
