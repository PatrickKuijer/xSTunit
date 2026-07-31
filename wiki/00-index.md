# TcXunit ST Fixture Wiki

How to write `.TcPOU` test fixtures against TcXunit's simulated-PLC-testing
primitives. Grounded in `src/TcXunit.Interpreter` and the existing xUnit
tests/fixtures as of 2026-07-22 (epics `TcXunit-w5x.15`, `TcXunit-sej`,
`TcXunit-k28`).

Status legend: **built** = usable in a fixture today. **pending** = spec'd,
ticket still open, API may shift.

| Page | Primitive | Status |
|---|---|---|
| [01-test-suite-basics.md](01-test-suite-basics.md) | `EXTENDS TcUnit.FB_TestSuite`, `TEST()`/`TEST_FINISHED()`/`TEST_ORDERED()`/`TEST_FINISHED_NAMED()`/`IS_TEST_FINISHED()`/`Assert*` | built |
| [02-step-cycles.md](02-step-cycles.md) | `FbInstance.StepCycles(n)` | built |
| [03-simulated-clock-and-timers.md](03-simulated-clock-and-timers.md) | `Engine.Clock.Advance(dt)`/`AdvanceNs(dt)`, `TON`/`TOF`/`TP`, `LTON`/`LTOF`/`LTP` | built |
| [04-loopback-and-faults.md](04-loopback-and-faults.md) | `Loopback` FB: `Transmit`, `Drop`/`Restore`/`Freeze`/`SetDelay`/`Duplicate`/`Corrupt`, `LinkUp`/`LastUpdateTime` | built |
| [05-session-counter-reconnect.md](05-session-counter-reconnect.md) | Reconnect pattern via ordinary VAR counter + Drop/Restore | built (pattern only, no new primitive) |
| [06-state-mirroring-assertions.md](06-state-mirroring-assertions.md) | `AssertConverges` / `AssertConvergesAndLatches` | built |
| [07-struct-array-and-builders.md](07-struct-array-and-builders.md) | `STRUCT`/`ARRAY` types, struct boundary `Build()` | built |
| [08-array-indexing-pointers-and-memcpy.md](08-array-indexing-pointers-and-memcpy.md) | `arr[i]`/`arr[i,j]`, `ADR(x) +/- offset`, `MEMCPY`/`MEMSET`/`MEMMOVE` | built |
| [09-control-flow-statements.md](09-control-flow-statements.md) | `FOR`/`WHILE`/`REPEAT`/`CASE`/`EXIT`, unary minus | built |

## Quick orientation

- One shared `Engine` per test run; one shared `Engine.Clock` for the whole
  suite (not per-instance).
- Every native primitive (`StepCycles`, `Loopback`, `TON`/`TOF`/`TP`, `LTON`/`LTOF`/`LTP`)
  is dispatched the same way: ordinary ST method-call syntax on a normal
  `VAR` field, intercepted by the engine's native-stub boundary
  (`NativeMethodBridge` / direct `Engine.CallMethod` dispatch) instead of
  running interpreted ST body code. From the fixture author's side it just
  looks like calling a method on an FB instance.
- Nothing is wired implicitly. Stepping, clock advance, and transmission
  each require an explicit call in your test method's ST body.
- Working example fixture today: `tests/Fixtures/FbCounterFixture/` (no
  timers/loopback yet — plain suite structure only). Use it as the
  skeleton, then layer in the primitives from the pages above.
