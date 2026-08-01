# TF1140 (Beckhoff TC3 Unit Test) sample audit vs xStunit parser/discovery

- Issue: `TcXunit-229.2` (part of epic `TcXunit-229`, feeds `TcXunit-229.7` and `TcXunit-229.8`).
- Source audited: `https://github.com/Beckhoff/TF1140_Sample.git`, cloned shallow
  (`--depth 1`) into the scratchpad at commit HEAD of the default branch at
  audit time. Not vendored into this repo.
- **Convention note**: `docs/research/` does not exist elsewhere in this repo.
  This file establishes it as a new location for cross-repo research notes —
  there was no existing convention to follow.

## Verdict: **mismatch** (not a partial-compat case — structural, at three independent layers)

TF1140's official sample authors test POUs in a shape xStunit's current
parser + discovery pipeline cannot recognize as tests at all, let alone run.
Given an unmodified TF1140 `UnitTest` folder, `xstunit <path>` would parse
every `.TcPOU` successfully (nothing trips `TcPouParser`'s rejected-construct
list) but **discovery would find zero suites** and the CLI would exit `2`
with `no TcUnit suites found under <path>` (`CliRunner.Run`).
This isn't a naming quirk fixable by a regex tweak — it's three compounding
divergences from what `TcXunit-w5x.7`'s discovery convention (walking
`EXTENDS` ancestry to the literal string `TcUnit.FB_TestSuite`) assumes:

1. **Different base-class ancestry, different library, dead-ends before
   reaching any recognized root.** TF1140 test cases `EXTENDS FB_TestCaseBase`
   from library `Tc3_PlcTestFramework`, not `TcUnit.FB_TestSuite`. That base
   type is never itself defined as a `.TcPOU` in the sample tree — it's a
   compiled library reference (`PlaceholderReference` in the `.plcproj`), so
   `TypeRegistry.Get("FB_TestCaseBase")` returns `null` and
   `SuiteDiscovery.IsSuiteType` bails out on the very first ancestry step.
2. **A test *case* per FB, not a test *suite* with many `TEST()`-bracketed
   methods.** Each TF1140 `FB_Test*` is one scenario with its logic inline in
   the FB's own top-level body (no methods except a `CleanUp` lifecycle hook)
   — there is no `TEST('name')` / `TEST_FINISHED()` bracketing convention at
   all. xStunit's model is one suite FB containing N test methods, each
   opening/closing a `TEST()` bracket.
3. **A different, and not textually compatible, assertion/registration
   surface.** TF1140 calls bare `AssertEqual(expected:=.., actual:=..,
   message:=..)` and `Succeeded()`; xStunit's native-call dispatch table only
   recognizes `AssertEquals` (with a trailing "s") / `AssertEquals_<TYPE>` /
   `AssertTrue` / `AssertFalse` / the `TEST*` family
   (`NativeMethodBridge.FixedNativeMethodNames`). `AssertEqual`
   and `Succeeded` are not in that table, so even if discovery were somehow
   patched to treat `FB_TestCaseBase` as a suite root, the interpreter would
   still fail to resolve those calls as anything but ordinary (missing)
   user-defined methods. Suite-level *registration* also differs: TF1140 has
   no auto-discovery convention at all — test cases are hand-instantiated as
   `VAR` in a `PROGRAM MAIN` and run via a single call to
   `Tc3_PlcTestFramework.TestController.TestCtrl()`, which is presumably a
   compiled reflection/registration mechanism internal to the (closed-source)
   framework, not something expressible as ST source xStunit could interpret.

None of this means TF1140-shaped tests are unrunnable in principle — it means
supporting them is a second, separate discovery+dispatch convention, not a
tweak of the existing one. That tradeoff is exactly what `TcXunit-229.7`
(single vs dual compatibility target) needs to weigh.

## Details

### 1. `EXTENDS` target: `TcUnit.FB_TestSuite` vs `Tc3_PlcTestFramework.FB_TestCaseBase`

**TF1140 side** — every test-case POU in
`PlcSample/UnitTest/TestCases/` (`FB_TestSum.TcPOU`, `FB_TestDiff.TcPOU`,
`FB_TestProd.TcPOU`, `FB_TestDiv.TcPOU`) declares:

