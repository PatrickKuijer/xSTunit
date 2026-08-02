# xStunit v0.1 kill criteria

- Issue: `xstunit-229.18` (part of epic `xstunit-229`; blockers `xstunit-229.17`
  and `xstunit-229.3` closed).
- Date: 2026-08-02.
- **STATUS: spec.** Companions: `docs/spec/test-pyramid.md` and
  `docs/spec/non-goals.md`, whose layer names (L0/L1/L2) and section numbers
  this document uses without redefining them.
- Cite convention, inherited from the companions: cite code by symbol —
  `CliRunner.Run`, `SuiteCoverage.Analyze` — never by line number.
- Numbered **V1–V4** so they never collide with the `K`/`R`/`Q` numbering in
  `docs/research/coverage-wedge.md` § B4, which is scoped to the coverage wedge
  and the deferred safety-evidence thesis rather than to v0.1 as a whole. Those
  are carried by reference below, not restated.

---

## What a kill criterion is here

**A criterion fires against a claim.** No claim, no criterion — which is why
this list is shorter than the roster of things that could go wrong. Each entry
therefore says four things:

1. **Guards** — the claim in `test-pyramid.md` or `non-goals.md` that would be
   falsified. Named exactly, so a reader can check the criterion against the
   text rather than against a memory of it.
2. **Fires when** — the observation. An observation, not a feeling: something a
   named run, a named document, or a named vendor release either shows or does
   not.
3. **Then** — the consequence, and it is not always *abandon*. **Re-scope** is
   the more common one, and the distinction matters: a criterion whose only
   outcome is "give up" gets argued with instead of applied.
4. **Armed by** — what produces the observation. A criterion nothing produces
   the observation for **cannot fire**, and saying so is the point of the
   field; an unarmed criterion is a promise, and reading it as a live safeguard
   is the mistake this field exists to prevent.

A kill criterion is not a **quality bar** — a threshold the product can fail
while remaining the right product (`coverage-wedge.md` § B4, Q1). The two are
kept apart deliberately; see *What is deliberately not a kill criterion*.

---

## V1 — The fidelity-claim gate

**Guards.** The whole of what v0.1 says about agreement with TwinCAT, which is
one qualified sentence plus one enumeration:

- `test-pyramid.md` § L0 — the code "computes what the test says it computes,
  *as far as the interpreter models the language*". The trailing clause is the
  claim; without it the sentence is a different, much larger one.
- `non-goals.md` § 4 — the named approximations (cast width, CLR floats,
  pointer arithmetic, `_TO_STRING`, GVL init), closed by *"the size of this gap
  is unmeasured — not small, not large."*

`xstunit-229.3` concluded that fidelity is **measurable but not yet measured**:
Grade B is a staged 7-day-trial pilot (`xstunit-229.20`) behind a license spend
owned outside this repo. So the gate has two halves with different arming, and
collapsing them into one criterion is what makes "we abandon if fidelity proves
insufficient" unwritable.

### V1a — the claim outruns the evidence *(armed today)*

**Fires when** any external-facing text — README, release notes, site copy, a
talk, an issue reply — asserts semantic agreement with TwinCAT beyond § 4's
enumeration. Concretely: dropping the *as far as the interpreter models the
language* qualifier, characterising the gap as small or as edge cases, or
quoting an offline green run as evidence about target behaviour.

**Then** the sentence is retracted, and the retraction is the work — this is
the one criterion that fires on a document rather than on a measurement. It
sits first because with the gap unmeasured an overclaim is not falsifiable
in-house: it is falsified by a user whose plant behaves differently from their
green run, and that is the failure mode that ends the product outright rather
than re-scoping it. The claim boundary is the only fidelity asset v0.1 has.

**Armed by** review of any change touching `README.md` § Limits, either spec
file, or external copy. No oracle required, which is exactly why it is armed
and V1b is not.

### V1b — the interpreter does not model what it says it models *(unarmed)*

**Fires when** the Grade B verdict diff exposes divergence, reproducibly, in
**ordinary ST** — code with no explicit narrowing cast, no float comparison near
a representation boundary, no pointer arithmetic and no multi-task dependency;
that is, code § 4 does not already name.

