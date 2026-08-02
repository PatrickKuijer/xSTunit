# xStunit v0.1 non-goals

- Issue: `xstunit-229.17` (part of epic `xstunit-229`). Companion:
  `docs/spec/test-pyramid.md`, whose layer names (L0/L1/L2) this document uses
  without redefining them.
- Date: 2026-08-02.
- **STATUS: spec.** This is the wording that ships. Sources are the ratified
  `docs/research/agent-boundary.md` § 5 and `docs/research/coverage-wedge.md`
  § B, plus the epic's own out-of-scope section; nothing here is decided for the
  first time.
- **Every entry is a boundary a reader can act on**, which means each one says
  three things: what does not exist, what a test may therefore not depend on,
  and which layer the question goes to instead. An entry that only says "we
  don't do that" has failed this document's bar.

---

## 1. Multi-task interaction and races

**Does not exist.** Nothing in the engine models more than one execution
context; the interpreter's only call stack is CLR recursion
(`Engine.ExecuteBody`). `StepCycles` has no scheduler — *"ordering across
several instances is whatever order the caller invokes them in"*
(`Engine.StepCycles`). Task priorities, preemption and cross-task data races are
not approximated badly; they are absent.

**Act on it:** never write an xStunit test whose verdict depends on
interleaving. If a test's meaning changes when two FBs are stepped in a
different order, it is asking L2's question at L1 and its green is not weak
evidence — it is no evidence.

**Goes to:** L2. Not on a roadmap: a second execution context is a different
product, not a missing feature.

---

## 2. I/O image semantics

**Does not exist.** Process-image mapping is not modelled. `Loopback` is a
fault-injection *model* of a transport (`Engine.CallMethod`, `LoopbackHost`) —
genuinely useful for testing how a POU reacts to drops, delays, duplication and
corruption — but it is not a bus. Inputs latched at task start, outputs written
at task end, `%I*`/`%Q*` mapping, and the fieldbus cycle's relationship to the
task cycle have no representation.

**Act on it:** an FB that takes its inputs through `VAR_INPUT` is testable at
L0/L1. An FB whose correctness depends on *when* the image was refreshed is not,
and no amount of test-side setup makes it so.

**Goes to:** L2.

---

## 3. Jitter and cycle-time behaviour

**Does not exist.** The clock is simulated: a monotonic nanosecond total
(`Clock.TotalNs`) advanced only by explicit calls (`Clock.AdvanceMs`,
`Clock.AdvanceNs`) and read by the timer hosts when invoked. A cycle costs zero
simulated time unless a test advances the clock. There is therefore no jitter,
no scan overrun, no scan-time distribution and no cycle budget.

**Act on it:** xStunit can tell you a timer sequence is *ordered* correctly. It
cannot tell you it is *fast enough*. Never quote an xStunit run in a cycle-time
argument — not as weak support, not as a starting point.

**Goes to:** L2.

---

## 4. Target-specific arithmetic and conversion semantics

This entry differs in kind from 1–3, and the difference is the reason it is
written out rather than folded into them. The first three are **absent**, which
is loud: a test that needs them cannot be written. These are **approximated**,
which is quiet — they can make a *green* run misleading rather than merely
incomplete.

- Explicit narrowing casts route through `Convert.ToInt32` for every integer
  target width — `SINT` through `LWORD` take the same branch
  (`Engine.TryEvaluateCast`, `Engine.IntegerCastTargets`). Target-width
  truncation is not modelled at the cast site.
- REAL/LREAL arithmetic runs on CLR `float`/`double` (`Engine.EvaluateNumeric`),
  not the target FPU.
- Pointer arithmetic is self-documented as *"an approximation for wider element
  types"* (`Engine.EvaluatePointerArithmetic`).
- `_TO_STRING` makes no attempt at TwinCAT digit-count or exponent parity
  (`Engine.TryEvaluateCast`).
- GVL initialization models *"no TwinCAT GVL init-cycle/task-binding semantics …
  just zero-initialized storage"* (`Engine._globals`).

**Act on it:** a branch guarded by an overflow-dependent comparison, or by float
equality near a representation boundary, can go the other way on target. Such a
branch is not certified by a green run at any offline layer.

**The size of this gap is unmeasured** — not small, not large. Measuring it is
the conformance floor's Grade B, which is unbuilt (`xstunit-229.20`). Until it
exists, this list is the whole of what is known.

---

## 5. Byte-layout conformance — measured, not claimed

Grade A has measured this and the result is *not* a blanket pass, so v0.1 makes
**no public layout-conformance claim**. `xstunit-229.12` read compiler-emitted
`.tmc` for 53 types and 145 members on both targets: x86 conformant on every
checklist rule, and two real defects open — x64 pointer/reference width
(`TypeLayout` hardcodes 4 bytes; `xstunit-229.30`) and union layout
(`xstunit-229.31`). BIT sub-byte offsets are accepted as unmodeled, and the
pack-write half of trailing padding is unverified because a `.tmc` cannot say
which bytes a write touches.

**Act on it:** do not rely on `SIZEOF`/`ADR`/`MEMCPY` agreeing with the compiler
for unions, or for anything pointer-width-sensitive on an x64 target, until
those two beads close. When a conformance claim does ship it must name BIT as
unmodeled and pack-write as unverified (`xstunit-229.33`).

---

## 6. Hardware faults, retain/persistent, online change, power cycles

**Does not exist**, entirely — no representation of any of it.

**Goes to:** L2.

---

## 7. Standing product boundaries

Out of category rather than deferred: these do not arrive by growing the
grammar, and none of them is a roadmap item.

- **Not a replacement for TcUnit or TF1140 as the authoritative on-hardware
  runner.** The pyramid puts xStunit under that run, not in place of it.
- **Nothing xStunit ships requires a TwinCAT install, a build, or an Activate
  Configuration.** The conformance oracle deliberately does run one — offline,
  as an answer key. That is test infrastructure for xStunit's own CI, not a
  runtime dependency of the product.
- **TF1140 authoring shape is not a v0.1 target** (`xstunit-229.7`): TcUnit
  shape only. The audit found dual support needs a wholly separate
  discovery-and-dispatch pipeline, not a parameterization, so no seams were
  pre-built for it.
- **Branch coverage is declared-next, not v0.1; MC-DC is out of scope and
  explicitly not seam-proofed** (`coverage-wedge.md` decisions 2 and 3). MC-DC
  needs a stable sub-expression identity model that does not exist, and a
  speculative half-built one is worse than none.
- **IEC 61508 / ISO 13849 evidence is not the v0.1 commercial thesis**, and is
  barred from all external-facing text until `coverage-wedge.md` § B has been
  checked against the actual standard text — those paragraphs are recollection,
  not primary source (decisions 7 and 8). The blocker is fidelity, not
  qualification cost.
- **HMI, I/O and motion testing** are outside the product.

---

## What this list is not

It is not the failure taxonomy, and confusing the two is the mistake that costs
source code.

`unsupported-construct` means *valid IEC 61131-3 that TwinCAT compiles and
xStunit has not grown support for yet* — a gap that closes, construct by
construct, and whose correct response is to escalate a grammar-gap report
(`README.md` § Usage). A non-goal above is a boundary that **does not close by
growing the grammar**. An agent that treats a non-goal as a grammar gap files
work that can never be done; an agent that treats a grammar gap as a non-goal
deletes correct ST. The two lists are read together and neither substitutes for
the other.
