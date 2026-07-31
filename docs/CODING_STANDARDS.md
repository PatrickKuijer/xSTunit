# Coding Standards

Applies to all `.cs` files under `src/`, `tests/`, and `samples/`. This is the
bar new code and code review are held to. For agent workflow (beads, git
policy, session close) see [CLAUDE.md](../CLAUDE.md) / [AGENTS.md](../AGENTS.md);
this file is the quality standard those workflows point at.

## Comments

The house comment style lives in [CLAUDE.md](../CLAUDE.md#comment-standard) —
XML doc always on public/internal API surface, inline comments state WHY not
WHAT, no ticket IDs or change-history prose in comments. Do not duplicate that
text here; update it in one place and mirror the edit into AGENTS.md.

One durability test on top of that, for comments written mid-task by any
contributor (human or agent): **a comment must be true and useful to a reader
with no access to the task, issue, or conversation that produced it.**
Comments that reference "the problem you mentioned," explain the diff rather
than the code, or narrate a decision process get deleted in review, not
reworded.

## Doc comments (`///`)

Required on every public and internal type, member, and parameter — see the
Comment Standard for what goes in `<summary>`/`<param>`/`<returns>`/
`<exception>`/`<remarks>`. This is enforced by the compiler, not just
convention: `<GenerateDocumentationFile>true</GenerateDocumentationFile>` on a
project turns a missing doc comment into `CS1591`. Until that's wired into
every `.csproj`, treat a missing `<summary>` on public/internal surface as a
review-blocking finding, same as a compiler warning would be.

## Naming

Standard .NET conventions: `PascalCase` for types/members/namespaces,
`camelCase` for locals/parameters, `_camelCase` for private fields, interfaces
prefixed `I`. Namespaces mirror the `src/` directory tree.

**Do not reuse the `Tc`/`TC` namespace or Beckhoff's own naming conventions**
(`Tc2_*`, `TcPOU`, `Tc*` prefixes) **in any new identifier** — namespace,
project, class, file, CLI flag, or config key. This repo is mid-rename away
from TwinCAT/Beckhoff-adjacent branding (epic `TcXunit-tbih`); existing names
that mirror Beckhoff file formats (e.g. `TcPouParser`) are legacy and tracked
for rename, not a pattern to extend. If a new type needs to reference a
TwinCAT/Beckhoff concept, name it after the IEC 61131-3 or domain concept
instead (e.g. `BistableLatchHost`, not `TcBistableHost`).

## Nullability

Interpreter/runner/parser libraries target `netstandard2.0` with
`<Nullable>disable</Nullable>` — don't add nullable annotations to those
projects piecemeal; it's a project-wide switch, not a per-file one. If a
project already has nullable enabled, keep it enabled and annotate honestly
(no blanket `!`-suppression to silence warnings).

## Project & test structure

- One `src/xStunit.X` project per architectural layer (`Parser`, `Interpreter`,
  `Runner`, `Cli`, `Vsix`); dependencies flow one direction — see the
  Architecture Overview in [CLAUDE.md](../CLAUDE.md).
- Tests live in `tests/xStunit.X.Tests`, mirroring `src/xStunit.X`. A new
  public type gets a same-named test file in the mirrored location, not a
  catch-all test file.
- Test names describe the behavior under test (`MethodUnderTest_Scenario_Expected`
  or equivalent), not the test's position in a sequence.
- Test comments state the invariant the test pins (what breaks if it goes
  red), per the Comment Standard — not what the arrange/act/assert lines do
  mechanically.

## Errors and control flow

- Exit codes for `xStunit.Cli` are part of the public contract (`0`/`1`/`2`,
  see CLAUDE.md) — don't repurpose them or add new ones without updating that
  contract and its docs together.
- Catch narrowly. A catch-and-summarize boundary (e.g. `CliRunner`,
  `SuiteCaseRunner`) is a deliberate design choice for surfacing partial
  failures to the caller — don't add speculative `catch (Exception)` blocks
  elsewhere to swallow errors "just in case."
- Unloadable/malformed input files are skipped and reported per file; they
  must never crash the run or produce exit code `2` for anything other than
  usage/discovery errors.

## Scope discipline

Interpreter/parser are hand-rolled and scoped to what fixtures actually need
(see `Engine.cs`/`Lexer.cs`) — extend incrementally to satisfy a real test
case, don't build out general IEC 61131-3 grammar support speculatively ahead
of a concrete need.
