# Scan-cycle stepping: StepCycles

Status: **built**. Ticket: `TcXunit-w5x.15.4`. Source:
`src/TcXunit.Interpreter/Engine.cs` (`StepCycles` case in the method-call
dispatch), `FbInstance.cs`. Tests: `tests/TcXunit.Interpreter.Tests/StepCyclesTests.cs`.

## What it does

`someInstance.StepCycles(n)` re-invokes that FB instance's own top-level ST
body `n` times, reusing its existing persisted `VAR` fields (`Cell`s)
across calls — the same effect as `n` real TwinCAT scan cycles, without a
scheduler.

## Usage

```
METHOD PRIVATE CounterAdvancesOverCycles
VAR
    fb : FB_Counter;
END_VAR
```
```
TEST('CounterAdvancesOverCycles');

fb.StepCycles(3);

AssertEquals_INT(Expected := 3, Actual := fb.Count, Message := 'after 3 cycles');

TEST_FINISHED();
```

(`FB_Counter` here just does `Count := Count + 1;` in its top-level body —
each `StepCycles` call runs that body again.)

## Rules

- No `dt` parameter — `StepCycles` is time-agnostic. If the FB under test
  reads elapsed time (a `TON` field, etc.), advance
  [`Engine.Clock`](03-simulated-clock-and-timers.md) yourself, separately
  and explicitly.
- No required method-name convention on the FB under test — it re-runs
  whatever top-level body the FB already has. You don't restructure the FB
  to make it steppable.
- Multi-instance ordering is entirely caller-controlled:
  ```
  a.StepCycles(1);
  b.StepCycles(1);
  ```
  You decide which instance observes which side effect first. There is no
  built-in "step this group together" scheduler — if you need three or
  more instances stepped in lockstep, write the loop yourself in the test
  method body.