**Then** the qualified L0 claim is itself false and re-scope is forced: the
`test-pyramid.md` question table loses its L0 row ("Does this compute the right
value?"), and what survives is execution plus coverage as a work list for the
agent loop — the tool runs your tests and tells you which POUs never executed,
without telling you the answers are right. Whether that is worth shipping is
the abandon decision, and it is taken then, with the divergence report in hand.

**Does not fire when** the divergence class lands *inside* § 4's enumeration —
float semantics, target-width arithmetic, task/cycle ordering. That is **K2**
(`coverage-wedge.md` § B4): it kills the deferred safety-evidence thesis, which
`non-goals.md` § 7 has already ruled out of v0.1, and `xstunit-229.20` records
it as a pilot **success**. Naming a known hole precisely is the oracle working,
not the product failing. V1b is about holes nobody declared.

**Armed by** `xstunit-229.20`, gated on `xstunit-229.21`/`xstunit-229.34` (the
miniload pilot project) and on the license route (`xstunit-229.23`,
`xstunit-229.24`). **Until that pilot runs, V1b cannot fire.**

### The standing consequence if the pilot never runs

The license may never be funded. That is **not** a kill — but neither is it
neutral, and writing it down is the point: absent Grade B, the fidelity claim
**never grows past § 4**, permanently, and every criterion above stays as it
stands. "We never got the oracle" is not permission to claim more quietly; it
is a commitment to claim exactly this much forever.

**Revival.** Grade B lands and runs green over the tree — then, in the
pyramid's own words, one on-target execution "stops being a per-change task and
becomes a standing property of the tool", and the L0 qualifier may be tightened
to match what was measured. That is the **only** route by which the fidelity
claim grows.

---

## V2 — The structural-advantage gate

**Guards.** The single competitive premise the epic rests on: no runtime, no
license, no hardware, milliseconds. `test-pyramid.md` § L0 states it as
mechanism ("milliseconds per run, no TwinCAT install, no build, no Activate
Configuration"); `README.md` § Limits states it as the thing the limits are
paid for; `non-goals.md` § 7 states it as a product boundary ("nothing xStunit
ships requires a TwinCAT install, a build, or an Activate Configuration").

**Fires when** either half goes:

- **Speed.** On a real production tree, the agent loop's default invocation —
  `xstunit <paths> --format json`, no `--coverage` (`CliRunner.Run`) — is slow
  enough that change-run-read stops being interactive. The number to beat is
  not absolute: it is TcUnit's build-plus-Activate round trip. Losing an order
  of magnitude against that is the observation.
- **Dependency.** Any *shipped* path acquires a TwinCAT install, build, or
  Activate. The conformance oracle deliberately runs one and is explicitly not
  a shipped path (`non-goals.md` § 7) — the oracle can never fire V2.

**Then** abandon. Against the competitive floor — TcUnit free and
runtime-based, TF1140/TF1040 first-party — there is nothing else being sold.
Every other claim in both spec files is a limit or a qualification; this is the
only positive one.

**Not Q1, and this is the boundary between them.** Q1 (`coverage-wedge.md`
§ B4, demoted from K5 by `xstunit-h4fs`) is about a deliberate **opt-in**
`--coverage` run becoming unpleasant, and it is a bar rather than a kill for
one reason: the loop never passes the flag, so the milliseconds constraint
never pays the cost. V2 is about the run with **no flag**. The one way the
demoted criterion returns is if the eager denominator pass ever lands in the
default path — and then its cost is being measured under V2, not under a
revived K5.

**Armed by** nothing today. `xstunit-fddl` needs the same real-tree access but
measures the opt-in coverage pass, not the default run. See *Arming* below.

---

## V3 — The reach gate

**Guards.** The premise underneath both spec files rather than any one
sentence in them: that L0 and L1 answer questions at all. Every claim in the
pyramid is downstream of the tool loading and running real code.

**Fires when** on a real production tree the fraction of loaded POUs that reach
a verdict is low enough that runs are dominated by `parse-error` and
`unsupported-construct` — the tool reporting its own reach instead of the
code's correctness. K4's ~70% is a stated guess for the *instrumentable*
fraction; V3 uses the same threshold, with the same caveat, for the *runnable*
fraction.

**Then** re-scope, not abandon: grammar work becomes the whole of v0.1 and
every other claim waits behind it.

**Why this is survivable and V1b is not.** V3 is a **loud** failure — the tool
says it cannot read the code, and `non-goals.md` § *What this list is not*
already routes that to a grammar-gap escalation. V1b is **quiet** — the tool
says green and is wrong. A tool that refuses honestly can be improved; a tool
that is confidently wrong has to be withdrawn.

**Armed by** nothing today. Same real-tree access as V2 and K4.

---

## V4 — The competitive-floor gate

**Guards.** That the structural advantage in V2 is *structural* — that the
incumbents cannot take it without becoming a different product. The floor as
assessed: TcUnit + TcUnit-Runner (free, runtime-based) and TF1140/TF1040
(Beckhoff first-party, runtime-based, licensed per platform level, **GA Q3
2026** — inside v0.1's horizon).

**Fires when** a free or already-licensed first-party tool executes ST test
suites **without a TwinCAT runtime**: TF1140 shipping a source-level or offline
mode, or TcUnit-Runner shedding its runtime dependency.

**Then** abandon. The advantage was never fidelity or feature breadth — it was
the absence of the runtime, and a first-party offline runner has better
fidelity by construction, since it can share the vendor's own front end.

**Does not fire when** TF1140 merely reaches GA on schedule. Runtime-based and
per-platform-level licensed *is* the floor as already assessed; V4 needs the
**offline** property to move, not the release date.

**Armed by** nothing today — it needs a standing watch on TF1140 release notes,
which no bead carries.

---

## What is deliberately not a kill criterion

Each of these has been proposed or could plausibly be read as one, and each is
excluded for a stated reason. The list is part of the spec: an entry here that
gets treated as a kill criterion produces an abandon argument that cannot be
resolved on evidence.

- **Q1 — the eager denominator pass costing too much** (`coverage-wedge.md`
  § B4, `xstunit-fddl`). Demoted from K5 by `xstunit-h4fs` and **not promoted
  back here**. A quality bar: the feature can fail it and still be the right
  feature. Its only route into this document is via V2.
- **The never-certifies list** (`agent-boundary.md` decision 7, shipped as
  `README.md` § Limits via `xstunit-kr5i`). A standing limit — a boundary that
  stays, per `non-goals.md`'s closing rule. Limits do not fire; overclaiming
  against them does, and that is V1a.
- **The open layout defects** — x64 pointer width (`xstunit-229.30`) and union
  layout (`xstunit-229.31`). `non-goals.md` § 5 makes **no** public
  layout-conformance claim, so there is nothing to falsify. They gate a
  *future* claim (`xstunit-229.33`); a criterion against a claim not yet made
  is a criterion against nothing. They are ordinary bugs.
- **BIT sub-byte offsets being unmodeled.** Ruled accept by `xstunit-229.12`
  and documented. Refusing to size a BIT member is an honest
  `unsupported-construct`.
- **The § 4 gap being unmeasured.** Unmeasured is not falsifying — it is the
  claim, stated. Only an overclaim (V1a) or a measurement (V1b) fires.
- **The license never being funded.** Not a kill; see V1's standing
  consequence.
- **Branch coverage and MC-DC being absent.** Declared-next and out of scope
  respectively (`coverage-wedge.md` decisions 2 and 3). A non-goal is not a
  failure to meet a goal.

---

## Arming

| Criterion | Armed by | Live today |
| --- | --- | --- |
| V1a — claim outruns evidence | Review of external-facing text | **Yes** |
| V1b — divergence in ordinary ST | `xstunit-229.20` Grade B pilot | No |
| V2 — structural advantage lost | Real-tree timing of the default run | No |
| V3 — reach too low | Real-tree runnable fraction | No |
| V4 — floor moves offline | Standing watch on TF1140 release notes | No |

**Three of the four unarmed criteria wait on the same thing: one run against a
real production tree**, which this repo's fixtures cannot substitute for
(`xstunit-fddl` says so for its own measurement). That run yields V2's default
timing, V3's runnable fraction, K4's instrumentable fraction and `xstunit-fddl`
in a single pass — one access request, four readings. Taking them separately
means asking for the same tree four times.

## How a criterion fires

A criterion that fires produces a decision — re-scope or abandon — recorded
against the epic, and only then an edit to the spec files. Never the other way
round: a claim quietly softened in `test-pyramid.md` or `non-goals.md` to
accommodate an observation is V1a running in reverse, and it leaves the
criterion looking un-fired forever.
