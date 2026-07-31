# TcXunit

xUnit-style test runner for TwinCAT/IEC 61131-3 PLC code, inspired by TcUnit. Discovers `TcUnit.FB_TestSuite`-derived function blocks in a folder of `.TcPOU` files, interprets their structured-text bodies, and reports pass/fail — no TwinCAT runtime required.

## Why

PLC/TwinCAT codebases typically have no automated test harness: everything is validated by hand-built loopback simulation FBs or by commissioning on real controllers. TcXunit closes that gap by parsing and executing ST directly, so scan-cycle-based state machines can be tested deterministically (no wall-clock waits, no hardware).

## Status

Early / grow-on-demand. The parser and interpreter cover only the constructs exercised by the current fixtures — not the full IEC 61131-3 grammar. See `wiki/` for in-progress design decisions (simulated clock, transport loopback, fault injection, state-mirroring assertions, struct/array support, etc.) that extend this toward full framework-testing capability.

## Architecture

```
src/
  TcXunit.Parser        Parses .TcPOU XML files into a POU/method AST (TcPouParser)
  TcXunit.Interpreter    Lexer/Parser/Engine that executes ST statement bodies over
                         a Cell-based value model; TypeRegistry + SuiteDiscovery find
                         suites by walking EXTENDS ancestry to TcUnit.FB_TestSuite
  TcXunit.Runner         TcUnit native-method stub boundary (assertions, suite host)
  TcXunit.Cli            `tcxunit <path>` entry point (CliRunner is the testable core)
tests/                  xUnit tests per project, mirroring src/
```

Design convention (see comments in `Engine.cs`/`Lexer.cs`): the interpreter is hand-rolled and scoped to what fixtures actually need, extended incrementally rather than building out the full v1 grammar up front.

## Build & test

Requires the .NET SDK (targets `net8.0` for the CLI, `netstandard2.0` for the interpreter/runner/parser libraries).

```bash
dotnet build TcXunit.sln
dotnet test TcXunit.sln
```

## Usage

```bash
dotnet run --project src/TcXunit.Cli -- <path-to-POUs-directory>
```

Scans `<path>` recursively for `*.TcPOU` files, finds any FB type that extends `TcUnit.FB_TestSuite` (directly or transitively), and runs its test methods. Exit code is `0` if all tests pass, `1` if any fail, `2` on usage/discovery errors. Multiple directory args are supported: TcXunit unions the POU sets from each, and errors out if duplicate type names collide across directories.

Files TcXunit can't load — POUs outside the v1 parse subset (`Tc2_System`, `__NEW`, …), malformed XML, unsupported DUT/GVL shapes — are **skipped and reported individually** (`skipped: <path> (<reason>)`, plus a skip count in the summary line) rather than aborting the run, so a real production tree still runs every suite it can. Skips don't change the exit code: a run that completed with skips is still `0`/`1` by test outcome, distinct from the `2` reserved for usage/discovery errors that produced no results at all.

A fault inside a test — an unsupported construct, a call to a method that doesn't exist — **fails that test and lets the rest of the suite run**. Only a fault outside any `TEST()`/`TEST_FINISHED()` bracket fails the suite as a whole.

Pass `--format json` for structured output instead of plain text — useful for a tool consuming results programmatically (an IDE extension, or an agent iterating on ST) rather than a human reading console output. It carries suites → tests → failures, a `skipped` array of `{filePath, reason}`, and overall pass/fail counts and exit code.

Every failure carries a machine-readable `kind`, so a consumer can tell the cases apart without parsing prose:

| `kind` | Meaning | What to do |
| --- | --- | --- |
| `assertion` | An assert compared values and they differed | Fix the code under test, or the expectation |
| `plc-fault` | Interpreted ST faulted at run time | Fix the code under test |
| `unsupported-construct` | Valid IEC 61131-3 that TwinCAT compiles and TcXunit doesn't implement yet, named by the throw site | **Stop** — escalate; never rewrite the POU to make this pass |
| `parse-error` | The lexer/parser couldn't read a body at all — either ST beyond TcXunit's subset, or invalid ST | Open the cited line; fix it if genuinely malformed, **stop** and escalate if it looks like valid ST |
| `load-error` | The container failed — file/XML unreadable, no suites discovered, instantiation failed; nothing ran | Fix the invocation or the tree |

