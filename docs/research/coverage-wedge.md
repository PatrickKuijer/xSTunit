# Coverage wedge: granularity, and whether IEC 61508/13849 evidence is the thesis

- Issue: `TcXunit-229.8` (part of epic `TcXunit-229`; blocker `TcXunit-229.2`
  closed, findings in `docs/research/tf1140-audit.md`; blocks `TcXunit-229.11`,
  the v0.1 spec write-up).
- Date: 2026-07-31.
- **STATUS: DRAFT — awaiting ratification.** Nothing here is decided. The
  recommendations at the end are proposals with their reasoning exposed; the
  human ratifies, modifies, or rejects.
- Method: repo-grounded. Every claim about what the interpreter can instrument
  is cited to `path/file.cs:line` and was read, not inferred from a class name
  or a header comment. **The IEC 61508 / ISO 13849 claims in §B are NOT
  primary-sourced** — the standards are paywalled, no copy was consulted, and
  those paragraphs are flagged inline as recollection. Treat them as hypotheses
  to verify, not as findings. This is the material difference in evidentiary
  weight between §A and §B of this document.

---

## Verdict in one paragraph

Statement coverage is nearly free — a per-statement execution hook already
exists at `src/xStunit.Interpreter/Engine/Engine.Statements.cs:26`, firing
exactly once per executed statement in every body. Branch coverage is cheap:
three or four local edits at sites that already exist. MC-DC is not the next
notch on the same dial — it needs a stable per-condition **identity scheme**
that does not exist anywhere in the codebase today, because `Expr.Line` is the
line of the token that *starts* an expression and every condition in
`IF a AND b OR c THEN` therefore shares one line
(`src/xStunit.Interpreter/Parsing/Expr.cs:6-12`). Separately: **the hard part of
any coverage level is the denominator, not the numerator** — bodies are parsed
lazily and cached by body *text*
(`src/xStunit.Interpreter/Types/TypeRegistry.cs:41,123-131`), so an
un-executed body is never parsed and its statement count is unknown at end of
run. On the commercial question, the recommendation is **no**: the
safety-evidence thesis does not survive its own bear case while
`TcXunit-229.3` (the licensed TwinCAT+TcUnit conformance oracle) is still OPEN,
because the fidelity claim it rests on is currently unbacked by any evidence at
all. The wedge that does survive is the one the code already states in its own
comments: coverage as the **agent's work list and self-check** inside the
edit/run/iterate loop (`src/xStunit.Interpreter/Discovery/SuiteCoverage.cs:12-15`).

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
// src/xStunit.Interpreter/Engine/Engine.Statements.cs:16-26
private void ExecuteStatement(Stmt stmt, Frame frame)
{
    // TcXunit-p3t.4: the frame's "you are here" marker, at statement
    // granularity. ...
    frame.CurrentLine = stmt.Line;
```

This is a per-statement execution hook in all but name. It is:

- **Unconditional** — the comment at `Engine.Statements.cs:24-25` records that
  this is deliberate, so a statement with no line degrades to "unknown" rather
  than inheriting the previous statement's.
- **Universal** — nested bodies (IF/FOR/CASE arms) re-enter through the same
  method (`Engine.Statements.cs:38,40,97,110,123,139,144`), and every body-entry
  path in the engine funnels into `ExecuteStatements` → `ExecuteStatement`:
  `CallMethod` (`Engine.Invocation.cs:283`), `CallGlobalFunction`
  (`Engine.Invocation.cs:412`), `InvokeFbInstance` (`Engine.Invocation.cs:561`),
  `StepCycles` (`Engine.Convergence.cs:36`), and the suite body itself
  (`Engine.cs:147` → `ExecuteSuiteBody` → `Engine.Diagnostics.cs:109`).
- **Already paired with the identity a report needs** — the executing `Frame`
  carries `DeclaringTypeName` (`Values/Frame.cs:14`), `MethodName`
  (`Values/Frame.cs:21`), `BodyStartLine` (`Values/Frame.cs:38`), and the
  derived `CurrentFileLine` (`Values/Frame.cs:56-59`). `(DeclaringTypeName,
  MethodName, CurrentLine)` is a complete, stable statement key, and
  `CurrentFileLine` is the number a consumer opens the `.TcPOU` at.

**Cost to record executed statements: one seam.** A sink interface on `Engine`
(the `NativeFunctionRegistry` constructor-injection pattern at `Engine.cs:44-54`
is the precedent), written to at `Engine.Statements.cs:26`, plus plumbing
through `CliRunner` to the JSON. Nothing about the engine's structure resists it.

**The trap to avoid, stated explicitly because the obvious design is wrong**:
do *not* key coverage on `Stmt` node identity (e.g. a `bool Hit` on `Stmt`).
`TypeRegistry._statementCache` is keyed on the implementation *text*, not on the
owning `PouAst` — "the mapping is content -> AST regardless of which declaration
the text came from" (`Types/TypeRegistry.cs:34-41`, cache at `123-131`). Two
POUs with byte-identical bodies therefore share one `IReadOnlyList<Stmt>` and
the *same* `Stmt` objects. A node-identity scheme would mark both twins covered
when only one ran. The Frame-supplied key above does not have this problem
because it carries the declaring type.

### A2. The denominator is the real work, at every granularity

To say "12 of 30 statements in `FB_Widget`" you need the 30. Today the run
cannot produce it, for three compounding reasons.

1. **Bodies are parsed lazily, on first execution.** `ExecuteBody` takes a
   *factory* (`Func<IReadOnlyList<Stmt>>`) and calls it inside its own `try`,
   deliberately: "A callee's ImplementationText is parsed lazily on first use
   (TypeRegistry.GetStatements), and if that parse itself throws ... it gets
   attributed here" (`Engine/Engine.Diagnostics.cs:26-42`). A body no suite ever
   reaches is never parsed. Its statement count is not merely uncounted — it has
   never been computed. Every call site passes a lambda for exactly this reason
   (`Engine.cs:148`, `Engine.Invocation.cs:283,412,561`,
   `Engine.Convergence.cs:36`).

2. **Eagerly parsing every loaded body — the obvious fix — will throw on the
   POUs today's laziness never touches.** The grammar is grow-on-demand by
   policy (`CLAUDE.md`, `README.md:11`), and the front end raises
   `FormatException` from at least 15 sites in `src/xStunit.Interpreter/Parsing/Parser.cs`
   (`:48,151,170,178,201,218,233,245,251,264,270,283,288,301,323`). Post-
   `TcXunit-229.9` those are nameable as `parse-error`; that does not make them
   parseable. So an eager denominator pass converts a currently-silent condition
   ("we never looked at that body") into a loud one ("N POUs could not be
   measured"). That is an improvement in honesty and a regression in the
   tidiness of the number.

3. **Whole files are already dropped before parsing ever starts.**
   `CliRunner` parses each `*.TcPOU` individually and skips unloadable ones
   rather than aborting (`src/xStunit.Cli/CliRunner.cs:209-226`; rationale at
   `:197-207`; `README.md:45`). A skipped file contributes nothing to either
   numerator or denominator and *is not visible in a percentage at all*.

**Consequence, and it is the single most important design constraint in §A:
never emit a tree-wide coverage percentage.** A percentage whose denominator
silently omits every POU outside the v1 grammar is a number that improves when
the grammar gets *worse*. The honest output is per-POU and three-valued:
covered statements, total statements, and *instrumentable yes/no* — with the
un-instrumentable POUs listed by name and reason, exactly as `skipped[]`
already does (`CliRunner.cs:594-595,768`).

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
   source has no ELSE (`Parsing/Stmt.cs:41-52`), and `ExecuteStatement` calls
   `ExecuteStatements(ifStmt.Else, frame)` unconditionally
   (`Engine.Statements.cs:36-41`). Executing an empty list produces no
   `ExecuteStatement` call at all. "The condition was false" leaves no trace.
2. **`CASE` with no matching arm.** `ExecuteCase` falls through to `ElseBody`
   when no label matches (`Engine.Statements.cs:131-145`), and `ElseBody` may be
   empty. Same invisibility. Which arm matched also needs recording at the
   selection site (`:137-141`), not inferred.
3. **Zero-iteration loops.** `ExecuteFor`/`ExecuteWhile`/`ExecuteRepeat`
   (`Engine.Statements.cs:83-129`) — a `WHILE` whose condition is false on
   first evaluation executes nothing.

A fourth trap is control-flow-specific: **`EXIT` unwinds by exception.**
`ExitStmt` throws `LoopExitSignal` (`Engine.Statements.cs:57-58`), caught by the
nearest enclosing loop (`:71-73,100-102,111-113,125-127`). Any branch counter
placed *after* the loop body will be skipped on `EXIT`. `RETURN` has the same
shape via `MethodReturnSignal` (`:59-60,79-81`), caught in `ExecuteBody`
(`Engine.Diagnostics.cs:44-49`). Recording must happen at the decision point,
before the body runs.

So: one seam at the `IfStmt` case, one at `ExecuteCase` (covering both
arm-selection and implicit-else), one shared across the three loop forms, and —
if short-circuit operators are counted as decisions — one at
`EvaluateShortCircuit` (`Engine.Expressions.cs:606-628`). Three to five edits,
all at sites that already exist, all local. No new concepts.

### A4. MC-DC: not a seam count — a missing identity model

MC-DC requires, per decision and per condition within it: the condition's
identity, its Boolean outcome on each evaluation of the decision, and the
decision's outcome — so independence pairs can be found. Four findings, in
descending order of how badly they hurt:

1. **There is no per-condition identity available, and line numbers cannot
   supply one.** `Expr.Line` is documented as the line of "the token that
   STARTS it, so a BinaryExpr reports its left operand's line"
   (`Parsing/Expr.cs:6-12`). In `IF bEnabled AND bReady OR bOverride THEN`,
   the whole decision and every condition inside it carry the same `Line`.
   AST node object identity exists but is unusable for the reason in §A1 (the
   text-keyed cache shares nodes across identical bodies) and is not stable
   across processes, so a report cannot name a condition with it. MC-DC needs
   a **synthesized structural path** — something like
   `(declaring type, method, statement index, expression path within the
   statement)` — computed at parse time and carried on every node. That concept
   does not exist anywhere in the codebase. It is the actual cost, and it is
   not a seam; it is a model.

2. **Short-circuit means "not evaluated" is a third outcome, not `false`.**
   `AND_THEN`/`OR_ELSE` genuinely skip their RHS
   (`Engine.Expressions.cs:187-188,606-628`), which is what makes the
   `guard AND_THEN arr[i]` idiom safe (`:598-605`). A recorder that logs an
   unevaluated condition as `false` produces wrong independence pairs. This is
   a known, solvable problem in MC-DC tooling for short-circuit languages — but
   it is a rule someone has to write, not a free consequence of hooking
   `Evaluate`.

3. **Operator overloading means "is this node a condition?" is a *runtime*
   question.** `AND`/`OR`/`XOR` dispatch to `EvaluateBitstring`
   (`Engine.Expressions.cs:201-202`), which is logical on `bool` operands
   (`:635-643`) and bitwise on `int`/`long`/`ulong` (`:644-669`). `NOT` is
   likewise BOOL-or-INT (`:153-160`). The same syntactic node is a decision in
   one evaluation and integer arithmetic in another. Classification must happen
   per evaluation, and a "decision" containing a bitstring subexpression is not
   a Boolean decision at all — another rule to specify.

4. **The outcome hook itself is easy** — `Evaluate` is one choke point
   (`Engine.Expressions.cs:10`) returning `object`, so "this node evaluated to
   this bool" is a single edit. That is the *only* cheap part of MC-DC here,
   and it is worth saying plainly: the cheapness of the hook is what makes MC-DC
   look closer than it is.

**Cost summary, in seams-to-add rather than hours:**

| Level | New seams | New concepts | Notes |
| --- | --- | --- | --- |
| Statement (numerator) | 1 (`Engine.Statements.cs:26`) + CLI plumbing | none | Hook already exists; only recording is new |
| Denominator (any level) | 1 eager-parse pass over all loaded bodies | "instrumentable / not" as a reported state | The real work; shared by all levels |
| Branch | +3–5, all local | none | Must record at decision points, not after bodies (EXIT/RETURN unwind by exception) |
| MC-DC | +1 (`Evaluate`) | **stable sub-expression identity scheme; not-evaluated as a third outcome; per-evaluation Boolean-vs-bitstring classification; independence-pair analysis + reporting** | Different order of work — a sub-system, not more hooks |

### A5. What happens to the existing `--coverage` flag

Today `--coverage` is a **reference** list, not execution coverage:
`SuiteCoverage.Analyze` (`src/xStunit.Interpreter/Discovery/SuiteCoverage.cs:27-52`)
matches each non-suite POU's type name, whole-word and case-insensitively,
against each suite's comment-stripped concatenated text
(`:41-44,69-70`). It emits one entry per non-suite POU with the suites that
mention it (`:46-51`), rendered as `coverage: [{pou, suites}]` in JSON
(`CliRunner.cs:782-792`) or one line per POU in text (`CliRunner.cs:576-583`),
and it deliberately never affects the exit code (`CliRunner.cs:56-59`,
`README.md:64`). It is computed even when discovery fails, because a tree with
no suites is the tree where everything is uncovered (`CliRunner.cs:308-316`).

The code already anticipates this ticket: *"Real line/branch coverage through
the interpreter is a later and much larger step (unusually cheap for xStunit
compared with any on-target tool, since the engine already walks every
statement), and would replace this rule without changing its shape"*
(`SuiteCoverage.cs:19-21`).

**Recommendation: subsume, not replace — and keep the flag's meaning.**
Reasoning:

- Reference coverage answers a question execution coverage cannot. A POU no
  suite even *mentions* has no execution data by construction; its correct
  report is "referenced by nothing", which is a directly actionable next task
  (`SuiteCoverage.cs:12-15`). Execution coverage would report it as `0/N` — or,
  if it never parsed, as `0/unknown`, which is strictly less useful.
- The two together are the interesting signal, and neither alone is: **a POU
  that a suite references but never executes a statement of** is precisely the
  false-confidence case (a suite declaring `VAR fb : FB_X;` and never calling
  it reads as covered today, because the match is textual). That case is the
  bridge to `docs/research/agent-boundary.md` §5b — see the cross-reference
  there.
- The wire format should grow additively:
  `{pou, suites[], statements: {covered, total} | null, instrumentable: bool}`.
  `pou` and `suites` keep their exact current shape. **This is safe for the
  VSIX**: `src/xStunit.Vsix/xStunit.Vsix.csproj` has no `ProjectReference` at
  all and targets `v4.7.2` (`:35`) — it consumes the CLI's JSON out-of-process
  via `TcxunitArgumentBuilder`/`TcxunitProcessRunner`/`TcxunitModels`, and
  `TcxunitModels.cs` models no coverage type whatsoever (nor, incidentally,
  any `kind`/`construct` field). Additive fields cost it nothing; renames would
  cost it everything.
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
line by line.

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
only structural advantage this project has (`README.md:3,7`), applied to the
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
(`README.md:23`) and is substantial. The rest is documentation plus a permanent
release-process tax. Call it weeks of writing, then an ongoing per-release
obligation. A solo or small project can carry that if the market pays for it.
So qualification is a *cost*, not the killer. The killer is (ii).

**(ii) Fidelity — and this is the one that decides the question.** Coverage
measured through our interpreter is coverage of *our reading* of the ST, not of
what the PLC executes. That is exactly the seam a safety assessor is trained to
attack, and the repo supplies the attack surface itself:

- The grammar is grow-on-demand *by policy*, not by accident (`CLAUDE.md`;
  `README.md:11`; `Engine.cs:10-12`).
- Whole POUs outside the subset are skipped and the run still exits 0/1
  (`CliRunner.cs:209-226`; `README.md:45`). A tree can be "fully covered" with
  files missing from the measurement entirely.
- An explicit narrowing cast evaluates via `Convert.ToInt32` for *every*
  integer target width — `SINT`, `USINT`, `INT`, … `LWORD` all take the same
  branch (`Engine.Expressions.cs:754-757,779-780`). Target-width truncation is
  not modelled at the cast site. A branch guarded by an overflow-dependent
  comparison can therefore go the other way on target.
- REAL/LREAL arithmetic runs through CLR `float`/`double`
  (`Engine.Expressions.cs:523-551,259-268`), not the target's FPU.
- Pointer arithmetic is documented as "correct as literal byte arithmetic when
  the pointee is a BYTE/SINT/USINT array …, **an approximation** for wider
  element types" (`Engine.Expressions.cs:271-286`).
- `_TO_STRING` explicitly does not attempt TwinCAT digit-count/exponent parity
  (`Engine.Expressions.cs:781-796`).
- GVL initialization has "no TwinCAT GVL init-cycle/task-binding semantics
  modeled, just zero-initialized storage" (`Engine.cs:19-22`), and unresolvable
  GVL defaults are silently swallowed after a bounded retry loop
  (`Engine.cs:93-105`).

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
described in `README.md:7`: a PLC codebase with *no automated test harness at
all*, validated by hand-built loopback FBs and commissioning. By construction,
that user has no functional-safety programme to sell evidence into.

**If that is right, the safety-evidence buyer and the current user are disjoint
populations**, and the product today serves the second. That, not the
qualification cost, is why I recommend against the thesis.

### B3. Recommended wedge instead

Decide granularity on engineering grounds now, and let the safety positioning
be a **later option gated on evidence we do not yet have**:

- **Statement coverage for v0.1**, reported per POU, alongside the existing
  reference list, never as a tree-wide percentage, never gating the exit code.
- **The wedge is the agentic loop**, which is what the code already says the
  feature is for: "The same data a human reads as a coverage report, aimed at
  the agent picking what to do next" (`SuiteCoverage.cs:12-15`). Statement
  coverage upgrades that from "which POUs does a suite mention" to "which lines
  did this run actually execute" — and it is what turns the *green-but-never-
  executed* failure mode (`docs/research/agent-boundary.md` §5b) from
  undetectable into a one-field check. That is a concrete, defensible,
  today-shippable claim that rests entirely on the structural advantage
  (no runtime, no license, milliseconds) without borrowing a fidelity claim we
  cannot support.
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

### B4. Kill criteria

`TcXunit-229.11` asks for these explicitly. Stated as observations that would
falsify, not as vibes.

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
- **K5** — the eager denominator pass measurably breaks the milliseconds
  constraint on a real tree. Everything in `TcXunit-3tx` rests on a full run
  costing milliseconds; a coverage feature that costs seconds is not a coverage
  feature, it is a different product. **Currently unmeasured.**
- **K6** — agents given the coverage output demonstrably do not change what
  they work on next. Then it is a report nobody reads, and the "work list"
  framing (`SuiteCoverage.cs:12-15`) is wrong.

---

## Decisions requested

Each is a yes/no the human makes; the recommendation and its one-line reason
follow. None of these is decided.

1. **Ship statement coverage in v0.1?** — *Recommend yes.* The per-statement
   hook already exists and is universal (`Engine.Statements.cs:26`); the
   marginal cost is one seam plus reporting.
2. **Ship branch coverage in v0.1, or declare it the next step?** — *Recommend
   declare-next, not v0.1.* It is only 3–5 local seams, but each needs its own
   correctness rule (empty-ELSE, implicit-CASE-else, zero-iteration loops,
   `EXIT` unwinding by exception) and none of them is load-bearing for the
   agent-loop wedge.
3. **MC-DC out of scope for v0.1, and explicitly NOT seam-proofed?** —
   *Recommend yes to both.* It needs a sub-expression identity model that does
   not exist (`Expr.cs:6-12` rules out line-based identity); a speculative
   partial seam is worse than none, same conclusion `TcXunit-229.7` reached
   about TF1140.
4. **Never emit a tree-wide coverage percentage; report per-POU with an
   explicit `instrumentable` state?** — *Recommend yes.* A percentage whose
   denominator omits unparseable POUs (`CliRunner.cs:209-226`) improves when the
   grammar gets worse.
5. **Subsume the existing `--coverage` by adding fields, keeping `pou` and
   `suites` byte-identical, with no second flag?** — *Recommend yes.* The VSIX
   parses this JSON out-of-process (`xStunit.Vsix.csproj:35`, no
   `ProjectReference`), so additive is free and renames are not; and the two
   signals are only useful together.
6. **Keep "coverage never affects the exit code"?** — *Recommend yes.* Gating is
   a CI product; the agent loop needs the number visible, and exit `2` must keep
   meaning "nothing ran".
7. **Is IEC 61508 / ISO 13849 evidence the commercial thesis for v0.1?** —
   *Recommend **no**, defer and gate on `TcXunit-229.3`.* Not because
   qualification is unaffordable, but because the fidelity claim underneath it
   is currently supported by zero evidence, and the buyer for it looks disjoint
   from the current user.
8. **Bar IEC 61508 / ISO 13849 from all external-facing text until §B1–B3 have
   been checked against the actual standard text?** — *Recommend yes.* Those
   paragraphs are recollection; §A is repo-checkable and §B is not, and the two
   should not be quoted with equal confidence.
9. **Measure the eager-parse denominator pass on a real tree before ratifying
   decision 1?** — *Recommend yes.* K5 is the only kill criterion that can fire
   silently, and it is a half-hour measurement.

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
(`struccpp-notes.md:37-45`) is the reason statement and branch coverage are
cheap here: the statement surface is not the gap. Companion draft:
`docs/research/agent-boundary.md` (`229.10`) — §5b there is the failure mode
this document's statement coverage exists to detect.