```
{ attribute 'Name':='TB_Sum-Test' }
{ attribute 'Timeout':='t#20s' }
{ attribute 'Owner':='TestOwner' }
FUNCTION_BLOCK FB_TestSum EXTENDS FB_TestCaseBase
```

(`PlcSample/UnitTest/TestCases/FB_TestSum.TcPOU`, `<Declaration>` CDATA, lines
1-5 of the CDATA block.) `FB_TestCaseBase` is not qualified with a namespace
prefix in source, but the project reference confirms which library it comes
from — `PlcSample/UnitTest/UnitTest.plcproj:65-67`:

```xml
<PlaceholderReference Include="Tc3_PlcTestFramework">
  <DefaultResolution>Tc3_PlcTestFramework, * (Beckhoff Automation GmbH)</DefaultResolution>
  <Namespace>Tc3_PlcTestFramework</Namespace>
</PlaceholderReference>
```

`FB_TestCaseBase` (and `TestController`, see §3) live inside this
`PlaceholderReference`d library — there is no `.TcPOU` for either type
anywhere in the cloned sample tree (confirmed: `find . -iname "*.TcPOU"`
returns exactly 9 files — 5 non-test FBs in `MyLib`/`SampleLib`, `MAIN.TcPOU`,
and the 4 `FB_Test*` cases; no `FB_TestCaseBase.TcPOU` or
`TestController.TcPOU`). The base type is a compiled/binary library
dependency, resolved only inside real TwinCAT — exactly analogous to how
xStunit treats `TcUnit.FB_TestSuite` as a "native stub boundary" it never
expects to parse (the `TypeRegistry` type comment), just under a different
qualified name and shape.

**xStunit side** — the parser extracts the base type name via regex:

```csharp
// TcPouParser.ExtendsPattern
private static readonly Regex ExtendsPattern = new Regex(
    @"FUNCTION_BLOCK(?:\s+(?:ABSTRACT|FINAL))*\s+\S+\s+EXTENDS\s+(?<baseType>[\w.]+)",
    RegexOptions.Compiled);
```

This regex *would* successfully extract `baseTypeName = "FB_TestCaseBase"`
from TF1140's declaration text (it matches anywhere in the text, ignoring
the leading `{ attribute ... }` pragma lines) — parsing itself is not where
this breaks.

Discovery is where it breaks. `SuiteDiscovery.IsSuiteType` walks the ancestry
chain looking for the literal string `TcUnit.FB_TestSuite`:

```csharp
// SuiteDiscovery.TestSuiteBaseType, SuiteDiscovery.IsSuiteType
private const string TestSuiteBaseType = "TcUnit.FB_TestSuite";
...
public static bool IsSuiteType(TypeRegistry registry, string typeName)
{
    var current = typeName;
    while (current != null)
    {
        if (current == TestSuiteBaseType)
            return true;

        var def = registry.Get(current);
        if (def == null)
            return false;

        current = def.BaseTypeName;
    }
    return false;
}
```

