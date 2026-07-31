# Agent boundary: where the automatable inner loop ends and the human-gated outer run begins

- Issue: `TcXunit-229.10` (part of epic `TcXunit-229`; blocker `TcXunit-229.9`,
  the result contract, closed; blocks `TcXunit-229.11`, the v0.1 spec
  write-up). Product framing comes from epic `TcXunit-3tx`.
- Date: 2026-07-31.
- **STATUS: DRAFT — awaiting ratification.** The boundary proposed here is a
  proposal. Section 4 is the load-bearing argument; if that reasoning is
  rejected, the rest of the document does not stand.
- Method: repo-grounded, cited to `path/file.cs:line`. Two deliberate
  exceptions, both flagged inline: the numeric bounds in §3.4 (attempt limits)
  are guesses with no data behind them, and §5 proposes detections that do not
  exist yet.
- **Naming**: the ticket title uses a `Tc`-prefixed label for the agent role.
  Per `CLAUDE.md`'s pre-beta naming constraint, this document does not coin it
  as a product name and refers to "the agent" or "the loop" throughout. The
  ticket label is left as-is; nothing new is built on it.
- **Contract version**: written against the post-`229.9` result contract —
  five `FailureKind`s including `parse-error`, and one vocabulary `kind` /
  `construct` at every JSON level. At the time of writing this worktree still
  carries the pre-change shape (four kinds in
  `src/xStunit.Runner/FailureKind.cs:20-48`; suite-level `errorKind` /
  `errorConstruct` at `src/xStunit.Cli/CliRunner.cs:896,902`). That rename is
  landing concurrently and is treated here as done. Line citations to
  `CliRunner.cs` are to the pre-rename file; the field *names* below are
  post-rename.

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
(`src/xStunit.Interpreter/Engine/FailureClassifier.cs:15-24`; `README.md:62`),
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
tcxunit <paths> --format json     # exit 0 / 1 / 2
```

- **Exit codes.** `0` all pass, `1` any failure, `2` usage or discovery error
  (`CliRunner.cs:515`, `:139-149`, `:165-188`; `README.md:43`). Per `229.9`,
  `parse-error` inside a loaded suite exits `1` like any other failure — it is
  a failure *of a suite that was found*, not a failure to find anything.
  Unloadable files are skipped and reported per file and never produce exit `2`
  (`CliRunner.cs:209-226,509-515`; `README.md:45`).
- **The blob.** `RunReport` carries `suites[]`, `passed`, `failed`, `exitCode`,
  `skipped[]`, and (under `--coverage`) `coverage[]`
  (`CliRunner.cs:734-777`).
- **Per suite**: `name`, `filePath`, `error`, `kind`, `construct`, `tests[]`,
  `durationMs`, `fileLine`, `callStack[]` (`CliRunner.cs:838-927`).
- **Per failure**: `message`, `kind`, `construct`, `assert`, `expected`,
  `actual`, `assertMessage`, `pou`, `method`, `bodyLine`, `line`, `callStack[]`
  (`CliRunner.cs:980-1056`).
- **Narrowing a re-run**: `--suite <name>`, repeatable
  (`CliRunner.cs:110-122,294-303`).

One negative worth recording so nobody builds on it: **`--stream` is not for
the agent.** It exists so a UI can render progress
(`CliRunner.cs:60-72,346-362,497-506`). A full run costs milliseconds, so an
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
all (`CliRunner.cs:674-713`). An agent that asks "were there any failures?"
gets "no" from a run in which *nothing executed* — the `no TcUnit suites found`
case (`CliRunner.cs:318-319`) reads identically to a clean green run if you
only inspect failures. That is the cheapest way to build a loop that reports
success while doing nothing, and it costs one `if` to prevent.

Order: `exitCode == 2` → the invocation or the tree is wrong, nothing ran.
`exitCode == 1` → read `suites[].kind` and `failures[].kind`.
`exitCode == 0` → green, subject to §5b.

---

## 3. What the agent may do unsupervised

### 3.1 `assertion` — iterate freely

An assert compared two values and they differed
(`src/xStunit.Runner/FailureKind.cs:22-26`). This is the safest kind, and the
reason is not "assertions are usually simple" — it is that **the failure is
fully described and entirely inside the agent's editing territory**. The report
carries the assert that failed, both formatted values, the author's own
message, and the exact location: `assert`, `expected`, `actual`,
`assertMessage`, `pou`, `method`, `bodyLine`, `line`
(`CliRunner.cs:1021-1047`) — "with three asserts in one method this is what
says which of them failed" (`:1036-1039`). Nothing about the tool's own
capability is in question: it read the code, ran it, compared two values, and
told you which two. Both candidate repairs (the code under test, or the
expectation) are ordinary edits.

### 3.2 `plc-fault` — iterate, but this bucket is not clean

Interpreted ST faulted at run time (`FailureKind.cs:28-33`). The
classification is *earned*, not assumed: `FailureClassifier.Classify` returns
`PlcFault` only when some interpreted body claimed the fault, and `LoadError`
otherwise — `return located ? FailureKind.PlcFault : FailureKind.LoadError`
(`FailureClassifier.cs:52-54`). "Located" means an `ExecuteBody` frame stamped
itself onto the exception on the way out
(`Engine/Engine.Diagnostics.cs:37-59,205-228`). So a `plc-fault` is evidence
that execution really entered ST.

**The caveat, and it is load-bearing:** `unsupported-construct` is claimed
*only* by explicit opt-in at the throw site, never inferred from a base type
(`FailureClassifier.cs:15-24`;
`src/xStunit.Runner/UnsupportedConstructException.cs:9-22`). That choice is
correct — the engine throws plain `NotSupportedException` for genuine defects
in the code under test too (`Operator '<' is not supported between Int32 and
String`, `Engine.Expressions.cs:221-224`) and classifying on the base type
would halt an agent over its own bug, the same failure inverted. But the
consequence is stated openly in `README.md:62`: *"some real interpreter gaps
… still read as `plc-fault`; widening the opt-in is incremental work."*

So the `plc-fault` bucket is known to contain some number of "the tool cannot
do this" failures wearing "your code is broken" labels. **That is why §3.4's
no-progress rule is not belt-and-braces — it is the only net under this
specific hole.** An agent that iterates indefinitely on a `plc-fault` will, in
some fraction of cases, be iterating on an interpreter gap; bounded attempts
convert that from source corruption into an escalation.

### 3.3 `load-error` — actionable, but on a different surface

Nothing ran: discovery, parsing or instantiation failed before or outside any
interpreted body (`FailureKind.cs:42-47`). The agent's action is on **its own**
surface — paths, directory sets, duplicate type names across merged directories
(`CliRunner.cs:232-239`), a `--suite` name that matches nothing
(`CliRunner.cs:294-299`) — not on the code under test. Fixing an invocation
unsupervised is fine.

One case deserves a named rule. If a `load-error` reading
`no TcUnit suites found under …` (`CliRunner.cs:318-319`) appears *after* the
agent edited a suite, it is a self-inflicted regression: `SuiteDiscovery`
identifies suites purely by walking `EXTENDS` ancestry to the literal
`TcUnit.FB_TestSuite`
(`src/xStunit.Interpreter/Discovery/SuiteDiscovery.cs:11,16-32`), so touching a
suite's `EXTENDS` line makes it vanish from discovery entirely. **The correct
response is revert, not repair** — and see §5c, because "suites vanish" is the
most rewarding wrong move available to a loop optimizing for "no failures".

### 3.4 What "iterate" is bounded by

Three bounds. The first is the important one; the other two are cheap and
mechanical.

**(a) No-progress detection, independent of `kind`.** Define a failure
identity as `(suite, test, pou, method, bodyLine, kind)` — every component is
already in the JSON (`CliRunner.cs:1015-1047`). After each run, compare the
identity *set* with the previous run's. Progress means the set strictly
shrinks. **N consecutive attempts against the same identity with no shrinkage →
halt and escalate.** This is the second net under §3.2's leaky bucket, and it
is the only mechanism in this document that does not depend on the tool having
classified the failure correctly.

*The value of N is a guess.* Three is proposed because it allows one
misdiagnosis and one correction; there is no data behind it. It should be
tunable and it should be revisited once real loop traces exist.

**(b) Total test count must not fall.** `passed + failed` are both in the
report (`CliRunner.cs:760-763`), as is `skipped[]` (`:768`). A drop in
`passed + failed` between iterations means tests *disappeared* — which is what
deleting a test method call, breaking an `EXTENDS` line, or making a file
unparseable all look like from outside. **Treat any decrease as an automatic
halt.** Zero tool cost; the fields already exist. This one invariant catches
three separate cheating routes at once.

**(c) Must-not-touch surfaces.**
- The suite's `EXTENDS` line and its ancestry to `TcUnit.FB_TestSuite`
  (§3.3's reasoning).
- The top-level suite body's list of test-method calls — removing one silently
  reduces the run. Note the engine reads that list structurally
  (`Engine/Engine.Diagnostics.cs:169-172` recognizes `TEST`/`TEST_ORDERED`
  brackets), so a removed call leaves no trace beyond the count in (b).
- Any file the runner itself reported as a suite, when the task is to fix the
  code under test — see §5c, which makes this checkable rather than merely
  stated.

---

## 4. What must halt: `unsupported-construct` and `parse-error`

This is the section the document exists for.

### 4.1 The structural reason

Three of the five kinds are statements **about the program**:

- `assertion` — "your value was wrong."
- `plc-fault` — "your code faulted while running."
- `load-error` — "I could not find or assemble the tree."

Two are statements **about the tool**:

- `unsupported-construct` — "this is valid IEC 61131-3 that TwinCAT compiles
  and I do not implement yet" (`FailureKind.cs:35-40`;
  `UnsupportedConstructException.cs:5-8`).
- `parse-error` — "I could not read this text."

In both of the latter, **the tool has formed no opinion whatsoever about
whether the program is correct.** It never got far enough to have one. The
failure is a report on the tool's own reach.

An agent that "fixes" such a failure is therefore not debugging. It is
searching the space of source edits for one the tool tolerates — which is
using tool capability as the specification. Applied to a real POU, that means
rewriting or deleting correct ST because the interpreter has not grown support
for it yet. This is precisely the scenario the whole taxonomy was built to
prevent; `FailureKind.cs:9-15` says so directly: *"a genuine defect in the code
under test, and valid IEC 61131-3 the interpreter has not grown support for
yet. An agent that cannot tell them apart 'fixes' the second by deleting
correct code."* `README.md:60` gives the concrete case: an agent deletes a
correct `SEL()` call to make a test pass, and reports success.

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
(`CliRunner.cs:898-902`) — and the escalation is a **grammar gap report**, an
input to the interpreter's grow-on-demand backlog, not a code change.

### 4.3 The honest weakness: the tool is delegating an undecidable call

`229.9`'s message for `parse-error` asks the agent to *"open the cited line,
fix if genuinely malformed, STOP and escalate if it looks like valid ST."*
That asks the model to make the exact judgement the tool refused to make — and
the tool refused for good reason. `Parser.cs:143-151` records it:

> *"Left as FormatException, not converted (TcXunit-3tx.5): … There is no known
> valid construct that reaches this fallback only malformed source does … so
> claiming unsupported-construct here would be a guess, not evidence."*

The front end "cannot tell syntax it doesn't implement from syntax that is
simply wrong" (`README.md:62`). Handing that undecidable classification to a
model that is *also* the author of the recent edits is the weakest joint in the
whole contract, and it should be recorded as such rather than papered over.

**Proposed resolution — an authorship rule, not a judgement rule.** The agent
does not need to know whether the line is valid ST. It knows something better
and entirely local: *did I write it?*

- If the cited `line` / `bodyLine` (`CliRunner.cs:1042-1047`) falls inside a
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

**5a. Timing and cycle-time behaviour.** The clock is simulated: a
process-wide monotonic counter advanced only by explicit calls
(`src/xStunit.Interpreter/Hosts/Clock.cs:9-14`), read by the TON/TOF/pulse
hosts when invoked (`Engine.cs:26-28`, `Engine.Invocation.cs:88-90`).
`StepCycles(n)` is a `for` loop re-invoking a body — *"No dt/scheduler: caller
controls ordering across multiple instances by choosing call order"*
(`Engine/Engine.Convergence.cs:8-11,18-37`). A green suite establishes logical
sequencing over simulated time. It says nothing about whether the real task
completes inside its cycle budget.

**5b. Task scheduling and multi-task interaction.** Nothing in the engine
models more than one execution context. The interpreter's only call stack is
CLR recursion (`Engine/Engine.Diagnostics.cs:17-20`). Task priorities,
preemption, jitter, and cross-task data races are not approximated badly — they
are absent.

**5c. Real I/O and fieldbus.** `Loopback` is a fault-injection *model* of a
transport (`Engine.Invocation.cs:123-154`, `Hosts/LoopbackHost.cs`), useful for
testing how a POU reacts to drops, delays, duplication and corruption. It is
not a bus, and process-image mapping is not modelled.

**5d. Hardware faults, retain/persistent semantics, online change, power
cycles.** Out of scope entirely; no representation exists.

**5e. Target-specific semantics our interpreter approximates.** These are the
subtle ones, and they are the ones that could make a *green* offline run
misleading rather than merely incomplete:

- Explicit narrowing casts route through `Convert.ToInt32` for every integer
  target width — `SINT` through `LWORD` all take the same branch
  (`Engine.Expressions.cs:754-757,779-780`). Target-width truncation is not
  modelled at the cast site.
- REAL/LREAL arithmetic runs on CLR `float`/`double`
  (`Engine.Expressions.cs:523-551`), not the target FPU.
- Pointer arithmetic is documented as *"an approximation for wider element
  types"* (`Engine.Expressions.cs:271-286`).
- `_TO_STRING` deliberately makes no attempt at TwinCAT digit/exponent parity
  (`Engine.Expressions.cs:781-796`).
- GVL initialization models *"no TwinCAT GVL init-cycle/task-binding semantics
  … just zero-initialized storage"* (`Engine.cs:19-22`), and an unresolvable
  GVL default is silently swallowed after a bounded retry loop
  (`Engine.cs:93-105`).

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
failure identity across iterations, possibly with growing edits around it.
*Detection: §3.4(a), the identity-set no-progress rule.* Entirely agent-side,
no tool change, and it also covers the leaky `plc-fault` bucket from §3.2.

**6b. A suite is green because it never actually executed the POU.** The most
dangerous quiet failure, because it produces exit `0`. Today's `--coverage`
does not catch it: association is by *textual* reference — a whole-word,
case-insensitive regex of the POU type name against the suite's
comment-stripped text
(`src/xStunit.Interpreter/Discovery/SuiteCoverage.cs:41-51,69-70`). A suite
that declares `VAR fb : FB_X;` and never calls it reads as covered. **There is
no cheap interim detection**, and pretending otherwise would be worse than
admitting it.

This is exactly what statement coverage exists to fix. See
`docs/research/coverage-wedge.md` §A1 and §A5: the per-statement hook already
exists (`Engine/Engine.Statements.cs:26`), and the useful signal is the two
coverage kinds *together* — "referenced by a suite, zero statements executed"
is the green-but-vacuous case rendered as one field. That is the strongest
argument in either document for shipping statement coverage in v0.1, and it is
an argument from the agent loop, not from safety evidence.

**6c. The agent edits the test instead of the code to get green.** The runner
cannot see this — it has no notion of which files the agent touched — so
detection must live in the harness. Two cheap ones, both using data already on
the wire:

- **Diff-scope check.** `suites[].filePath` (`CliRunner.cs:884`) is the on-disk
  path of every discovered suite, and `--stream`'s discovery event carries the
  same pairing (`CliRunner.cs:356-361,799-809`). So the harness can compute
  "did I modify a file the runner reported as a suite?" with zero tool change.
  When the task is "fix the code under test", that is a review trigger, not
  necessarily a violation — sometimes the expectation genuinely was wrong — but
  it must be surfaced, never silent.
- **Count invariant.** §3.4(b): `passed + failed` must not fall
  (`CliRunner.cs:760-763`). This catches deleted tests, tampered `EXTENDS`
  lines (which make suites vanish from discovery,
  `SuiteDiscovery.cs:11,16-32`), and files that became unloadable and joined
  `skipped[]` (`CliRunner.cs:768`) — three cheating routes, one comparison.

**6d. Exit-code confusion read as success.** §2. `ErrorReport` has no `suites`
array (`CliRunner.cs:674-713`), so "no failures found" is the literal truth of
a run in which nothing ran. Detection is the `exitCode`-first branch; cost is
one `if`.

---

## 7. Summary table — the deliverable

| `kind` | What it is a statement about | Agent action | Escalation trigger |
| --- | --- | --- | --- |
| `assertion` | The program's values | Iterate unsupervised: fix the code under test, or the expectation | No-progress on the same failure identity after N attempts (§3.4a) |
| `plc-fault` | The program's runtime behaviour | Iterate unsupervised: fix the code under test | Same as above — **and this bucket is known to contain real interpreter gaps** (`README.md:62`), so the no-progress rule is the primary net, not a backstop |
| `unsupported-construct` | **The tool's reach** | **STOP.** Never rewrite the POU. Report the named `construct` as a grammar gap | Immediate, on first occurrence |
| `parse-error` | **The tool's reach** | **STOP by default.** May repair only if the cited line falls inside a hunk this session authored (§4.3) | Immediate, unless agent-authored |
| `load-error` | The invocation or the tree | Fix paths / directory set / `--suite` name. Never edit POUs for this | Immediate if it followed an edit to a suite's `EXTENDS` line — revert, don't repair (§3.3) |

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

## Decisions requested

Each is a yes/no; recommendation and one-line reasoning follow. None is
decided.

1. **Ratify `assertion` and `plc-fault` as the only kinds an agent may iterate
   on unsupervised?** — *Recommend yes.* Both are statements about the program
   and carry complete, locally-actionable detail (`CliRunner.cs:1021-1047`).
2. **Ratify `load-error` as agent-actionable on the invocation/tree surface
   only, never by editing POUs?** — *Recommend yes.* Nothing ran, so nothing
   about the code under test has been established either way
   (`FailureKind.cs:42-47`).
3. **Adopt the authorship rule for `parse-error` — repair only lines inside a
   hunk this session authored, escalate otherwise?** — *Recommend yes.* It
   replaces an undecidable semantic judgement the tool itself declined to make
   (`Parser.cs:143-151`) with a decidable provenance question the agent can
   answer locally.
4. **Adopt no-progress detection as a halt condition independent of `kind`?** —
   *Recommend yes.* It is the only net under the known leak in the `plc-fault`
   bucket (`README.md:62`); N=3 is an explicit guess and should be tunable.
5. **Adopt "total test count must not decrease" as a hard loop invariant?** —
   *Recommend yes.* Zero tool cost, fields already on the wire
   (`CliRunner.cs:760-763`), and it catches three separate cheating routes at
   once.
6. **Adopt "never edit a file the runner reported as `suites[].filePath` when
   the task is to fix the code under test", as a review trigger?** — *Recommend
   yes, as a trigger rather than a prohibition.* Zero tool cost
   (`CliRunner.cs:884`); sometimes the expectation genuinely is the bug, so it
   must surface rather than block.
7. **Publish §5's never-certifies list as an external product statement (README
   / docs), not just an internal note?** — *Recommend yes.* The honesty is what
   makes the milliseconds claim credible. Note this is a `README.md` change and
   is outside this draft's territory — it is proposed, not made.
8. **Ratify §5f's outer-run floor — one on-target execution plus I/O and timing
   validation, irreducible, with `TcXunit-229.3`'s oracle as what collapses the
   logic half?** — *Recommend yes.* It is the honest limit, and stating it is
   what stops the inner loop being oversold.
9. **Confirm that `--stream` is explicitly not part of the agent contract?** —
   *Recommend yes.* It exists for UI progress (`CliRunner.cs:60-72`); a
   milliseconds-long run gains nothing from incremental events and pays a
   second parsing path.

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
