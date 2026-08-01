# Coverage wedge: granularity, and whether IEC 61508/13849 evidence is the thesis

- Issue: `TcXunit-229.8` (part of epic `TcXunit-229`; blocker `TcXunit-229.2`
  closed, findings in `docs/research/tf1140-audit.md`; blocks `TcXunit-229.11`,
  the v0.1 spec write-up).
- Date: drafted 2026-07-31, ratified 2026-08-01.
- **STATUS: ratified.** All nine calls were answered; the outcomes, including
  the one amendment (decision 9 and the demotion of K5 from a kill criterion to
  a quality bar), are recorded in the "Decisions" section at the end.
- Method: repo-grounded. Every claim about what the interpreter can instrument
  is cited to the symbol that owns it — never to a line number, which is wrong
  the moment anything above it moves — and was read, not inferred from a class
  name or a header comment. Markdown is cited by file and heading
  (`README.md § Usage`). **The IEC 61508 / ISO 13849 claims in §B are NOT
  primary-sourced** — the standards are paywalled, no copy was consulted, and
  those paragraphs are flagged inline as recollection. Treat them as hypotheses
  to verify, not as findings. This is the material difference in evidentiary
  weight between §A and §B of this document.

---

## Verdict in one paragraph

Statement coverage is nearly free — a per-statement execution hook already
exists in `Engine.ExecuteStatement`, firing exactly once per executed statement
in every body. Branch coverage is cheap: three or four local edits at sites
that already exist. MC-DC is not the next notch on the same dial — it needs a
stable per-condition **identity scheme** that does not exist anywhere in the
codebase today, because `Expr.Line` is the line of the token that *starts* an
expression and every condition in `IF a AND b OR c THEN` therefore shares one
line. Separately: **the hard part of any coverage level is the
denominator, not the numerator** — bodies are parsed lazily and cached by body
*text* (`TypeRegistry.GetStatements`, `TypeRegistry._statementCache`), so an
un-executed body is never parsed and its statement count is unknown at end of
run. On the commercial question, the answer is **no**: the safety-evidence
thesis does not survive its own bear case while `TcXunit-229.3` (the licensed
TwinCAT+TcUnit conformance oracle) is still OPEN, because the fidelity claim it
rests on is currently unbacked by any evidence at all. The wedge that does
survive is the one the code already states in its own comments: coverage as the
**agent's work list and self-check** inside the edit/run/iterate loop
(`SuiteCoverage`).

---

## A. Granularity

### A0. The ticket's framing is not quite the shape of the problem

"Statement, branch, or MC-DC?" reads as three settings on one dial. The repo
says otherwise: statement and branch are the same kind of work (hook an
existing execution site, record a hit), MC-DC is a different kind of work
(invent an identity model for sub-expressions, then do independence-pair
analysis over it). And *all three* share one cost the ticket doesn't mention —
establishing what was **not** executed. That cost is paid once, is the same
size at every granularity, and is larger than the statement-level instrumentation
itself. The rest of §A is organized around that.

### A1. Statement coverage: the hook already exists

`Engine.ExecuteStatement` is a single choke point through which every executed
statement passes, and it already writes a per-statement marker:

```csharp
// Engine.ExecuteStatement
private void ExecuteStatement(Stmt stmt, Frame frame)
{
    // The frame's "you are here" marker, at statement granularity. ...
    frame.CurrentLine = stmt.Line;
```

This is a per-statement execution hook in all but name. It is:

- **Unconditional** — `Engine.ExecuteStatement`'s own comment records that this
  is deliberate, so a statement with no line degrades to "unknown" rather
  than inheriting the previous statement's.
- **Universal** — nested bodies (IF/FOR/CASE arms) re-enter through the same
  method, and every body-entry path in the engine funnels into
  `Engine.ExecuteStatements` → `Engine.ExecuteStatement`: `Engine.CallMethod`,
  `Engine.CallGlobalFunction`, `Engine.InvokeFbInstance`, `Engine.StepCycles`,
  and the suite body itself (`Engine.RunSuite` → `Engine.ExecuteSuiteBody`).