For `typeName = "FB_TestSum"`: `current` becomes `"FB_TestCaseBase"` on the
first iteration (not equal to `"TcUnit.FB_TestSuite"`), then
`registry.Get("FB_TestCaseBase")` returns `null` (that type was never parsed
— it isn't a `.TcPOU` file in the tree), so the loop returns `false`
immediately. Every `FB_Test*` case is invisible to discovery.

`CliRunner.Run` feeds *every* parsed POU name in the tree as a discovery
candidate (`SuiteDiscovery.FindSuiteTypeNames(registry, types.Select(t =>
t.Name))`), so this isn't a candidate-selection gap — it's that none of the
9 parsed types' ancestry ever reaches `TcUnit.FB_TestSuite`. Confirmed
result: `CliRunner.Run` — `suiteNames.Count == 0` → exit code `2`,
message `no TcUnit suites found under <args>`.

### 2. One-FB-per-test-case vs one-FB-suite-with-many-`TEST()`-methods

**TF1140 side** — a test case's entire scenario lives in the FB's own
top-level `<Implementation><ST>` (no `TEST()`/`TEST_FINISHED()` calls
anywhere), e.g. `PlcSample/UnitTest/TestCases/FB_TestSum.TcPOU`:

```
myFB(var1:= 2, var2:= 2, result=> result);
expectedValue:=4;
AssertEqual(expected:= expectedValue, actual:= result, message:= 'Addition with the FB_Sum function block is incorrect!');
Succeeded();
```

The only `<Method>` present is `CleanUp` (a lifecycle hook, commented `//Must
be implemented if you want to use fastmode`):

```
<Method Name="CleanUp" Id="{13039c32-e814-4f8d-baa1-70bdbbaa8a33}">
  <Declaration><![CDATA[//Must be implemented if you want to use fastmode
METHOD CleanUp : BOOL
]]></Declaration>
  <Implementation><ST><![CDATA[CleanUp:=true;]]></ST></Implementation>
</Method>
```

"Suite" composition happens outside any FB entirely, in
`PlcSample/UnitTest/POUs/MAIN.TcPOU`: a `PROGRAM MAIN` declares one `VAR`
instance per test case FB (`fbT1 : FB_TestSum; fbT2 : FB_TestDiff; ...`) and
its body is a single call: `Tc3_PlcTestFramework.TestController.TestCtrl();`
— an opaque, compiled entry point, not ST source describing how the 4
instances get run/aggregated.

**xStunit side** — per `wiki/01-test-suite-basics.md` § Shape and the working
fixture `tests/Fixtures/FbCounterFixture/FB_CounterTests.TcPOU`, one suite FB
contains N test *methods*, and the suite's own top-level body explicitly
calls each in order:

```
FUNCTION_BLOCK FB_CounterTests EXTENDS TcUnit.FB_TestSuite
```
```
CounterStartsAtZero();
IncrementAddsDelta();
DecrementClampsAtZero();
ClampedCounterIncrementRespectsCeiling();
```

and each method brackets its assertions with `TEST('name')` /
`TEST_FINISHED()` (the `CounterStartsAtZero` method of
`tests/Fixtures/FbCounterFixture/FB_CounterTests.TcPOU`). There is no analog to
`MAIN` + `TestController` — discovery is purely structural (ancestry), and
running is purely "call the suite FB's `Body()`", per `SuiteHost.Body` being a
deliberate no-op that the interpreter drives directly.

Net effect: even a hypothetical patch that recognized `FB_TestCaseBase` as a
suite root would still need to reinterpret "1 FB with a bare top-level body"
as "1 test", not "N tests inside one FB" — a different granularity, not just
a different root name.

### 3. Assertion/lifecycle call surface: `AssertEqual`/`Succeeded`/`CleanUp` vs `AssertEquals`/`TEST`/`TEST_FINISHED`

**TF1140 side** — the only assertion call observed across all 4 test cases is
`AssertEqual(expected:=.., actual:=.., message:=..)` (lower-case parameter
names, no trailing "s" on the call name), followed by an explicit
`Succeeded()` call to end the test with no further checks run
(`PlcSample/UnitTest/TestCases/FB_TestSum.TcPOU`, `<Implementation>` CDATA).
`CleanUp` is a boolean-returning method framework hook, called by
`TestController` (not visible in source) to know when fast-mode teardown is
done.

**xStunit side** — `NativeMethodBridge`'s recognized-name table:

```csharp
// NativeMethodBridge.FixedNativeMethodNames
"TEST", "TEST_ORDERED", "TEST_FINISHED", "TEST_FINISHED_NAMED", "IS_TEST_FINISHED",
"AssertTrue", "AssertFalse", "AssertEquals",
```

plus the prefix-matched families `AssertEquals_<TYPE>`,
`AssertArrayEquals_<TYPE>`, `AssertArray2dEquals_<TYPE>` /
`AssertArray3dEquals_<TYPE>` (`NativeMethodBridge.CanInvoke`). `AssertEqual`
(no trailing "s") and `Succeeded` are absent from every list — they would
resolve as ordinary (and, since undefined anywhere in the tree, unresolvable)
user method calls, not native assertion calls. There's also no lifecycle hook
analogous to `CleanUp` in `src/xStunit.Runner/TcUnitStub/FB_TestSuite.cs` (only
`Test`/`TestOrdered`/`TestFinished`/`TestFinishedNamed`/`IsTestFinished`/
`AssertTrueCall`/`AssertFalseCall`/`AssertEqualsScalar`/`AssertEqualsAnyCall`/
`AssertArrayEqualsCall`/`HasOpenTestCase`/`AbortCurrentTestCase`/
`EnterNativeCall`, per `SuiteHost`).

### Pragma attributes (`'Name'`, `'Timeout'`, `'Owner'`) — not consumed either way

TF1140 test cases carry TwinCAT `{ attribute '...' := '...' }` pragmas above
the `FUNCTION_BLOCK` line (test display name, execution timeout, owner
metadata) — presumably read by the closed-source `Tc3_PlcTestFramework`/Test
Explorer integration. `TcPouParser.Parse` never reads pragma/attribute text at
all — it only extracts `Name`, `Declaration`, `Implementation/ST`, `Method`s,
and `Property`s from the XML. This is a non-blocking gap (xStunit doesn't need
`Timeout`/`Owner` today) but would matter if any dual-compat design wanted to
surface TF1140's metadata.

## Open questions for TcXunit-229.7 (single vs dual compatibility target)

1. Is there any appetite for treating "one FB is one test case, composed by a
   hand-written `MAIN`/registry PRG" as a *second* discovery convention
   alongside "one FB is a suite of `TEST()`-bracketed methods, discovered by
   `EXTENDS` ancestry"? These are different enough (different granularity,
   different discovery trigger — ancestry vs body registration) that
   supporting both likely means two independent discovery strategies behind
   a common `SuiteDiscovery`-like seam, not a parameterized version of the
   existing one.
