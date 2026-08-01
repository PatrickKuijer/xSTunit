# Agent boundary: where the automatable inner loop ends and the human-gated outer run begins

- Issue: `TcXunit-229.10` (part of epic `TcXunit-229`; blocker `TcXunit-229.9`,
  the result contract, closed; blocks `TcXunit-229.11`, the v0.1 spec
  write-up). Product framing comes from epic `TcXunit-3tx`.
- Date: drafted 2026-07-31, ratified 2026-08-01.
- **STATUS: ratified.** All nine calls were answered; the outcomes, including
  the one amendment (decision 4, the no-progress key), are recorded in the
  "Decisions" section at the end. Section 4 is the load-bearing argument; if
  that reasoning is ever rejected, the rest of the document does not stand.
- Method: repo-grounded, cited to the symbol that owns the behaviour. Two
  deliberate exceptions, both flagged inline: the numeric bounds in §3.4
  (attempt limits) are guesses with no data behind them, and §5 proposes
  detections that do not exist yet.
- **Cite convention.** Cite source by symbol — `CliRunner.WriteError`,
  `FailureKind.LoadError`, `Engine.ExecuteStatement` — never by line number. A
  symbol survives the refactor that moves it and greps to one place; a line
  number is wrong the moment anything above it changes. Cite Markdown by file
  and heading (`README.md § Usage`). For the failure taxonomy specifically the
  live contract is the `kind` table in `README.md` § Usage, not this document.
- **Naming**: the ticket title uses a `Tc`-prefixed label for the agent role.
  Per `CLAUDE.md`'s pre-beta naming constraint, this document does not coin it
  as a product name and refers to "the agent" or "the loop" throughout. The
  ticket label is left as-is; nothing new is built on it.
- **Contract version**: written against the post-`229.9` result contract —
  five `FailureKind`s including `parse-error`, and one vocabulary `kind` /
  `construct` at every JSON level. That contract has since landed
  (`xstunit-229.15`), so the field names below are the live ones. `229.15` also
  narrowed `load-error` to container level and moved body-level parse failures
  to `parse-error`; §3.3 and §3.5 are written to the narrowed meaning.

---

## Verdict in one paragraph

The line falls exactly where the failure taxonomy already puts it, and for a
reason worth stating precisely: `assertion`, `plc-fault` and `load-error` are
statements about the **program**; `unsupported-construct` and `parse-error` are
statements about the **tool**. An agent iterating on the first three is
debugging. An agent iterating on the last two is editing correct ST until the
tool stops complaining — using tool capability as the specification. The
asymmetry that makes this the single most important line in the document is
that every other kind's wrong fix leaves a *failing* test, while this kind's
wrong fix leaves a *passing* one, and the evidence of what was deleted is the
thing that was deleted. But `kind` alone is not a sufficient stop rule, because
the classifier deliberately under-claims `unsupported-construct`
(`FailureClassifier.Classify`; `README.md` § Usage),
so real interpreter gaps are known to be sitting inside the `plc-fault` bucket.
The boundary therefore needs a second, kind-independent net: no-progress
detection plus two zero-cost invariants (total test count must not fall; never
edit a file the runner itself reported as a suite). Outside all of that, the
outer run has an irreducible floor — timing, real I/O, task scheduling and
target-specific semantics are not merely unimplemented but out of category —
and no amount of offline coverage reduces it below one on-target execution.

---

## 1. What the loop actually is, mechanically

`TcXunit-3tx` frames the product role: the agent edits ST, runs the tool,
parses `--format json`, iterates. Concretely, that is:

```
xstunit <paths> --format json     # exit 0 / 1 / 2
```

- **Exit codes.** `0` all pass, `1` any failure, `2` usage or discovery error
  (`CliRunner.Run` and its local `WriteError`; `README.md` § Usage). Per
  `229.9`, `parse-error` inside a loaded suite exits `1` like any other
  failure — it is a failure *of a suite that was found*, not a failure to find
  anything. Unloadable files are skipped and reported per file and never
  produce exit `2` (`CliRunner.Run`'s `StructuralParseGuard.TryParseOrSkip`
  loop; `README.md` § Usage).
- **The blob.** `RunReport` carries `suites[]`, `passed`, `failed`, `exitCode`,
  `skipped[]`, and (under `--coverage`) `coverage[]`
  (`CliRunner.RunReport`).