- **Already paired with the identity a report needs** — the executing `Frame`
  carries `Frame.DeclaringTypeName`, `Frame.MethodName`, `Frame.BodyStartLine`,
  and the derived `Frame.CurrentFileLine`. `(DeclaringTypeName,
  MethodName, CurrentLine)` is a complete, stable statement key, and
  `CurrentFileLine` is the number a consumer opens the `.TcPOU` at.

**Cost to record executed statements: one seam.** A sink interface on `Engine`
(the `NativeFunctionRegistry` constructor-injection pattern in
`Engine(TypeRegistry, NativeFunctionRegistry)` is the precedent), written to in
`Engine.ExecuteStatement`, plus plumbing through `CliRunner` to the JSON.
Nothing about the engine's structure resists it.

**The trap to avoid, stated explicitly because the obvious design is wrong**:
do *not* key coverage on `Stmt` node identity (e.g. a `bool Hit` on `Stmt`).
`TypeRegistry._statementCache` is keyed on the implementation *text*, not on the
owning `PouAst` — "parsing is a pure function of the text and two declarations
sharing it share an AST" (`TypeRegistry.GetStatements`). Two
POUs with byte-identical bodies therefore share one `IReadOnlyList<Stmt>` and
the *same* `Stmt` objects. A node-identity scheme would mark both twins covered
when only one ran. The Frame-supplied key above does not have this problem
because it carries the declaring type.

### A2. The denominator is the real work, at every granularity

To say "12 of 30 statements in `FB_Widget`" you need the 30. Today the run
cannot produce it, for three compounding reasons.

1. **Bodies are parsed lazily, on first execution.** `ExecuteBody` takes a
   *factory* (`Func<IReadOnlyList<Stmt>>`) and calls it inside its own `try`,
   deliberately: "A callee's ImplementationText is parsed on first use, and if
   that parse throws, the fault has to land while THIS frame - the callee's
   own - is innermost" (`Engine.ExecuteBody`). A body no suite ever
   reaches is never parsed. Its statement count is not merely uncounted — it has
   never been computed. Every call site passes a lambda for exactly this reason
   (`Engine.RunSuite`, `Engine.CallMethod`, `Engine.CallGlobalFunction`,
   `Engine.InvokeFbInstance`, `Engine.StepCycles`).

