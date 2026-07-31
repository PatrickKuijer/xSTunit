# Grammar source: keep hand-rolled, or borrow iec61131/RuSTy EBNF

- Issue: `TcXunit-229.5` (part of epic `TcXunit-229`, blocks `TcXunit-229.6`
  "Split parse-superset from execute-time classification").
- Method: primary-source only — `crates.io` crate/API pages, the actual
  GitHub repos (README, `Cargo.toml`, source tree, `LICENSE`/`COPYING`
  files), the GitHub REST API for stars/issues/license/activity metadata.
  No blog posts or secondhand summaries used for any factual claim below.
  Neither candidate repo was cloned into the scratchpad — everything here
  comes from GitHub's web UI/API and crates.io's web UI/API, fetched live.

## Verdict: keep the hand-rolled lexer/parser. Do not adopt either candidate's grammar, in any form.

Neither candidate offers what would actually move `TcXunit-229.6` forward.
`iec61131` (the crate) has no standalone grammar artifact to port at all —
its EBNF source is an external, unlinked input to a private generator, not
something in the repo to read. RuSTy has no grammar artifact either — it is
explicitly hand-rolled recursive descent, the same architecture xStunit
already uses, so "borrowing" it would mean porting Rust parsing *logic*
rather than referencing a grammar, and its parsing code is LGPL/GPL-licensed,
which is a genuine contamination risk for a permissively-licensed .NET
project if copied rather than merely read for inspiration. Both projects are
solving a different-shaped problem — full IEC 61131-3 compilation across all
five languages or heavy static analysis — while xStunit interprets ST bodies
of TcUnit-style suite POUs for assertion execution and grows its grammar
incrementally per fixture need (`CLAUDE.md`). There is no grammar-portability
shortcut available here; the fastest path to `229.6`'s parse-superset /
execute-time-classification split is still to keep extending xStunit's own
recursive-descent grammar against real fixture gaps (per the STruC++
audit in `docs/research/struccpp-notes.md`, e.g. the confirmed `SHL`/`SHR`/
`ROL`/`ROR` gap), not to import either candidate.

## Candidate 1: `iec61131` crate (crates.io) / `radevgit/plc` (GitHub)

