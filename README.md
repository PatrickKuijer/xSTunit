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

Pass `--format json` for structured output (suites → tests → pass/fail/failure messages, a `skipped` array of `{filePath, reason}`, plus overall pass/fail counts and exit code) instead of plain text — useful for a tool consuming results programmatically (e.g. an IDE extension) rather than a human reading console output.

## Issue tracking

This project uses `bd` (beads). Run `bd prime` for workflow context, `bd ready` for available work.
