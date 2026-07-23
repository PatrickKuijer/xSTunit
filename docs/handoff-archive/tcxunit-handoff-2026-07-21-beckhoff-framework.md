> **ARCHIVED — historical snapshot, not a live tracker.** This is a point-in-time status
> briefing; current status lives in beads (`bd show TcXunit-w5x.15`, `bd show TcXunit-k28`).
> Do not treat this file as an open issue list. Moved here from `.handoff/` on 2026-07-23.

# TcXunit Handoff — for b_beckhoff_framework repo team

Date: 2026-07-21
Repo: TcXunit (c:\Git\TcXunit), branch master, clean
Purpose: brief b_beckhoff_framework team on today's TcXunit progress relevant to them. No code work done in this chat session — this doc summarizes repo state as of today's commits/closed issues, for handoff to that team.

## What shipped today

Epic `TcXunit-w5x.15` "Spec: simulated-PLC-testing capabilities" closed — all 11 subtasks done. Adds simulated-PLC test primitives to interpreter, relevant if b_beckhoff_framework wants to test FBs against TcXunit:

- Scan-cycle stepping: `FbInstance.StepCycles(n)`
- Simulated clock + TON/TOF/FB_Pulse native stubs
- In-process transport loopback: `Loopback` FB + `Transmit(source, sink)`
- Fault injection on Loopback: Drop/Restore/Freeze/SetDelay/Duplicate/Corrupt
- State-mirroring asserts: `AssertConverges` / `AssertConvergesAndLatches`
- STRUCT/ARRAY type declarations + literals, boundary builder, deep-clone Transmit for STRUCT/ARRAY
- Session/generation-counter reconnect simulation pattern
- REAL/LREAL literals + widening/narrowing casts
- TIME/LTIME literal grammar
- MOD/AND/OR/XOR/NOT operators

Also closed: `TcXunit-w5x` (parent epic), unparented `.9`/`.5` children from it.

Commit range (see `git log` for full messages/diffs): `265e326`..`7d4b46a`.

## Current TcXunit repo state (updated — audit now done)

- `TcXunit-k28` (P2 epic) "TcUnit compatibility audit vs upstream" — audit complete, 7 child beads filed, none closed yet. Epic stays open till children resolved.
- ST fixture wiki added for simulated-PLC-testing primitives (`6f55e83`, docs commit) — worth pointing b_beckhoff_framework devs at this as the usage reference.

## TcUnit compatibility audit findings (TcXunit-k28.1–.7)

Compared TcXunit.Runner/Interpreter against upstream TcUnit POUs (FB_TestSuite, FB_Test, FB_TestResults, FB_TcUnitRunner, FB_AdsAssertMessageFormatter, TEST/TEST_FINISHED/TEST_ORDERED functions). Filed, priority order:

- **k28.1 (P1)** — TcXunit accumulates/reports all failed asserts per test; upstream keeps only first failure per test (`FB_Test.SetAssertionMessage`). Biggest divergence — changes reported content for any multi-assert failing test.
- **k28.2 (P2)** — failure message format diverges from upstream's `FAILED TEST '<path>', EXP: x, ACT: y, MSG: z`.
- **k28.4 (P2)** — `AssertEquals_INT` uses C# 32-bit int; upstream INT is 16-bit, overflow/wraparound semantics differ.
- **k28.3 (P3)** — AssertTrue/AssertFalse drop EXP/ACT detail; upstream delegates both through AssertEquals_BOOL.
- **k28.5 (P3)** — duplicate-TEST()-name check is per-interpreter-pass; upstream is per-PLC-cycle. Already a known v1 limitation (ref TcXunit-w5x.7) — audit made root cause explicit.
- **k28.6 (P3)** — only TEST/TEST_FINISHED/AssertEquals_INT/AssertTrue wired in NativeMethodBridge; upstream has ~20 scalar AssertEquals_* overloads + array asserts + generic AssertEquals(ANY). Note: FB_TestSuite.cs already implements AssertFalse/AssertEquals_BOOL/AssertEquals_STRING/AssertEquals_REAL but they're unreachable — no bridge wiring.
- **k28.7 (P3)** — no equivalent for TEST_ORDERED, TEST_FINISHED_NAMED, IS_TEST_FINISHED (ordered/async/multi-cycle test patterns).

Not bugs: `AssertConverges`/`AssertConvergesAndLatches` — confirmed no upstream POU, intentional TcXunit extension. Setup/teardown lifecycle — upstream TcUnit has none either, so absence isn't a divergence.

## Relevance to b_beckhoff_framework

TcXunit now supports enough ST surface (STRUCT/ARRAY, TON/TOF, loopback+fault injection, scan-cycle stepping) that Beckhoff-framework FBs using structs/timers/transport patterns should be testable without a live TwinCAT runtime. **Caveat for teams relying on multi-assert-per-test reports or exact TcUnit-format failure messages: see k28.1/k28.2 above — behavior currently diverges from upstream TcUnit.** If b_beckhoff_framework hits further TcUnit-API gaps, feed findings into TcXunit-k28's children rather than duplicating investigation.

## Suggested skills for next session

- `code-review` — before closing any TcXunit-k28 child fix, review diff against repo standards.
- `tdd` — k28.1/k28.2/k28.4 are behavior-changing fixes; write failing test against upstream-observed behavior first.
- `domain-modeling` — if TcUnit-vs-TcXunit terminology gaps keep recurring, worth an ADR.

## Not covered here

- Full diffs/commit messages: see `git log 265e326..7d4b46a` in TcXunit repo.
- Issue detail: `bd show TcXunit-w5x.15` and its children for acceptance criteria/design notes.
- No b_beckhoff_framework repo state inspected — this session had no access to that repo; doc is TcXunit-side info only.