- **Language coverage**: ST (Structured Text) only, fully implemented today.
  IL, LD, FBD, SFC are explicitly listed as *planned*, not present.
  Source: crate README via
  [crates.io/crates/iec61131](https://crates.io/crates/iec61131) and
  [raw README](https://raw.githubusercontent.com/radevgit/plc/main/iec61131/README.md).
- **Grammar artifact**: the README states the crate is "generated from the
  official IEC 61131-3:2013 EBNF specification using the `plcp/iec61131`
  parser generator" — but the repo (`radevgit/plc`) does **not** contain a
  standalone grammar file. Directory listing for
  [`radevgit/plc/iec61131`](https://github.com/radevgit/plc/tree/main/iec61131)
  shows only `src/`, `tests/`, `CHANGELOG.md`, `Cargo.toml`, `LICENSE`,
  `README.md` — no `.ebnf`/`.bnf`/`.pest`/`.lalrpop`/`.g4` file, and the
  `plcp` generator itself is referenced only by name, never linked or
  vendored in this repo. The generator and the actual EBNF source text it
  consumes are both external and unverifiable from this repo — there is
  nothing here that could be independently read or ported as a grammar
  artifact; only the generated Rust parser code exists in-repo. The
  underlying spec is the paywalled/purchase-only official IEC 61131-3:2013
  standard document, not an openly published EBNF file.
- **License**: MIT, confirmed both from the crates.io API
  (`GET /api/v1/crates/iec61131` → `"license": "MIT"`) and the repo's
  top-level `LICENSE` file referenced in the directory listing above. MIT is
  fully compatible with porting into a .NET project (permissive, no copyleft
  obligations) — but there is no grammar artifact to port, only generated
  Rust source, and porting *that* would mean reading/reimplementing
  MIT-licensed Rust code, not referencing a grammar.
- **Maturity signals**: young. `created_at: 2025-12-11` on crates.io (i.e.
  first published roughly 7 months before this audit), 3 published versions
  (`0.6.0`, `0.6.1`, `0.7.0`, all published within hours of each other on
  2025-12-11 per
  [crates.io API](https://crates.io/api/v1/crates/iec61131)), 814 total
  downloads. The parent GitHub repo `radevgit/plc` has 13 stars, 3 forks,
  MIT license, and commit activity extending into mid-2026
  ([`radevgit/plc` commits](https://github.com/radevgit/plc/commits/main)) —
  "moderately active" but with a very short track record and a very small
  user base (three-digit download count, low star count). The crate itself
  markets as "production ready," but that claim has no external validation
  signal (no independent adopters visible, no CI badge content confirmed in
  this pass).

## Candidate 2: RuSTy (`github.com/PLC-lang/rusty`)

- **Parser approach**: confirmed from the project's own architecture doc —
  [`book/src/arch/parser.md`](https://raw.githubusercontent.com/PLC-lang/rusty/master/book/src/arch/parser.md)
  states RuSTy uses a two-stage pipeline: a `logos`-crate-based lexer (token
  types for ST's large keyword set), then an explicitly **hand-written
  recursive descent parser** — the doc states the choice of manual
  implementation over a parser-generator was deliberate, "to gain more
  control and more understanding of the parsing process." AST nodes are
  individually-typed structs (not generic nodes), enforcing structural
  correctness at compile time. Confirmed independently via `Cargo.toml`:
  `logos = "0.12.0"` is the only lexing/parsing dependency; no
  `pest`/`lalrpop`/`chumsky`/`nom`/`peg` anywhere in the manifest.
- **Grammar artifact**: none. No `.pest`/`.lalrpop`/`.g4`/`.ebnf`/`.bnf`
  file exists anywhere in the repo tree (checked via the GitHub git-trees
  API over the full recursive tree). This is architecturally the *same*
  choice xStunit already made — hand-rolled recursive descent, no formal
  grammar artifact to read or port — so there is nothing grammar-shaped to
  borrow here; the only thing "borrowable" would be Rust parsing *code*,
  which is a license and language-porting problem, not a grammar-reference
  one.
- **Language coverage**: ST only (per repo description "Structured Text
  Parser and LLVM Frontend" —
  [`api.github.com/repos/PLC-lang/rusty`](https://api.github.com/repos/PLC-lang/rusty)).
  No evidence found of LD/FBD/SFC/IL support; RuSTy is a full ST-to-native
  compiler (via LLVM/`inkwell`, confirmed in `Cargo.toml` dependencies),
  materially larger in scope than xStunit needs (OOP surface, full codegen
  pipeline, standard library in `libs/stdlib`).
- **License**: dual LGPL-3.0/GPL-3.0, confirmed via the GitHub API
  (`license.spdx_id: "LGPL-3.0"`) and via the repo's own `COPYING` (GPL-3.0)
  and `COPYING.LESSER` (LGPL-3.0) files, both listed in the top-level repo
  tree. This is the material licensing distinction from `iec61131`: LGPL/GPL
  are copyleft. Reading RuSTy's source for architectural inspiration is
  fine, but **copying or closely porting its parser code** into xStunit
  would pull copyleft obligations into a project that currently carries no
  such constraint — a real compatibility problem worth flagging even though
  it's moot here, since there is no grammar artifact (as opposed to code) to
  port in the first place. Porting *grammar rules* (informal knowledge of
  "here's how ST's CASE statement is shaped") learned by reading LGPL code is
  a much greyer, lower-risk case than porting actual parsing functions
  verbatim — this project should treat "reading RuSTy for how they structured
  a rule" as fine and "translating RuSTy source line-by-line into C#" as a
  license violation risk to avoid.
- **Maturity signals**: substantial. 352 stars, 71 forks, 188 open issues,
  created 2019-11-23, most recent push `2026-07-29` (same day as this audit)
  — actively maintained, long track record
  ([`api.github.com/repos/PLC-lang/rusty`](https://api.github.com/repos/PLC-lang/rusty)).
  1,717+ commits on `master`. This is a real, maintained, industry-adjacent
  compiler project (part of the `PLC-lang` org, documented at
  [plc-lang.github.io/rusty](https://plc-lang.github.io/rusty/)) — far more
  mature than `iec61131`, but its maturity doesn't translate into anything
  xStunit can use here, precisely because there's no grammar artifact and
  the code is copyleft.

## Reasoning against what xStunit actually needs

xStunit is not building an IEC 61131-3 compiler — it interprets ST bodies of
TcUnit-shaped test-suite POUs to run assertions, and per `CLAUDE.md` it grows
its interpreter "incrementally per fixture need... not built to full spec up
front." Against that mission:

1. **There is nothing concrete to port from either candidate.** "Borrow the
   grammar" presumes a standalone grammar artifact (EBNF/BNF/PEG file)
   independent of the host language's implementation. Neither candidate has
   one: `iec61131`'s EBNF source is an external, unlinked/paywalled
   standard-document input to a private generator tool, and RuSTy has no
   grammar file at all because it's hand-rolled — same as xStunit already
   is. The premise behind option 2 in the issue title ("iec61131/RuSTy
   EBNF") doesn't hold up under inspection of either repo: neither ships an
   EBNF artifact in-repo.
2. **iec61131's only usable asset is MIT-licensed generated Rust source**,
   which is a different language, a nine-months-old project with a tiny
   download/star footprint, and (per its own README) still missing 4 of 5
   IEC 61131-3 languages — though xStunit only needs ST anyway, so that gap
   is irrelevant. Its "production ready" self-description has no
   independent corroboration found in this pass. Low risk to reference for
   ideas (permissive license), but also low payoff — a brand-new project run
   by presumably one or a small group of maintainers, with no evidence its
   ST coverage is broader or more battle-tested than xStunit's own
   fixture-driven grammar.
3. **RuSTy's only usable asset is LGPL/GPL-licensed Rust source implementing
   the exact same architecture xStunit already has** (hand-rolled recursive
   descent over a token stream) — reading it for inspiration on specific
   rules (e.g. how it structures `CASE` or handles operator precedence) is
   plausible and safe if done as "read then reimplement independently in
   C#," but there's no shortcut here: no grammar file to drop in, and
   copying code verbatim would be a license problem. RuSTy's scope (full
   OOP, LLVM codegen, standard library) is also a compiler for real
   production PLC codebases, not a narrow test-fixture interpreter — most of
   what makes RuSTy "mature" (codegen, LLVM integration, stdlib) is
   irrelevant surface area for xStunit's mission.
4. **xStunit's existing incremental strategy already produces evidence this
   works**: the STruC++ audit (`docs/research/struccpp-notes.md`) found
   xStunit's current statement/control-flow surface (`IF/ELSIF/ELSE`,
   `CASE`, `FOR/WHILE/REPEAT`, `EXIT`) already matches what a much larger,
   independently-built ST implementation considers its core statement set,
   with the only confirmed gap being expression-level bitwise-shift
   operators (`SHL`/`SHR`/`ROL`/`ROR`) — a small, well-scoped, independently
   addable gap, not evidence that the hand-rolled approach is falling behind
   or needs a wholesale grammar replacement.

**Recommendation for `TcXunit-229.6`**: proceed with splitting parse-superset
from execute-time classification directly against xStunit's own hand-rolled
grammar. Treat both candidates as optional secondary reading (RuSTy's
`book/src/arch/parser.md` and AST-design rationale in particular, since it
argues for the same typed-node, hand-rolled approach xStunit already uses)
rather than as a grammar or code source to import. If a future fixture needs
an IEC 61131-3 construct xStunit doesn't yet parse, resolve it the way the
STruC++ gap was resolved — name the specific missing token/rule and add it
directly — rather than reaching for either candidate's grammar.

## Related research

This continues the `TcXunit-229` research thread alongside
`docs/research/tf1140-audit.md` and `docs/research/struccpp-notes.md`
(`TcXunit-229.2`). Those two are compatibility-target-shape audits
(`229.7`); this one is a grammar-source audit (`229.5`) feeding the
parse-superset/execute-time-classification split (`229.6`). The STruC++
audit's confirmed `SHL`/`SHR`/`ROL`/`ROR` gap is the most concrete, already-
identified "grow the grammar" candidate this note is aware of — worth
picking up directly rather than through either `iec61131` or RuSTy.
