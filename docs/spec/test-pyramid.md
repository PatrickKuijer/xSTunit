# xStunit v0.1 test pyramid

- Issue: `xstunit-229.17` (part of epic `xstunit-229`; blockers `xstunit-229.10`
  and `xstunit-229.8` closed; blocks `xstunit-229.18`, the kill criteria).
- Date: 2026-08-02.
- **STATUS: spec.** This is the wording that ships, not a draft awaiting
  ratification. It *carries* decisions already ratified in
  `docs/research/agent-boundary.md` (`229.10`) and
  `docs/research/coverage-wedge.md` (`229.8`) and adds none of its own; where
  those two disagree with this document, they are the source and this is the
  bug. Companion: `docs/spec/non-goals.md`.
- Cite convention, inherited from the two source documents: cite code by symbol
  — `Engine.StepCycles`, `Clock.TotalNs` — never by line number. A symbol
  survives the refactor that moves it; a line number is wrong the moment
  anything above it changes.

---

## What the pyramid is for

Three layers, each named by **what it can establish and what it cannot**. The
point is not to rank the layers by cost — it is so that a reader with a question
can tell which layer answers it, and stop asking the offline layers for answers
they are structurally unable to give. A green run at L0 or L1 is not a weaker
version of an on-target run; it is an answer to a different question.

Below the pyramid sits a **conformance floor** that is deliberately not a layer:
it tests xStunit against TwinCAT rather than testing anyone's PLC code.

```
  L2   On-target run, human-gated          real TwinCAT, real I/O, real time
  ---------------------------------------------------------------------------
  L1   Simulated-time sequencing           offline, agent loop, milliseconds
  L0   Pure logic                          offline, agent loop, milliseconds
  ===========================================================================
       conformance floor                   Grade A .tmc fixtures (exists),
       (xStunit's own CI, not yours)       Grade B verdict diff (does not)
```

---

## L0 — Pure logic, offline

**What it tests.** Computation and state transitions with no notion of elapsed
time: scalar and struct arithmetic, array handling, guard conditions, and
scan-cycle state machines driven one invocation at a time.

**Mechanism.** One-shot `xstunit <paths> --format json`, exit `0`/`1`/`2`
(`CliRunner.Run`; `README.md` § Usage). Milliseconds per run, no TwinCAT
install, no build, no Activate Configuration.

**Who drives it.** The agent, unsupervised, under the boundary in
`docs/research/agent-boundary.md` § 3: iterate freely on `assertion` and
`plc-fault`; **stop** on `unsupported-construct` and `parse-error`, which are
statements about the tool's reach rather than about the program. The loop-level
invariants apply here and at L1 — branch on `exitCode` before inspecting
failures, never let `passed + failed` decrease, halt after N attempts against an
unchanged `(suite, test, kind)` identity set.

**What a green L0 run establishes.** The code computes what the test says it
computes, *as far as the interpreter models the language* — which is the clause
`docs/spec/non-goals.md` § 4 exists to keep honest.

**What it cannot establish.** Anything involving a clock (L1 at best), anything
involving more than one execution context (L2 only), anything whose outcome
turns on target-specific arithmetic (unmeasured — see the floor).

---

## L1 — Sequencing over simulated time, offline

L1 is **not a separate run**. It is the same invocation, the same JSON, and the
same agent loop as L0; the split is by what a test establishes, not by tooling.

**Mechanism.** A shared simulated clock holding a monotonic nanosecond total
(`Clock.TotalNs`), advanced *only* by explicit calls (`Clock.AdvanceMs`,
`Clock.AdvanceNs`) and read by the TON/TOF/TP and LTON/LTOF/LTP hosts when they
are invoked (`Engine.CallMethod`, `TimerHost.Update`). `StepCycles(n)`
re-invokes an instance's top-level body n times, reusing its Cell state across
calls (`Engine.StepCycles`). `Loopback` injects transport faults — drops,
delays, duplication, corruption (`LoopbackHost`).

**What a green L1 run establishes.** Logical ordering over simulated time: that
after n cycles and t simulated milliseconds the FB is in the expected state;
that a TON with `PT := T#500MS` fires once the simulated total crosses it; that
a POU degrades correctly when the transport misbehaves.