- **Per suite**: `name`, `filePath`, `error`, `kind`, `construct`, `tests[]`,
  `durationMs`, `fileLine`, `callStack[]` (`CliRunner.SuiteReport`).
- **Per failure**: `message`, `kind`, `construct`, `assert`, `expected`,
  `actual`, `assertMessage`, `pou`, `method`, `bodyLine`, `line`, `callStack[]`
  (`CliRunner.FailureReport`).
- **Narrowing a re-run**: `--suite <name>`, repeatable (`CliRunner.Run`).

One negative worth recording so nobody builds on it: **`--stream` is not for
the agent.** It exists so a UI can render progress
(`CliRunner.DiscoveryEvent`, `CliRunner.SuiteStartEvent`,
`CliRunner.SuiteReport.Outcome`). A full run costs milliseconds, so an
agent gains nothing from incremental events and pays a second parsing code
path. The inner loop should use one-shot `--format json`.

Design constraint on everything below, from `TcXunit-3tx`: **no feature may
require a TwinCAT install, a build, or an Activate Configuration.** Anything
that does turns one iteration from milliseconds into minutes, which does not
slow the loop down — it ends it. This is also what permanently rules out the
on-target runner direction (ADS result streaming, target abstraction, Run-mode
interlocks, HiL rigs): not because it would be bad, but because it is a
different product with a different iteration cost.

---

## 2. Reading the report in the right order

Before any per-kind rule: **branch on `exitCode` first, never on the failure
list.** Exit `2` serializes an `ErrorReport`, which has no `suites` array at
all (`CliRunner.ErrorReport`). An agent that asks "were there any failures?"
gets "no" from a run in which *nothing executed* — the `no TcUnit suites found`
case (`CliRunner.Run`) reads identically to a clean green run if you
only inspect failures. That is the cheapest way to build a loop that reports
success while doing nothing, and it costs one `if` to prevent.

Order: `exitCode == 2` → the invocation or the tree is wrong, nothing ran.
`exitCode == 1` → read `suites[].kind` and `failures[].kind`.
`exitCode == 0` → green, subject to §6b (green may be vacuous).

---

## 3. What the agent may do unsupervised

### 3.1 `assertion` — iterate freely

An assert compared two values and they differed (`FailureKind.Assertion`;
`README.md` § Usage). This is the safest kind, and the reason is not
"assertions are usually simple" — it is that **the failure is fully described
and entirely inside the agent's editing territory**. The report carries the
assert that failed, both formatted values, the author's own message, and the
exact location: `assert`, `expected`, `actual`, `assertMessage`, `pou`,
`method`, `bodyLine`, `line` (`CliRunner.FailureReport`) — "with three asserts
in one method this is what says which of them failed"
(`CliRunner.FailureReport.Pou`). Nothing about the tool's own capability is in
question: it read the code, ran it, compared two values, and told you which
two. Both candidate repairs (the code under test, or the expectation) are
ordinary edits.

### 3.2 `plc-fault` — iterate, but this bucket is not clean

Interpreted ST faulted at run time (`FailureKind.PlcFault`). The classification
is *earned*, not assumed: `FailureClassifier.Classify` returns `PlcFault` only
when some interpreted body claimed the fault, and `LoadError` otherwise —
`return located ? FailureKind.PlcFault : FailureKind.LoadError`
(`FailureClassifier.Classify`). "Located" means an `ExecuteBody` frame stamped
itself onto the exception on the way out (`Engine.ExecuteBody`,
`Engine.RunWithFaultAttribution`, `Engine.RecordFaultSite`). So a `plc-fault`
is evidence that execution really entered ST.

**The caveat, and it is load-bearing:** `unsupported-construct` is claimed
*only* by explicit opt-in at the throw site, never inferred from a base type
(`FailureClassifier.Classify`; `UnsupportedConstructException`). That choice is
correct — the engine throws plain `NotSupportedException` for genuine defects
in the code under test too (`Operator '<' is not supported between Int32 and
String`, `Engine.EvaluateBinary`) and classifying on the base type would halt
an agent over its own bug, the same failure inverted. But the consequence is
stated openly in `README.md` § Usage: *"anything that hasn't opted in reports
as `plc-fault`"*, and the opt-in list grows construct by construct.

