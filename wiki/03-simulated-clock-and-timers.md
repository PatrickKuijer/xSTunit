# Simulated clock, TON/TOF/FB_Pulse

Status: **built**. Ticket: `TcXunit-w5x.15.7`. Source:
`src/TcXunit.Interpreter/Clock.cs`, `TimerHost.cs`. Tests:
`tests/TcXunit.Interpreter.Tests/ClockTests.cs`, `TimerFbTests.cs`.

## The clock

One `Engine.Clock` per suite run — process-wide, not per-instance. It's a
monotonic absolute running total in ms; nothing rewinds it.

There's no direct ST syntax to call `Engine.Clock.Advance` shown in the
current fixtures (it's driven from the C# test harness in today's tests).
If your fixture needs to advance simulated time, do it the same way the
existing interpreter tests do — advance the shared clock between
`StepCycles` calls, not inside the ST body.

## TON / TOF / FB_Pulse

Declared and called exactly like a real TwinCAT timer FB — no special
syntax:

```
VAR
    fbTimer : TON;
    inVar   : BOOL;
    ptVar   : TIME;
END_VAR
```
```
fbTimer(IN := inVar, PT := ptVar);
measuredQ := fbTimer.Q;
measuredEt := fbTimer.ET;
```

Each call to the timer instance re-reads the shared clock's current total,
computes its own delta since its own last-observed total, and publishes
`Q`/`ET` back into the instance's normal fields — same as reading a real
timer FB's outputs.

### Firing semantics (matches documented TON/TOF/TP contract)

- Internal raw elapsed time accumulates **unclamped** and drives the firing
  check (`Q`).
- Published `ET` clamps to `min(rawElapsed, PT)`.
- This means you can assert "not yet fired" at `PT - 1` and "fired" at `PT`
  or beyond, including after a single large clock jump that overshoots
  `PT`.

### Per-type edge behavior

- **TON** (on-delay): `Q` follows `IN` after a `PT` delay. Falling edge on
  `IN` resets immediately — no delay on the way down.
- **TOF** (off-delay): `Q` follows `IN` immediately on rising edge, but
  stays `TRUE` for `PT` after `IN`'s falling edge.
- **FB_Pulse**: rising edge of `IN` (while not already running) starts a
  fixed-width `PT` pulse on `Q`, independent of what `IN` does for the rest
  of the pulse. Re-triggering needs a fresh rising edge after the pulse
  ends.

## Rules

- Each timer instance tracks its **own** last-observed clock total — a
  multi-instance stepping order (`a.StepCycles(1); b.StepCycles(1);`)
  can't cause one timer to "steal" another timer's elapsed time. Verified
  by `Clock_MultiInstanceSteppingOrder_DoesNotStealElapsedTime`.
- The very first call to a timer instance just initializes its clock
  tracking — no elapsed time is counted until the *next* call after a
  clock advance. Always do a baseline call before advancing the clock and
  asserting.