**What it cannot establish, and why it is structural rather than a gap.**
`Engine.StepCycles` says it in its own comment: *"There is no dt and no
scheduler: ordering across several instances is whatever order the caller
invokes them in."* A cycle costs **zero** simulated time unless a test advances
the clock. So cycle-time budget, jitter, overrun and inter-task ordering are not
merely unmodelled here — they are unaskable. A test whose verdict changes when
two instances are stepped in a different order is asking L2's question at L1,
and its green is meaningless rather than merely limited.

---

## L2 — On-target run under real TwinCAT, human-gated

The outer run irreducibly owns four things (`agent-boundary.md` § 5f):

1. One on-target (or emulated-target) execution of the same suites.
2. Task cycle and cycle-time budget validation.
3. Real I/O mapping and fieldbus behaviour.
4. Anything with retain/persistent, online-change or power-cycle semantics.

**The floor, stated as a number.** If a semantics oracle ever exists and runs
green over the tree, item 1 stops being a per-change task and becomes a standing
property of the tool. Items 2–4 are physical and temporal, and **no amount of
offline coverage reduces them at all.** So the minimum is one on-target
execution plus I/O and timing validation. The honest product claim is that the
inner loop takes a change from "no idea" to "logically correct as far as the
interpreter models the language" in milliseconds — and that what remains is
smaller, but never zero.

---

## The conformance floor — not a layer

It is excluded from the pyramid on purpose: **it does not test the code under
test.** It measures xStunit against TwinCAT, it is run by xStunit's own CI, and
it is not per-change work for anyone using the tool. It is what makes L0 and L1
believable at all.

Two grades, with different costs and different gates (`xstunit-229.3`):

- **Grade A — layout. Exists.** Needs no license: a TwinCAT XAE *Build* (never
  an Activate) emits a `.tmc` carrying per-member `BitOffs`/`BitSize`, pointer
  width, `STRING(n)` sizes, enum base type and array info. Committed as golden
  fixtures; harness `xstunit-229.19`. It has already been run —
  `xstunit-229.12` measured 53 types and 145 members on both targets.
- **Grade B — semantics. Does not exist.** Needs the runtime. Mechanism is a
  verdict diff of the same TcUnit suites normalised to
  `(suite, test, verdict, message)`; staged as a 7-day trial pilot
  (`xstunit-229.20`) behind a license spend owned outside this repo. Value-trace
  diffing is deliberately not built.

**What the pyramid therefore claims today: nothing that needs either grade.**
Grade B is unbuilt, so the size of the semantic gap in
`docs/spec/non-goals.md` § 4 is unmeasured — not small, not large. And Grade A's
result does **not** license a public layout-conformance claim while the x64
pointer-width defect (`xstunit-229.30`) and the union-layout defect
(`xstunit-229.31`) are open; when such a claim lands it must name BIT sub-byte
offsets as unmodeled and the pack-write half as unverified (`xstunit-229.33`).

---

## Where coverage sits

Statement coverage ships in v0.1 (`coverage-wedge.md` decision 1), reported
per-POU alongside the existing reference list, **never as a tree-wide
percentage**, and **never affecting the exit code**.

It is not a layer — it is an instrument on L0 and L1, and it exists for one
specific job: it is the only check on those layers' worst failure mode, a suite
that is green because it never executed the POU at all
(`agent-boundary.md` § 6b). Today's `--coverage` cannot catch that, because
association is textual — a suite declaring `VAR fb : FB_X;` and never calling it
reads as covered (`SuiteCoverage.MentionsType`). "Referenced by a suite, zero
statements executed" is that failure rendered as one field, and it is the
strongest argument for shipping statement coverage in v0.1.

Branch coverage is declared-next, not v0.1. MC-DC is out of scope and explicitly
not seam-proofed (decisions 2 and 3).

---

## Which layer answers which question

| Question | Layer |
| --- | --- |
| Does this compute the right value? | L0 |
| Does this state machine reach state X after n cycles? | L1 |
| Does this TON fire after 500 ms of simulated time? | L1 |
| Does this POU survive a dropped/duplicated message? | L1 |
| Does this fit inside the 10 ms task cycle? | **L2 only** |
| Do these two tasks race? | **L2 only** |
| Does the output actually reach the drive? | **L2 only** |
| Do my retains survive a power cycle? | **L2 only** |
| Does my struct's byte layout match what the compiler emits? | Conformance floor — measured, not yet claimed |
| Does REAL arithmetic here match the target FPU? | Conformance floor — **unmeasured**, Grade B unbuilt |

A question in the L2 rows has no cheaper answer. Asking it at L0 or L1 does not
produce a weak answer; it produces a confident wrong one.