2. If TF1140 compat matters, does it need `Tc3_PlcTestFramework.TestController`
   emulated at all (an opaque, closed-source aggregation point), or would a
   real integration only need to run each `FB_TestCaseBase`-descended FB
   directly and treat "no assertion failure + reached `Succeeded()`" as pass —
   sidestepping `TestController` entirely?
3. `AssertEqual`/`Succeeded` naming: does the project want native-stub aliases
   (`AssertEqual` → same code path as `AssertEquals`) or a distinct
   `Tc3_PlcTestFramework`-flavored stub family, given the parameter list
   shape (`expected`/`actual`/`message`, ANY-typed) is already close to
   xStunit's existing `AssertEqualsAny` (`SuiteHost.AssertEqualsAnyCall`)?
4. The pragma attributes (`Name`/`Timeout`/`Owner`) are presently discarded
   entirely by `TcPouParser`. Does `TcXunit-229.8`'s coverage/MC-DC thesis
   want any of that metadata (e.g. per-test timeout enforcement) surfaced,
   or is it out of scope for a coverage-evidence argument?
5. TF1140's sample only exercises the "happy path" 4-case shape (no
   `TEST_ORDERED`-equivalent, no multi-cycle/async pattern visible in this
   sample). Before committing to dual-compat scope, is it worth pulling a
   second, larger real-world TF1140-style project to check whether more
   elaborate suites deviate further (e.g. nested test-case composition,
   parameterized cases) from what this audit saw?

## Related research

A second, separate source was audited for the same `TcXunit-229.2` question:
`docs/research/struccpp-notes.md`, covering
`https://github.com/Autonomy-Logic/STruCpp.git`. STruC++ is an independent
IEC 61131-3-to-C++17 compiler (not a TwinCAT/`TcUnit`-shaped tool at all), so
it doesn't extend this file's discovery/dispatch mismatch analysis — but it
contributes a third distinct ST-test-DSL shape (a standalone `SETUP`/
`TEARDOWN`/`TEST` file format, parsed with its own test-only token set,
entirely unrelated to both TcUnit's suite-FB convention and TF1140's
one-FB-per-case convention) plus a concrete grammar-coverage gap check
(xStunit has no `SHL`/`SHR`/`ROL`/`ROR` support at all) relevant to
`TcXunit-229.7`'s compatibility-target-shape question and `TcXunit-229.8`'s
coverage-evidence scope statement. See that file for details.