So the `plc-fault` bucket is known to contain some number of "the tool cannot
do this" failures wearing "your code is broken" labels. **That is why §3.4's
no-progress rule is not belt-and-braces — it is the only net under this
specific hole.** An agent that iterates indefinitely on a `plc-fault` will, in
some fraction of cases, be iterating on an interpreter gap; bounded attempts
convert that from source corruption into an escalation.

### 3.3 `load-error` — actionable, but on a different surface

Nothing ran, at **container** level: the file or its XML was unreadable, no
suites were discovered, or instantiation failed (`FailureKind.LoadError`;
`README.md` § Usage). Note what is *not* here: a body that failed to parse is
`parse-error`, not `load-error` — that suite loaded, and its other tests ran
and reported their own verdicts (§3.5). Reading a body-level parse failure as
`load-error` would send the agent to fix its invocation over a problem in the
source. The agent's action here is on **its own** surface — paths, directory
sets, duplicate type names across merged directories
(`MultiDirectoryPouLoader.CheckForDuplicates`), a `--suite` name that matches
nothing (`CliRunner.Run`) — not on the code under test. Fixing an invocation
unsupervised is fine.

One case deserves a named rule. If a `load-error` reading `no TcUnit suites
found under …` (`CliRunner.Run`) appears *after* the agent edited a suite, it
is a self-inflicted regression: `SuiteDiscovery` identifies suites purely by
walking `EXTENDS` ancestry to the literal `TcUnit.FB_TestSuite`
(`SuiteDiscovery.IsSuiteType`, `SuiteDiscovery.TestSuiteBaseType`), so touching
a suite's `EXTENDS` line makes it vanish from discovery entirely. **The correct
response is revert, not repair** — and see §6c, because "suites vanish" is the
most rewarding wrong move available to a loop optimizing for "no failures".

### 3.4 What "iterate" is bounded by

Three bounds. The first is the important one; the other two are cheap and
mechanical.

**(a) No-progress detection, independent of `kind`.** Define a failure
identity as the **coarse counter key** `(suite, test, kind)` — for a
suite-level failure, which carries no `test`, `(suite, kind)`. All three
components are already in the JSON (`CliRunner.FailureReport`). After each run,
compare the identity *set* with the previous run's. Progress means the set
strictly shrinks. **N consecutive attempts against the same identity with no
shrinkage → halt and escalate.** This is the second net under §3.2's leaky
bucket, and it is the only mechanism in this document that does not depend on
the tool having classified the failure correctly.

*Why coarse, and why that is the whole point.* The obvious key is the full
location — `pou`, `method`, `bodyLine`, `line` are all on the wire and all
tempting. Every one of them moves under the agent's own edits: inserting three
lines above the failing assert changes `bodyLine`, and lifting the call into a
helper changes `method`. A key containing any of them therefore renders the
*same* stuck failure as a *new* identity, the set appears to have changed, and
the counter resets — the detector is defeated precisely by the churn it exists
to detect, and it fails silently in the permissive direction. `(suite, test,
kind)` is stable across arbitrary edits to the body, which is exactly the
property the counter needs. The cost is real and accepted: a genuinely
different failure in the same test under the same kind — a second assert in
the same method going red once the first is fixed — does not read as progress,
so the loop escalates a case a human would have let continue. That trade is
deliberate: this rule's job is to bound wasted iterations, and its expensive
error is the one that halts, not the one that lets a corrupting loop run.
The location fields stay in the report and stay in the escalation, where a
human reads them; they are just not part of the key.

*The value of N is a guess.* Three is proposed because it allows one
misdiagnosis and one correction; there is no data behind it. It should be
tunable and it should be revisited once real loop traces exist.

**(b) Total test count must not fall.** `passed + failed` are both in the
report (`CliRunner.RunReport.Passed`/`.Failed`), as is `skipped[]`
(`CliRunner.RunReport.Skipped`). A drop in `passed + failed` between iterations
means tests *disappeared* — which is what deleting a test method call, breaking
an `EXTENDS` line, or making a file unparseable all look like from outside.
**Treat any decrease as an automatic halt.** Zero tool cost; the fields already
exist. This one invariant catches three separate cheating routes at once.

