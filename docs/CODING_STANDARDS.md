# Coding Standards

Applies to all `.cs` files under `src/`, `tests/`, and `samples/`. This is the
bar new code and code review are held to. For agent workflow (beads, git
policy, session close) see [CLAUDE.md](../CLAUDE.md) / [AGENTS.md](../AGENTS.md);
this file is the quality standard those workflows point at.

## Comments

The house comment style lives in [CLAUDE.md](../CLAUDE.md#comment-standard) —
good code doesn't need comments, delete rather than reformat, inline comments
state WHY not WHAT, no ticket IDs or change-history prose. Do not duplicate
that text here or in `AGENTS.md`; `CLAUDE.md` is the one copy, and `AGENTS.md`
points at it rather than mirroring it.

Two durability tests on top of that, for comments written mid-task by any
contributor (human or agent):

- **A comment must be true and useful to a reader with no access to the task,
  issue, or conversation that produced it.** Comments that reference "the
  problem you mentioned," explain the diff rather than the code, or narrate a
  decision process get deleted in review, not reworded.
- **A comment that fails the bar is deleted, not polished.** Reformatting bad
  prose into well-formatted bad prose gives it a second life it didn't earn,
  and a comment that survives review is one future readers will trust. Prefer
  no comment over one that may go stale.

## Doc comments (`///`)

**Not required on every member.** Write a `<summary>` only where it tells a
caller something the signature doesn't already — what a non-obvious return
means, what state a type represents, why a type exists at all. A `Name`
property of type `string` needs nothing; adding "The name." is noise that will
outlive its accuracy. See the Comment Standard for what belongs in
`<param>`/`<returns>`/`<exception>`/`<remarks>`.

Document without fail: **exceptions a caller must handle**, and **parameters
or return values with non-obvious semantics** (units, ranges, null/empty
meaning, ownership).

`<GenerateDocumentationFile>` is on for the three consumable libraries
(`Interpreter`, `Runner`, `Parser`) via `Directory.Build.props`, with
`CS1591` in `NoWarn` — the flag is an emit switch for docgen/IntelliSense,
deliberately **not** a "document everything" enforcement mechanism. A missing
`<summary>` is therefore not a review finding; a *wrong* or *redundant* one is.

One compiler constraint to know: `CS1573` makes `<param>` all-or-nothing per
member. Documenting some parameters and not others warns, so once one
parameter genuinely needs a tag, give the remaining ones a short one too.

## Naming

Standard .NET conventions: `PascalCase` for types/members/namespaces,
`camelCase` for locals/parameters, `_camelCase` for private fields, interfaces
prefixed `I`. Namespaces mirror the `src/` directory tree.

**Do not reuse the `Tc`/`TC` namespace or Beckhoff's own naming conventions**
(`Tc2_*`, `TcPOU`, `Tc*` prefixes) **in any new identifier** — namespace,
project, class, file, CLI flag, or config key. The product is named `xStunit`,
deliberately distinct from TwinCAT/Beckhoff-adjacent naming to avoid trademark
confusion. If a new type needs to reference a TwinCAT/Beckhoff concept, name it
after the IEC 61131-3 or domain concept instead (e.g. `BistableLatchHost`, not
`TcBistableHost`).

One deliberate exception, so it is not "fixed" by a later sweep: the
`.TcPOU`/`.TcDUT`/`.TcGVL` **file-format** parsers keep their `Tc` prefix —
`TcPouParser`, `TcDutParser`, `TcGvlParser`, `TcPouRejectedException`. They
parse TwinCAT's specific XML wrapper and cannot read portable ST, so `Tc` names
what they actually are; a name like `PouParser` would promise format-agnostic
parsing they do not deliver. The prohibition targets *branding*, not accurate
description of a format-specific adapter. Types that are ours rather than
Beckhoff's take the `Xstunit` token instead (`XstunitLog`,
`IXstunitNativeFunction`) — capital `X` in PascalCase identifiers, the
stylized lowercase `xStunit` only in namespaces and the CLI name.

This section is the full rule. `CLAUDE.md` and `AGENTS.md` carry the
prohibition itself in short form and point here for the reasoning and the
exception; keep all three saying the same thing.

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
- Catch narrowly by default — don't add speculative `catch (Exception)` blocks
  to swallow errors "just in case."
- The exception is a deliberate **isolation boundary**: `CliRunner`,
  `SuiteCaseRunner` and `StructuralParseGuard` catch broadly *on purpose*, so
  one bad file or suite can't abort a whole run. Narrowing those to an
  enumerated list of "expected" exception types is a regression, not a
  tidy-up: anything off the list escapes past every caller's handler and takes
  the remaining scan down with it. Their doc comments say so — read before
  "fixing".
- Unloadable/malformed input files are skipped and reported per file; they
  must never crash the run or produce exit code `2` for anything other than
  usage/discovery errors.

## Scope discipline

Interpreter/parser are hand-rolled and scoped to what fixtures actually need
(see `Engine.cs`/`Lexer.cs`) — extend incrementally to satisfy a real test
case, don't build out general IEC 61131-3 grammar support speculatively ahead
of a concrete need.
