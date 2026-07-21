# Session/generation-counter reconnect simulation

Status: **built as a pattern** — demonstrated in
`tests/TcXunit.Interpreter.Tests/LoopbackReconnectSessionTests.cs`
(ticket `TcXunit-w5x.15.11`). No new API is needed; it composes
[Loopback](04-loopback-and-faults.md)'s `Drop`/`Restore` with an ordinary
`VAR` field on the FB under test.

## Pattern

The "generation counter" is just a normal field on the FB under test — set
directly via ST assignment, not something `Loopback` or fault injection
knows about.

```
VAR
    fbLink : Loopback;
    fbA    : FB_ControllerUnderTest;
    fbB    : FB_ControllerUnderTest;
END_VAR
```

**Brief link blip** (counter unchanged):

```
fbLink.Drop();
fbLink.Restore();
(* fbA.Generation untouched *)
```

**Simulated process restart** (same pair, plus the bump):

```
fbLink.Drop();
fbA.Generation := fbA.Generation + 1;
fbLink.Restore();
```

Then assert on the FB under test's own reconnect-path output — a
`ReregistrationCount`/`Status`-style field it publishes — never on the
counter field or `Loopback`'s internal wiring. The point of the test is
"does the FB under test's observable reconnect behavior diverge correctly
between these two preconditions," not "did the counter change."

## Rules

- No new fault-vocabulary primitive — brief-blip vs. simulated-restart is
  entirely which ST statements you put between `Drop()` and `Restore()`.
- Assert the FB-under-test's own contract, not the mechanism that produced
  the precondition.