**(c) Must-not-touch surfaces.**
- The suite's `EXTENDS` line and its ancestry to `TcUnit.FB_TestSuite`
  (§3.3's reasoning).
- The top-level suite body's list of test-method calls — removing one silently
  reduces the run. Note the engine reads that list structurally
  (`Engine.OpensTestBracket` recognizes `TEST`/`TEST_ORDERED` brackets), so a
  removed call leaves no trace beyond the count in (b).
- Any file the runner itself reported as a suite, when the task is to fix the
  code under test — see §6c, which makes this checkable rather than merely
  stated.

### 3.5 `unsupported-construct` and `parse-error` — nothing, unsupervised

The remaining two of the five kinds appear here only to be excluded, so that a
reader working down section 3 sees the whole vocabulary rather than three of
five and a gap.

- `unsupported-construct` — statement level: a throw site recognized the
  construct *by name* (`FailureKind.UnsupportedConstruct`; `README.md` § Usage).
- `parse-error` — body level: the front end could not read that body at all.
  The suite loaded, and its sibling tests ran and reported their own verdicts,
  which is what separates it from `load-error` (§3.3). It fails a test and
  exits `1`, like any other failure inside a suite that was found.

Both halt. §4 is the argument for why, and §4.3 gives the one narrow exception
— a `parse-error` on a line this session authored.

---

## 4. What must halt: `unsupported-construct` and `parse-error`

This is the section the document exists for.

### 4.1 The structural reason

Three of the five kinds are statements **about the program**:

- `assertion` — "your value was wrong."
- `plc-fault` — "your code faulted while running."
- `load-error` — "I could not find or assemble the container."

Two are statements **about the tool**:

- `unsupported-construct` — "this is valid IEC 61131-3 that TwinCAT compiles
  and I do not implement yet" (`FailureKind.UnsupportedConstruct`;
  `UnsupportedConstructException`).
- `parse-error` — "I could not read this text."

In both of the latter, **the tool has formed no opinion whatsoever about
whether the program is correct.** It never got far enough to have one. The
failure is a report on the tool's own reach.

An agent that "fixes" such a failure is therefore not debugging. It is
searching the space of source edits for one the tool tolerates — which is
using tool capability as the specification. Applied to a real POU, that means
rewriting or deleting correct ST because the interpreter has not grown support
for it yet. This is precisely the scenario the whole taxonomy was built to
prevent; `FailureKind`'s own type doc says so directly: *"a genuine defect in
the code under test, and valid IEC 61131-3 the interpreter has not grown
support for yet. An agent that cannot tell them apart 'fixes' the second by
deleting correct code."* `README.md` § Usage gives the concrete case: an agent
deletes a correct `SEL()` call to make a test pass, and reports success.

### 4.2 Why this specific kind of wrong fix is worse than the others

The asymmetry is what makes STOP the right verb rather than "be careful":

> **Every other kind's wrong fix leaves a failing test. This kind's wrong fix
> leaves a passing one.**

Guess wrong on an `assertion` and the assert still fails — the loop notices.
Guess wrong on a `plc-fault` and it still faults — the loop notices. Delete the
`SEL()` call the interpreter could not handle and the suite goes green, the
exit code goes to `0`, and *the evidence of what was removed is the thing that
was removed*. There is no downstream check that can recover it, because
nothing downstream ever saw it. The damage is silent, permanent, and rewarded
by the loop's own success signal.

That is why the response is not "be conservative" or "prefer smaller edits" but
a hard halt with the construct named. `construct` exists on the wire for
exactly this — "so escalation names it without parsing Error"
(`CliRunner.SuiteReport.Construct`) — and the escalation is a **grammar gap
report**, an input to the interpreter's grow-on-demand backlog, not a code
change.

### 4.3 The honest weakness: the tool is delegating an undecidable call

`229.9`'s message for `parse-error` asks the agent to *"open the cited line,
fix if genuinely malformed, STOP and escalate if it looks like valid ST."*
That asks the model to make the exact judgement the tool refused to make — and
the tool refused for good reason. `Parser.ParseStatementCore` records it:

> *"A ParseException rather than an UnsupportedConstructException: the whole
> IEC 61131-3 statement grammar … is dispatched by name above, so no valid
> construct reaches here. Only malformed source does … and calling that
> unsupported would be a guess."*

The front end "cannot tell syntax it doesn't implement from syntax that is
simply wrong" (`README.md` § Usage). Handing that undecidable classification to
a model that is *also* the author of the recent edits is the weakest joint in
the whole contract, and it should be recorded as such rather than papered over.

**Proposed resolution — an authorship rule, not a judgement rule.** The agent
does not need to know whether the line is valid ST. It knows something better
and entirely local: *did I write it?*