2. **Eagerly parsing every loaded body — the obvious fix — will throw on the
   POUs today's laziness never touches.** The grammar is grow-on-demand by
   policy (`CLAUDE.md`, `README.md` § Status), and the front end raises
   `ParseException` — a `FormatException` subclass carrying the offending token
   and body line — from fifteen sites in `Parser.cs`. Post-
   `TcXunit-229.9` those are nameable as `parse-error`; that does not make them
   parseable. So an eager denominator pass converts a currently-silent condition
   ("we never looked at that body") into a loud one ("N POUs could not be
   measured"). That is an improvement in honesty and a regression in the
   tidiness of the number.

3. **Whole files are already dropped before parsing ever starts.** `CliRunner`
   parses each `*.TcPOU` individually and skips unloadable ones rather than
   aborting (`CliRunner.Run`, via `StructuralParseGuard.TryParseOrSkip`;
   `README.md` § Usage). A skipped file contributes nothing to either numerator
   or denominator and *is not visible in a percentage at all*.

**Consequence, and it is the single most important design constraint in §A:
never emit a tree-wide coverage percentage.** A percentage whose denominator
silently omits every POU outside the v1 grammar is a number that improves when
the grammar gets *worse*. The honest output is per-POU and three-valued:
covered statements, total statements, and *instrumentable yes/no* — with the
un-instrumentable POUs listed by name and reason, exactly as `skipped[]`
already does (`CliRunner.SkipReport`, `CliRunner.RunReport.Skipped`).

**Unmeasured, and it should be measured before ratifying**: an eager full-tree
parse pass costs wall-clock time that the "milliseconds per run" constraint
cares about. It should be cheap (the parse is already done for every executed
body, and `_statementCache` deduplicates by text), but nobody has timed it on a
real production tree. Flagging this as a guess.

### A3. Branch coverage: +3–5 local seams, and three traps

Branch coverage cannot be derived from statement hits, because in three places
the "not taken" outcome executes zero statements and is therefore invisible at
the statement hook:

1. **`IF` with no `ELSE`.** `IfStmt.Else` is always a list, empty when the
   source has no ELSE (`IfStmt.Else`), and `Engine.ExecuteStatement` calls
   `ExecuteStatements(ifStmt.Else, frame)` unconditionally. Executing an empty
   list produces no `ExecuteStatement` call at all. "The condition was false"
   leaves no trace.
2. **`CASE` with no matching arm.** `Engine.ExecuteCase` falls through to
   `ElseBody` when no label matches, and `ElseBody` may be
   empty. Same invisibility. Which arm matched also needs recording at the
   selection site (`Engine.CaseLabelMatches`), not inferred.
3. **Zero-iteration loops.** `Engine.ExecuteFor`/`Engine.ExecuteWhile`/
   `Engine.ExecuteRepeat` — a `WHILE` whose condition is false on
   first evaluation executes nothing.

A fourth trap is control-flow-specific: **`EXIT` unwinds by exception.**
`ExitStmt` throws `LoopExitSignal` (`Engine.ExecuteStatement`), caught by the
nearest enclosing loop (`Engine.ExecuteFor`, `Engine.ExecuteWhile`,
`Engine.ExecuteRepeat`). Any branch counter placed *after* the loop body will
be skipped on `EXIT`. `RETURN` has the same shape via `MethodReturnSignal`,
caught in `Engine.ExecuteBody`. Recording must happen at the decision point,
before the body runs.

So: one seam at the `IfStmt` case, one at `Engine.ExecuteCase` (covering both
arm-selection and implicit-else), one shared across the three loop forms, and —
if short-circuit operators are counted as decisions — one at
`Engine.EvaluateShortCircuit`. Three to five edits,
all at sites that already exist, all local. No new concepts.

### A4. MC-DC: not a seam count — a missing identity model

MC-DC requires, per decision and per condition within it: the condition's
identity, its Boolean outcome on each evaluation of the decision, and the
decision's outcome — so independence pairs can be found. Four findings, in
descending order of how badly they hurt:

1. **There is no per-condition identity available, and line numbers cannot
   supply one.** `Expr.Line` is documented as the line of "the token that
   STARTS it, so a BinaryExpr reports its left operand's line" (`Expr.Line`).
   In `IF bEnabled AND bReady OR bOverride THEN`, the whole decision and every
   condition inside it carry the same `Line`. AST node object identity exists
   but is unusable for the reason in §A1 (the text-keyed cache shares nodes
   across identical bodies) and is not stable across processes, so a report
   cannot name a condition with it. MC-DC needs a **synthesized structural
   path** — something like `(declaring type, method, statement index,
   expression path within the statement)` — computed at parse time and carried
   on every node. That concept does not exist anywhere in the codebase. It is
   the actual cost, and it is not a seam; it is a model.

2. **Short-circuit means "not evaluated" is a third outcome, not `false`.**
   `AND_THEN`/`OR_ELSE` genuinely skip their RHS (`Engine.EvaluateBinary`
   dispatches to `Engine.EvaluateShortCircuit`), which is what makes the
   `guard AND_THEN arr[i]` idiom safe. A recorder that logs an
   unevaluated condition as `false` produces wrong independence pairs. This is
   a known, solvable problem in MC-DC tooling for short-circuit languages — but
   it is a rule someone has to write, not a free consequence of hooking
   `Evaluate`.

3. **Operator overloading means "is this node a condition?" is a *runtime*
   question.** `AND`/`OR`/`XOR` dispatch from `Engine.EvaluateBinary` to
   `Engine.EvaluateBitstring`, which is logical on `bool` operands and bitwise
   on `int`/`long`/`ulong`. `NOT` is likewise BOOL-or-INT
   (`Engine.EvaluateUnary`). The same syntactic node is a decision in
   one evaluation and integer arithmetic in another. Classification must happen
   per evaluation, and a "decision" containing a bitstring subexpression is not
   a Boolean decision at all — another rule to specify.

4. **The outcome hook itself is easy** — `Engine.Evaluate` is one choke point
   returning `object`, so "this node evaluated to this bool" is a single edit.
   That is the *only* cheap part of MC-DC here, and it is worth saying plainly:
   the cheapness of the hook is what makes MC-DC look closer than it is.

**Cost summary, in seams-to-add rather than hours:**

| Level | New seams | New concepts | Notes |
| --- | --- | --- | --- |
| Statement (numerator) | 1 (`Engine.ExecuteStatement`) + CLI plumbing | none | Hook already exists; only recording is new |
| Denominator (any level) | 1 eager-parse pass over all loaded bodies | "instrumentable / not" as a reported state | The real work; shared by all levels |
| Branch | +3–5, all local | none | Must record at decision points, not after bodies (EXIT/RETURN unwind by exception) |
| MC-DC | +1 (`Engine.Evaluate`) | **stable sub-expression identity scheme; not-evaluated as a third outcome; per-evaluation Boolean-vs-bitstring classification; independence-pair analysis + reporting** | Different order of work — a sub-system, not more hooks |

### A5. What happens to the existing `--coverage` flag

Today `--coverage` is a **reference** list, not execution coverage:
`SuiteCoverage.Analyze` matches each non-suite POU's type name, whole-word and
case-insensitively (`SuiteCoverage.MentionsType`), against each suite's
comment-stripped concatenated text (`SuiteCoverage.AllText`,
`Lexer.StripComments`). It emits one entry per non-suite POU with the suites
that mention it, rendered as `coverage: [{pou, suites}]` in JSON
(`CliRunner.CoverageReport`) or one line per POU in text
(`CliRunner.WriteCoverageLines`), and it deliberately never affects the exit
code (`CliRunner.Run`, `README.md` § Usage). It is computed even when discovery
fails, because a tree with no suites is the tree where everything is uncovered
(`CliRunner.Run`).

The code already anticipates this ticket: *"Real line/branch coverage through
the interpreter would replace this rule without changing the shape of what it
reports"* (`SuiteCoverage`).

**Recommendation: subsume, not replace — and keep the flag's meaning.**
Reasoning:

- Reference coverage answers a question execution coverage cannot. A POU no
  suite even *mentions* has no execution data by construction; its correct
  report is "referenced by nothing", which is a directly actionable next task
  (`SuiteCoverage`). Execution coverage would report it as `0/N` — or,
  if it never parsed, as `0/unknown`, which is strictly less useful.
- The two together are the interesting signal, and neither alone is: **a POU
  that a suite references but never executes a statement of** is precisely the
  false-confidence case (a suite declaring `VAR fb : FB_X;` and never calling
  it reads as covered today, because the match is textual). That case is the
  bridge to `docs/research/agent-boundary.md` §6b — see the cross-reference
  there.
- The wire format should grow additively: `{pou, suites[], statements:
  {covered, total} | null, instrumentable: bool}`. `pou` and `suites` keep
  their exact current shape. **This is safe for the VSIX**:
  `src/xStunit.Vsix/xStunit.Vsix.csproj` has no `ProjectReference` at all and
  targets `v4.7.2` (`TargetFrameworkVersion`) — it consumes the CLI's JSON
  out-of-process via
  `XstunitArgumentBuilder`/`XstunitProcessRunner`/`XstunitModels`, and
  `XstunitModels.cs` models no coverage type whatsoever. It does model the
  failure vocabulary (`XstunitRunResult.Kind`, `XstunitSuiteResult.Kind`,
  `XstunitSuiteResult.Construct`), so `kind`/`construct` are no longer additive
  there. Additive fields cost it nothing; renames would cost it everything.
- **No second flag.** `--coverage` should keep doing one thing: report what is
  and isn't exercised, at the best granularity available for each POU. A
  `--coverage=statement|reference` split invites the agent to pick the wrong
  one.
- **Keep "never affects the exit code."** A coverage gate is a different
  product (CI enforcement) and a bad fit for the loop this tool serves — the
  agent should *see* the number and decide, not be blocked by it. It also
  preserves the property that exit `2` means "nothing ran", which
  `docs/research/agent-boundary.md` §6 leans on.

---

## B. Is IEC 61508 / ISO 13849 evidence the commercial thesis?

**Evidentiary warning, repeated because it matters:** everything in §B1–B3
about what the standards require is **recollection, not primary source**. The
standards are paywalled; none was consulted for this draft. The specific
clause numbers, tool classes, and per-SIL recommendation tables are exactly the
kind of detail that is easy to half-remember and expensive to be wrong about.
Nothing in §B should reach a customer-facing claim without being checked
against the standard text. §A, by contrast, is checkable against this repo
symbol by symbol.

### B1. The bull case, stated fairly

Functional-safety software certification demands structural coverage evidence.
IEC 61508-3 (recalled) recommends or highly recommends specific structural
coverage levels as a function of SIL — statement at the low end, branch and
then condition/MC-DC as SIL rises. ISO 13849 has its own software requirements
across PL a–e. Producing that evidence today, on a TwinCAT codebase, means an
on-target or emulated run with a licensed toolchain, per platform level, with
build-and-activate in the loop. A tool that produces the same class of evidence
**in milliseconds, from source, with no license and no hardware** is not a
cheaper version of that offer — it is a structurally different one. It is the
only structural advantage this project has (`README.md` § Why), applied to the
most expensive artifact in the domain.

That is a real argument. It is why the ticket exists. Here is why I do not
think it survives.

### B2. The bear case

**(i) Tool qualification.** IEC 61508-3 (recalled) classifies offline support
tools into T1/T2/T3, where T2 is roughly "a tool that supports test or
verification of the design or executable code, where an error in the tool can
fail to reveal defects". A coverage measurement tool sits squarely in T2. T2
qualification (recalled) wants evidence of suitability: a validation test suite,
a tool safety manual, a documented known-defects list, version and release
discipline, reproducibility of results.

Honest assessment of that cost: **not fatal, and less scary than it sounds.**
The validation suite largely exists — `tests/` mirrors `src/` per project
(`README.md` § Architecture) and is substantial. The rest is documentation plus
a permanent release-process tax. Call it weeks of writing, then an ongoing
per-release obligation. A solo or small project can carry that if the market
pays for it. So qualification is a *cost*, not the killer. The killer is (ii).

**(ii) Fidelity — and this is the one that decides the question.** Coverage
measured through our interpreter is coverage of *our reading* of the ST, not of
what the PLC executes. That is exactly the seam a safety assessor is trained to
attack, and the repo supplies the attack surface itself:

- The grammar is grow-on-demand *by policy*, not by accident (`CLAUDE.md`;
  `README.md` § Status; the `Engine` type comment).
- Whole POUs outside the subset are skipped and the run still exits 0/1
  (`CliRunner.Run`; `README.md` § Usage). A tree can be "fully covered" with
  files missing from the measurement entirely.
- An explicit narrowing cast evaluates via `Convert.ToInt32` for *every*
  integer target width — `SINT`, `USINT`, `INT`, … `LWORD` all take the same
  branch (`Engine.TryEvaluateCast`, `Engine.IntegerCastTargets`). Target-width
  truncation is not modelled at the cast site. A branch guarded by an
  overflow-dependent comparison can therefore go the other way on target.
- REAL/LREAL arithmetic runs through CLR `float`/`double`
  (`Engine.EvaluateNumeric`, `NumericCoercion.Promote`), not the target's FPU.
- Pointer arithmetic is documented as "correct as literal byte arithmetic when
  the pointee is a BYTE/SINT/USINT array …, **an approximation** for wider
  element types" (`Engine.EvaluatePointerArithmetic`).
- `_TO_STRING` explicitly does not attempt TwinCAT digit-count/exponent parity
  (`Engine.TryEvaluateCast`).
- GVL initialization has "no TwinCAT GVL init-cycle/task-binding semantics
  modeled, just zero-initialized storage" (`Engine._globals`), and unresolvable
  GVL defaults are silently swallowed after a bounded retry loop (the
  `Engine(TypeRegistry, NativeFunctionRegistry)` constructor).

Each is a place where a branch outcome here can differ from the branch outcome
there. Individually each is small and closable. Collectively they mean the
sentence "this coverage report describes the code your PLC runs" is a claim
with **no supporting evidence today** — because `TcXunit-229.3`, the licensed
TwinCAT+TcUnit nightly conformance oracle, is still OPEN. The oracle does not
exist. Selling structural-coverage evidence whose fidelity is unbacked is
selling the one thing an assessor will ask for first and we cannot produce.

**(iii) Market shape.** This paragraph is a guess and is labelled as one; no
customer research was done and none exists in the repo. The buyer of
safety-evidence tooling is a machine builder or component maker inside a
functional-safety programme, and such a programme names its toolchain in its
safety plan and agrees it with its assessor. Introducing a new, unqualified
tool costs that buyer an assessment conversation — a cost they will not pay to
save minutes. Meanwhile the user who feels xStunit's actual pain is the one
described in `README.md` § Why: a PLC codebase with *no automated test harness
at all*, validated by hand-built loopback FBs and commissioning. By
construction, that user has no functional-safety programme to sell evidence
into.

**If that is right, the safety-evidence buyer and the current user are disjoint
populations**, and the product today serves the second. That, not the
qualification cost, is why I recommend against the thesis.

### B3. Recommended wedge instead

Decide granularity on engineering grounds now, and let the safety positioning
be a **later option gated on evidence we do not yet have**:

- **Statement coverage for v0.1**, reported per POU, alongside the existing
  reference list, never as a tree-wide percentage, never gating the exit code.
- **The wedge is the agentic loop**, which is what the code already says the
  feature is for: "an uncovered POU is meant to read as a directly usable next
  task … for whoever or whatever is driving the loop" (`SuiteCoverage`).
  Statement coverage upgrades that from "which POUs does a suite mention" to
  "which lines did this run actually execute" — and it is what turns the
  *green-but-never-executed* failure mode (`docs/research/agent-boundary.md`
  §6b) from undetectable into a one-field check. That is a concrete,
  defensible, today-shippable claim that rests entirely on the structural
  advantage (no runtime, no license, milliseconds) without borrowing a fidelity
  claim we cannot support.
- **Safety evidence is deferred, not abandoned.** The prerequisite is
  `TcXunit-229.3` producing a *published, ongoing* conformance record. Coverage
  granularity is not the blocker; the oracle is. If the oracle lands and is
  clean, revisit with real leverage. Until then, do not put IEC 61508 or ISO
  13849 in any external-facing text.
- **Do not seam-proof for MC-DC now.** Same posture `TcXunit-229.7` took on
  TF1140: dual support turned out to need a wholly separate pipeline, not a
  parameterization, so no seams were pre-built. MC-DC is the same shape — it
  needs a sub-expression identity model, and a speculative half-built one is
  worse than none.

### B4. Kill criteria, and one quality bar

`TcXunit-229.11` asks for these explicitly. Stated as observations that would
falsify, not as vibes. The numbering has a gap: K5 was demoted to Q1 in
ratification and is kept under its old name so earlier references still resolve.

**Kills the safety-evidence thesis (already leaning dead; these would confirm):**

- **K1** — any real prospect, or any assessor-side response, states that an
  unqualified T2-class tool's coverage output is inadmissible as primary
  evidence. Confirms §B2(i) is worse than assessed.
- **K2** — `TcXunit-229.3`'s oracle, once it exists, exposes a divergence class
  that cannot be closed by grammar work (float semantics, task/cycle ordering,
  target-width arithmetic). Fidelity then has a permanent, nameable hole.
- **K3** — the first two prospects with a functional-safety programme both turn
  out to be contractually bound to an existing qualified toolchain. Confirms
  §B2(iii)'s market-shape guess.

**Revives it:**

- **R1** — the oracle exists, runs nightly, is publicly green, *and* a prospect
  asks for the coverage artifact unprompted. Both halves required; the second
  without the first is a trap.

**Kills the statement-coverage wedge itself:**

- **K4** — on a real production tree, the fraction of loaded POUs that parse
  well enough to be instrumentable is low enough (say under ~70%) that the
  report is dominated by `instrumentable: false`. The feature then reports
  mostly its own limits. The threshold is a guess; the measurement is not
  optional.
- **K6** — agents given the coverage output demonstrably do not change what
  they work on next. Then it is a report nobody reads, and the "work list"
  framing (`SuiteCoverage`) is wrong.

**Not a kill — a quality bar:**

- **Q1 (was K5)** — the eager denominator pass costs enough on a real tree to
  make a coverage run unpleasant. **Currently unmeasured**; tracked as
  `xstunit-fddl`, which needs a real production tree, not this repo's fixtures.

  The draft filed this as a kill criterion on the reasoning that everything in
  `TcXunit-3tx` rests on a full run costing milliseconds. It cannot fire there.
  `--coverage` is **opt-in**: the flag is parsed into a local `withCoverage`
  (`CliRunner.Run`), `coverage` stays null unless it was passed, and
  `SuiteCoverage.Analyze` runs only inside that branch. The agent inner loop
  invokes `--format json` with no `--coverage`, so it never pays the cost, and
  the milliseconds constraint is therefore not on the line. What is on the line
  is whether a deliberate coverage run stays usable — a real bar, worth
  measuring, but a bar the feature can fail without the feature being wrong.

  The measurement is also larger than the draft assumed: the eager full-tree
  parse pass **does not exist yet**. `SuiteCoverage.Analyze` is textual
  reference-matching over the POU AST with no statement-level parse, so
  `xstunit-fddl` is "build a throwaway harness and time it", not "time the
  existing pass".

---

## Decisions — all answered 2026-07-31, one amended

Every call below was accepted. One was amended in ratification (9), and that
amendment is what demoted K5 to Q1.

1. **Ship statement coverage in v0.1.** *Accepted.* The per-statement hook
   already exists and is universal (`Engine.ExecuteStatement`); the marginal
   cost is one seam plus reporting.
2. **Branch coverage is declared-next, not v0.1.** *Accepted.* It is only 3–5
   local seams, but each needs its own correctness rule (empty-ELSE,
   implicit-CASE-else, zero-iteration loops, `EXIT` unwinding by exception) and
   none of them is load-bearing for the agent-loop wedge.
3. **MC-DC is out of scope for v0.1 and explicitly NOT seam-proofed.**
   *Accepted, both halves.* It needs a sub-expression identity model that does
   not exist (`Expr.Line` rules out line-based identity); a speculative
   partial seam is worse than none, same conclusion `TcXunit-229.7` reached
   about TF1140.
4. **Never emit a tree-wide coverage percentage; report per-POU with an
   explicit `instrumentable` state.** *Accepted.* A percentage whose
   denominator omits unparseable POUs (`CliRunner.Run`) improves when the
   grammar gets worse.
5. **Subsume the existing `--coverage` by adding fields, keeping `pou` and
   `suites` byte-identical, with no second flag.** *Accepted.* The VSIX parses
   this JSON out-of-process (`xStunit.Vsix.csproj`'s `TargetFrameworkVersion`,
   no `ProjectReference`), so additive is free and renames are not; and the two
   signals are only useful together.
6. **Coverage never affects the exit code.** *Accepted.* Gating is a CI
   product; the agent loop needs the number visible, and exit `2` must keep
   meaning "nothing ran".
7. **IEC 61508 / ISO 13849 evidence is not the commercial thesis for v0.1.**
   *Accepted — deferred and gated on `xstunit-229.3`.* Not because
   qualification is unaffordable, but because the fidelity claim underneath it
   is currently supported by zero evidence, and the buyer for it looks disjoint
   from the current user.
8. **IEC 61508 / ISO 13849 are barred from all external-facing text until
   §B1–B3 have been checked against the actual standard text.** *Accepted.*
   Those paragraphs are recollection; §A is repo-checkable and §B is not, and
   the two should not be quoted with equal confidence.
9. **Measure the eager-parse denominator pass on a real tree — but not as a
   gate on decision 1.** *Accepted, amended.* The draft made the measurement a
   precondition for shipping statement coverage, on the reading that the cost
   lands on the milliseconds constraint. It does not: `--coverage` is opt-in and
   the agent loop never passes it (see Q1 under §B4). So the measurement is
   ordinary work, tracked as `xstunit-fddl`, and K5 becomes the quality bar Q1.
   `xstunit-fddl` stays human-gated for a resource reason — it needs a real
   production tree, which this repo does not contain.

---

## Related research

Continues the `TcXunit-229` thread. `docs/research/tf1140-audit.md` and
`docs/research/struccpp-notes.md` (`229.2`) are compatibility-target audits;
`docs/research/grammar-source-audit.md` (`229.5`) is the grammar-source
decision that feeds `229.6`'s parse-superset/execute-time split — which is what
makes `parse-error` a nameable kind and therefore what makes
`instrumentable: false` reportable with a reason instead of a shrug. The
STruC++ audit's finding that xStunit's control-flow statement set already
matches a serious independent ST implementation almost exactly
(`struccpp-notes.md`, verdict item 1) is the reason statement and branch
coverage are cheap here: the statement surface is not the gap. Companion
document (also ratified): `docs/research/agent-boundary.md` (`229.10`) — §6b
there is the failure mode this document's statement coverage exists to detect.
