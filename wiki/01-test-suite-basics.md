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
- Assertions available today: `AssertTrue`, `AssertEquals_INT` (see
  `TcUnitSuiteHost.cs` for the full native-stub surface). Failures raise
  through the same native-stub boundary every other primitive in this wiki
  uses.

## CLI

```bash
dotnet run --project src/TcXunit.Cli -- run <path-to-POUs-directory>
```

Exit codes: `0` all pass, `1` any fail, `2` usage/discovery error.