- If the cited `line` / `bodyLine` (`CliRunner.FailureReport.Line`,
  `CliRunner.FailureReport.BodyLine`) falls inside a
  hunk the agent produced in this session, it may repair or revert its own
  edit. That is fixing its own typo, which is unambiguously in scope.
- If the cited line is code the agent did not author, **escalate.** Pre-existing
  ST that the tool cannot read is a grammar-gap candidate by default, and the
  cost of being wrong in that direction is a human reading one line — versus
  corrupting source in the other.

This converts an undecidable semantic question into a decidable provenance
question, using information the agent already has. It is proposed, not settled;
it is decision 3.

### 4.4 The same weakness, mirrored

`unsupported-construct` under-claims (§3.2). `parse-error` over-claims by
construction — it fires on malformed source *and* on beyond-grammar source with
no way to separate them. So the taxonomy is conservative in one direction and
ambiguous in the other, and neither error is fixable by the classifier alone.
**Both are why §3.4(a)'s no-progress rule must be independent of `kind`.** A
boundary drawn purely on labels would be exactly as good as the labels, and the
labels are honestly documented as imperfect.

---

## 5. What the tool can never certify

Not a list of unimplemented features — a list of things outside the category.
Offline ST interpretation, run in milliseconds with no runtime and no hardware,
buys its speed by giving these up. Stating them plainly is what makes the speed
claim credible rather than glib.

**5a. Timing and cycle-time behaviour.** The clock is simulated: a process-wide
monotonic counter advanced only by explicit calls (`Clock.TotalNs`,
`Clock.AdvanceMs`), read by the TON/TOF/pulse hosts when invoked
(`Engine.Clock`, `Engine.CallMethod`). `StepCycles(n)` is a `for` loop
re-invoking a body — *"There is no dt and no scheduler: ordering across several
instances is whatever order the caller invokes them in"* (`Engine.StepCycles`).
A green suite establishes logical sequencing over simulated time. It says
nothing about whether the real task completes inside its cycle budget.

**5b. Task scheduling and multi-task interaction.** Nothing in the engine
models more than one execution context. The interpreter's only call stack is
CLR recursion (`Engine.ExecuteBody`). Task priorities,
preemption, jitter, and cross-task data races are not approximated badly — they
are absent.

**5c. Real I/O and fieldbus.** `Loopback` is a fault-injection *model* of a
transport (`Engine.CallMethod`, `LoopbackHost`), useful for
testing how a POU reacts to drops, delays, duplication and corruption. It is
not a bus, and process-image mapping is not modelled.

**5d. Hardware faults, retain/persistent semantics, online change, power
cycles.** Out of scope entirely; no representation exists.

**5e. Target-specific semantics our interpreter approximates.** These are the
subtle ones, and they are the ones that could make a *green* offline run
misleading rather than merely incomplete:

- Explicit narrowing casts route through `Convert.ToInt32` for every integer
  target width — `SINT` through `LWORD` all take the same branch
  (`Engine.TryEvaluateCast`, `Engine.IntegerCastTargets`). Target-width
  truncation is not modelled at the cast site.
- REAL/LREAL arithmetic runs on CLR `float`/`double`
  (`Engine.EvaluateNumeric`), not the target FPU.
- Pointer arithmetic is documented as *"an approximation for wider element
  types"* (`Engine.EvaluatePointerArithmetic`).
- `_TO_STRING` deliberately makes no attempt at TwinCAT digit/exponent parity
  (`Engine.TryEvaluateCast`).
- GVL initialization models *"no TwinCAT GVL init-cycle/task-binding semantics
  … just zero-initialized storage"* (`Engine._globals`), and an unresolvable
  GVL default is silently swallowed after a bounded retry loop (the
  `Engine(TypeRegistry, NativeFunctionRegistry)` constructor).

`TcXunit-229.3` (the licensed TwinCAT+TcUnit conformance oracle) is the thing
that would put a number on 5e. It is still OPEN, so today the size of that gap
is genuinely unknown — not small, not large: unmeasured.

### 5f. What the outer run must therefore cover, and its minimum

The human-gated outer run irreducibly owns:

1. One on-target (or emulated-target) execution of the same suites, under real
   TwinCAT — which is `TcXunit-229.3`'s oracle doing double duty.