That distinction is the point: without it, an agent seeing a non-empty error deletes a correct `SEL()` call to make a test "pass" and reports success. **`kind` and `construct` are the same two keys at every level** — the top-level error, each suite-level error, and each per-test failure. Per-test failures additionally carry the structured detail behind the message — `assert`, `expected`, `actual`, `assertMessage`, and the location (`pou`, `method`, `bodyLine`, `line`), so which of several asserts in a method failed is unambiguous. `construct` names the unimplemented construct for an `unsupported-construct` (e.g. `SEL`) and the offending token for a `parse-error`; a `parse-error`'s position reuses `bodyLine`/`line` rather than adding a field.

Each failure's message ends with that kind's guidance, so a consumer acting on one JSON object never has to have read this table. (The one exception is the formatted TcUnit assert line — `FAILED TEST 'X', EXP: …, ACT: …` — which is reproduced verbatim.)

The three non-assertion kinds are separated **structurally**, not stylistically: `load-error` is container level (nothing ran at all), `parse-error` is body level (that body never ran; the suite and its other tests did — so it fails a test, exit `1`, and sibling tests still report their own verdicts), and `unsupported-construct` is statement level (a throw site recognized the construct *by name*).

`unsupported-construct` is therefore deliberately **conservative**: it is claimed only where the throw site says so explicitly, never inferred from an exception's base type. The engine raises plain `NotSupportedException` for genuine defects in the code under test too (`Operator '<' is not supported between Int32 and String`), so anything that hasn't opted in reports as `plc-fault`. `parse-error` is the shrinking residue of that rule: the hand-rolled ST front end cannot tell syntax it doesn't implement from syntax that is simply wrong, so it claims neither and says so out loud. As the parser learns to recognize a construct by name, that construct is *promoted* out of `parse-error` into `unsupported-construct` — construct by construct, rather than by building a superset grammar up front.

Pass `--coverage` to additionally list every non-suite POU with the suites exercising it, in both formats. The entries with no suites are the useful ones — a next-task list ("write a suite for `F_ComputeChecksum`"). Association is by direct textual reference from a suite; it's a report, never a gate, and doesn't affect the exit code.

## Native-function plugins

TwinCAT library functions (`Tc2_Utilities.F_CheckSum16`, `Tc2_Standard.F_ToUpper`, …) ship compiled-only — no `.TcPOU` to parse, so a suite that calls one can't resolve it from source. A native-function plugin supplies the behavior from outside the interpreter, so vendor/in-house library implementations don't have to live in this repo.

Implement `ITcXunitNativeFunction` (`src/TcXunit.Interpreter/Extensibility/ITcXunitNativeFunction.cs`) against `TcXunit.Interpreter` referenced with `Private="false"`:

```csharp
using TcXunit.Interpreter.Extensibility;

public sealed class CheckSum16Function : ITcXunitNativeFunction
{
    public string Name => "F_CheckSum16";   // matched case-insensitively

    public object Invoke(NativeCallContext context)
    {
        var size = context.RequireInt32("nSize", 1);
        var seed = context.RequireInt32("nSeed", 2);
        var data = context.RequireBytes("pData", 0, size);

        var sum = seed;
        foreach (var b in data)
            sum += b;

        return sum & 0xFFFF;                // WORD -> int
    }
}
```

Build the plugin project and point the CLI at its output directory:

```bash
dotnet build samples/TcXunit.SamplePlugins -c Release
tcxunit run <path-to-POUs> --plugins samples/TcXunit.SamplePlugins/bin/Release/netstandard2.0
```

Every `*.dll` in the directory is scanned for `ITcXunitNativeFunction` implementations with a public parameterless constructor; a DLL that isn't managed, fails type load, or collides on a function name already registered is skipped and reported — never fatal. Registered functions are consulted last (after intrinsics, methods, and real `FUNCTION` POUs), so a plugin only fills a hole that would otherwise be an error — it can never shadow real source. The VSIX forwards a `plugins` directory from `tcxunit.json` the same way.

Full worked example, argument/return type table, and guarantees: [`samples/TcXunit.SamplePlugins/README.md`](samples/TcXunit.SamplePlugins/README.md) (epic TcXunit-rl4).

## Issue tracking

This project uses `bd` (beads). Run `bd prime` for workflow context, `bd ready` for available work.
