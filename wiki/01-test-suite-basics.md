# Test suite basics

Status: **built**. Source: `src/TcXunit.Runner/TcUnitStub/FB_TestSuite.cs`,
`src/TcXunit.Interpreter/TcUnitSuiteHost.cs`. Working example:
`tests/Fixtures/FbCounterFixture/FB_CounterTests.TcPOU`.

## Shape

A test suite is a `FUNCTION_BLOCK` that `EXTENDS TcUnit.FB_TestSuite`. The
top-level body just calls each test method in order; each test method calls
`TEST('name')`, does its arrange/act/assert, then `TEST_FINISHED()`.

```
FUNCTION_BLOCK FB_MyTests EXTENDS TcUnit.FB_TestSuite

(* top-level ST body *)
SomeScenario();
AnotherScenario();
```

```
METHOD PRIVATE SomeScenario
VAR
    counter : FB_Counter;
END_VAR
```
```
TEST('SomeScenario');

counter.Increment(delta := 3);

AssertEquals_INT(Expected := 3,
                  Actual := counter.GetValue(),
                  Message := 'after Increment(3)');

TEST_FINISHED();
```

## Rules

- `SuiteDiscovery` finds suites by walking `EXTENDS` ancestry up to
  `TcUnit.FB_TestSuite` — no attribute or naming convention needed beyond
  the `EXTENDS` clause itself.
- Each test method declares its own fixture instances in its own `VAR`
  block (`counter : FB_Counter;` above) — instances are not shared across
  test methods unless you deliberately put them in the suite's own `VAR`
  block.
- `TEST('name')` / `TEST_FINISHED()` bracket the assertions that belong to
  that one test case; a suite can run any number of test methods from its
  top-level body.
- Assertions available today: `AssertTrue`, `AssertFalse`,
  `AssertEquals_INT`, `AssertEquals_BOOL`, `AssertEquals_STRING`,
  `AssertEquals_REAL(Expected, Actual, Delta, Message)` (see
  `FB_TestSuite.cs`/`TcUnitSuiteHost.cs` for the full native-stub surface).
  `AssertEquals_INT` compares as signed 16-bit (matching IEC 61131-3
  `INT`); `AssertTrue`/`AssertFalse` delegate to `AssertEquals_BOOL`
  internally, so their failure message carries the same EXP/ACT detail.
  Only the *first* assertion failure per `TEST()` bracket is recorded —
  later failing asserts in the same test are dropped, matching upstream
  TcUnit. Failure message format:
  `FAILED TEST '<name>', EXP: <expected>, ACT: <actual>[, MSG: <message>]`.

## Ordered tests and out-of-order finish

For a fixed run order (or finishing a test other than the currently-open
one — e.g. multi-cycle/async test bodies), use `TEST_ORDERED`,
`TEST_FINISHED_NAMED`, and `IS_TEST_FINISHED` instead of `TEST`/
`TEST_FINISHED`:

```
IF TEST_ORDERED('Test_1') THEN
    (* asserts *)
    TEST_FINISHED();
END_IF

IF TEST_ORDERED('Test_2') THEN
    (* asserts *)
    TEST_FINISHED();
END_IF
```

- `TEST_ORDERED('name')` declares (or re-attaches to) a test and returns
  `TRUE` only when it's currently that test's turn and it hasn't finished
  yet — order numbers are assigned in first-call order, one at a time, so
  the body above only enters `Test_2`'s block after `Test_1` finishes.
- `TEST_FINISHED_NAMED('name')` finishes a named test regardless of which
  test is currently open. An unknown name (never declared via `TEST`/
  `TEST_ORDERED`) fails fast.
- `IS_TEST_FINISHED('name')` polls whether a named test has finished.
  Same fail-fast-on-unknown-name behavior.
- Repeat-name dedup for both `TEST`/`TEST_ORDERED` is keyed off the
  suite's current cycle index, not process lifetime: a repeat `TEST('X')`
  in the *same* cycle is a real duplicate (error); a repeat in a *later*
  cycle re-attaches to the existing test record instead (the normal
  cyclic re-declaration flow for a multi-cycle runner).

## CLI

```bash
dotnet run --project src/TcXunit.Cli -- run <path-to-POUs-directory>
```

Exit codes: `0` all pass, `1` any fail, `2` usage/discovery error.