2. Task cycle and cycle-time budget validation.
3. Real I/O mapping and fieldbus behaviour.
4. Anything with retain/persistent or online-change semantics.

**The minimum it can be reduced to:** if and when the oracle exists and runs
green over the tree, the outer run's *logic* obligation collapses — item 1
stops being a per-change task and becomes a standing property of the tool. What
remains is items 2–4, which are physical and temporal and which **no amount of
offline coverage reduces at all**. So the floor is: one on-target execution
plus I/O and timing validation. The honest product statement is that the inner
loop can take a change from "no idea" to "logically correct as far as the
interpreter models the language, in milliseconds" — and that the remaining step
is smaller, but never zero.

---

## 6. Failure modes of the boundary itself

Four, with detections where cheap ones exist. Where none exists, that is said.

**6a. The agent loops on an assertion it cannot satisfy.** Symptom: the same
failure identity across iterations, possibly with growing edits around it —
and "growing edits around it" is exactly why §3.4(a)'s key excludes the
location fields those edits move. *Detection: §3.4(a), the identity-set
no-progress rule.* Entirely agent-side, no tool change, and it also covers the
leaky `plc-fault` bucket from §3.2.

**6b. A suite is green because it never actually executed the POU.** The most
dangerous quiet failure, because it produces exit `0`. Today's `--coverage`
does not catch it: association is by *textual* reference — a whole-word,
case-insensitive regex of the POU type name against the suite's
comment-stripped text (`SuiteCoverage.MentionsType`, `SuiteCoverage.AllText`).
A suite that declares `VAR fb : FB_X;` and never calls it reads as covered.
**There is no cheap interim detection**, and pretending otherwise would be
worse than admitting it.

This is exactly what statement coverage exists to fix. See
`docs/research/coverage-wedge.md` §A1 and §A5: the per-statement hook already
exists (`Engine.ExecuteStatement`), and the useful signal is the two
coverage kinds *together* — "referenced by a suite, zero statements executed"
is the green-but-vacuous case rendered as one field. That is the strongest
argument in either document for shipping statement coverage in v0.1, and it is
an argument from the agent loop, not from safety evidence.

**6c. The agent edits the test instead of the code to get green.** The runner
cannot see this — it has no notion of which files the agent touched — so
detection must live in the harness. Two cheap ones, both using data already on
the wire:

- **Diff-scope check.** `suites[].filePath` (`CliRunner.SuiteReport.FilePath`)
  is the on-disk path of every discovered suite, and `--stream`'s discovery
  event carries the same pairing (`CliRunner.SuiteDiscoveryEntry`). So the
  harness can compute "did I modify a file the runner reported as a suite?"
  with zero tool change. When the task is "fix the code under test", that is a
  review trigger, not necessarily a violation — sometimes the expectation
  genuinely was wrong — but it must be surfaced, never silent.
- **Count invariant.** §3.4(b): `passed + failed` must not fall
  (`CliRunner.RunReport.Passed`/`.Failed`). This catches deleted tests,
  tampered `EXTENDS` lines (which make suites vanish from discovery,
  `SuiteDiscovery.IsSuiteType`), and files that became unloadable and joined
  `skipped[]` (`CliRunner.RunReport.Skipped`) — three cheating routes, one
  comparison.

**6d. Exit-code confusion read as success.** §2. `ErrorReport` has no `suites`
array (`CliRunner.ErrorReport`), so "no failures found" is the literal truth of
a run in which nothing ran. Detection is the `exitCode`-first branch; cost is
one `if`.

---

## 7. Summary table — the deliverable

| `kind` | What it is a statement about | Agent action | Escalation trigger |
| --- | --- | --- | --- |
| `assertion` | The program's values | Iterate unsupervised: fix the code under test, or the expectation | No-progress on the same coarse key `(suite, test, kind)` after N attempts (§3.4a) |
| `plc-fault` | The program's runtime behaviour | Iterate unsupervised: fix the code under test | Same as above — **and this bucket is known to contain real interpreter gaps** (`README.md` § Usage), so the no-progress rule is the primary net, not a backstop |
| `unsupported-construct` | **The tool's reach** | **STOP.** Never rewrite the POU. Report the named `construct` as a grammar gap | Immediate, on first occurrence |
| `parse-error` | **The tool's reach** | **STOP by default.** Body level: that body never ran, its siblings did. May repair only if the cited line falls inside a hunk this session authored (§4.3) | Immediate, unless agent-authored |
| `load-error` | The invocation or the tree | Container level: fix paths / directory set / `--suite` name. Never edit POUs for this | Immediate if it followed an edit to a suite's `EXTENDS` line — revert, don't repair (§3.3) |

| Exit code | Meaning | Agent action |
| --- | --- | --- |
| `0` | All passed | Green — subject to §6b (green may be vacuous) |
| `1` | At least one failure inside a loaded suite (including `parse-error`) | Read `kind` per row above |
| `2` | Usage or discovery error — **nothing ran** | Never read as success; fix the invocation (§2) |

Loop-level invariants, independent of `kind`:

- `passed + failed` must never decrease between iterations → halt (§3.4b).
- Never modify a file reported in `suites[].filePath` when the task is to fix
  the code under test → review trigger (§6c).
- Branch on `exitCode` before inspecting failures (§2).

---

## Decisions — all answered 2026-07-31, one amended

Every call below was accepted. One was amended in ratification (4); one is
accepted but not yet carried out (7), and is tracked rather than assumed done.

1. **`assertion` and `plc-fault` are the only kinds an agent may iterate on
   unsupervised.** *Accepted.* Both are statements about the program and carry
   complete, locally-actionable detail (`CliRunner.FailureReport`).
2. **`load-error` is agent-actionable on the invocation/tree surface only,
   never by editing POUs.** *Accepted.* Nothing ran, so nothing about the code
   under test has been established either way (`FailureKind.LoadError`;
   `README.md` § Usage).
3. **The authorship rule for `parse-error` — repair only lines inside a hunk
   this session authored, escalate otherwise.** *Accepted.* It replaces an
   undecidable semantic judgement the tool itself declined to make
   (`Parser.ParseStatementCore`) with a decidable provenance question the
   agent can answer locally.
4. **No-progress detection as a halt condition independent of `kind`.**
   *Accepted, amended.* The draft keyed the counter on the full location; the
   ratified key is the coarse `(suite, test, kind)`, because every location
   field moves under the agent's own edits and would reset the counter (§3.4a
   carries the reasoning and the accepted cost). It is the only net under the
   known leak in the `plc-fault` bucket (`README.md` § Usage); N=3 remains an
   explicit guess and should be tunable.
5. **"Total test count must not decrease" as a hard loop invariant.**
   *Accepted.* Zero tool cost, fields already on the wire
   (`CliRunner.RunReport.Passed`/`.Failed`), and it catches three separate
   cheating routes at once.
6. **"Never edit a file the runner reported as `suites[].filePath` when the
   task is to fix the code under test", as a review trigger.** *Accepted as a
   trigger, not a prohibition.* Zero tool cost
   (`CliRunner.SuiteReport.FilePath`); sometimes the expectation genuinely is
   the bug, so it must surface rather than block.
7. **Publish §5's never-certifies list as an external product statement
   (README / docs), not just an internal note.** *Accepted, not yet done.* The
   honesty is what makes the milliseconds claim credible. It is a `README.md`
   change, outside this document's territory; tracked as `xstunit-kr5i`. Until
   that lands, §5 is the only place the list exists.
8. **§5f's outer-run floor — one on-target execution plus I/O and timing
   validation, irreducible, with `xstunit-229.3`'s oracle as what collapses the
   logic half.** *Accepted.* It is the honest limit, and stating it is what
   stops the inner loop being oversold.
9. **`--stream` is explicitly not part of the agent contract.** *Accepted.* It
   exists for UI progress (`CliRunner.DiscoveryEvent`); a milliseconds-long
   run gains nothing from incremental events and pays a second parsing path.

---

## Related research

Companion draft: `docs/research/coverage-wedge.md` (`TcXunit-229.8`) — §6b here
is the failure mode that document's statement coverage exists to detect, and
the cross-reference runs both ways. `docs/research/tf1140-audit.md` and
`docs/research/struccpp-notes.md` (`229.2`) established the
compatibility-target shape (`229.7`: TcUnit shape only, no TF1140 for v0.1),
which is what lets this document assume one discovery convention and one
assertion vocabulary. `docs/research/grammar-source-audit.md` (`229.5`) feeds
`229.6`'s parse-superset/execute-time split — the split that makes
`parse-error` a nameable kind at all, and therefore what makes §4.3's
authorship rule expressible rather than hypothetical.
