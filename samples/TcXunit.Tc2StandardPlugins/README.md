# Tc2_Standard string function plugins

`Tc2_Standard` is compiled-only, like every library TwinCAT ships. A suite
whose call chain reaches `DELETE`, `FIND`, `INSERT`, `LEFT`, `LEN`, `MID`,
`REPLACE`, or `RIGHT` - or any of their `W`-prefixed `WSTRING` counterparts -
can't resolve them the way it resolves a `FUNCTION` POU in your own tree (see
`samples/TcXunit.SamplePlugins` for the general native-function-plugin
extension point, TcXunit-6k2).

Unlike that project's `F_CheckSum16` (a stand-in for a proprietary,
undocumented vendor algorithm), Tc2_Standard's string functions are publicly
documented and used by nearly every TwinCAT project, so this project ships
real, spec-accurate implementations rather than a worked example (TcXunit-8po).

`CONCAT` is deliberately not part of this bundle: TcXunit-3lt already added it
as an interpreter intrinsic (variadic `STR1..STR10`) before this epic was
filed. Native functions are consulted only as a last resort, after intrinsics,
so a plugin `CONCAT` would never be reached - TcXunit-8po.1 was closed as
already-resolved rather than adding unreachable code.

## Using it

```bash
dotnet build samples/TcXunit.Tc2StandardPlugins -c Release
tcxunit run <path-to-POUs> --plugins samples/TcXunit.Tc2StandardPlugins/bin/Release/netstandard2.0
```

## Functions covered

| Function | Signature                          | Behavior |
|----------|-------------------------------------|----------|
| `DELETE`  | `DELETE(STR, LEN, POS)`             | Removes `LEN` characters from `STR` starting at the 1-based `POS`. |
| `FIND`    | `FIND(STR1, STR2)`                  | 1-based position of the first occurrence of `STR2` in `STR1`, or `0` if absent/empty. |
| `INSERT`  | `INSERT(STR1, STR2, POS)`           | Inserts `STR2` into `STR1` immediately after the 1-based `POS`. |
| `LEFT`    | `LEFT(STR, SIZE)`                   | Leftmost `SIZE` characters of `STR`. |
| `LEN`     | `LEN(STR)`                          | Character count of `STR`. |
| `MID`     | `MID(STR, LEN, POS)`                | `LEN` characters of `STR` starting at the 1-based `POS`. |
| `REPLACE` | `REPLACE(STR1, STR2, L, P)`         | Replaces `L` characters in `STR1` starting at the 1-based `P` with `STR2`. |
| `RIGHT`   | `RIGHT(STR, SIZE)`                  | Rightmost `SIZE` characters of `STR`. |

Out-of-range position/size arguments clamp rather than throw, matching each
ticket's acceptance criteria (TcXunit-8po.2 through TcXunit-8po.9).

## WSTRING counterparts (TcXunit-93l9)

The wide-character half of the same set. Identical semantics - 1-based
indexing, clamp rather than throw - so narrow and wide are two registrations
over one shared body (`StringOperations.cs`), differing only in the function
name and the `CharacterMeasure` the body counts with (TcXunit-p4qb).

| Function   | Signature                     | Behavior |
|------------|-------------------------------|----------|
| `WCONCAT`  | `WCONCAT(STR1, STR2, ... STR10)` | Concatenates `STR1`/`STR2` plus any supplied `STR3..STR10`, in declared order. |
| `WDELETE`  | `WDELETE(STR, LEN, POS)`      | Removes `LEN` characters from `STR` starting at the 1-based `POS`. |
| `WFIND`    | `WFIND(STR1, STR2)`           | 1-based position of the first occurrence of `STR2` in `STR1`, or `0` if absent/empty. |
| `WINSERT`  | `WINSERT(STR1, STR2, POS)`    | Inserts `STR2` into `STR1` immediately after the 1-based `POS`. |
| `WLEFT`    | `WLEFT(STR, SIZE)`            | Leftmost `SIZE` characters of `STR`. |
| `WLEN`     | `WLEN(STR)`                   | Character count of `STR`. |
| `WMID`     | `WMID(STR, LEN, POS)`         | `LEN` characters of `STR` starting at the 1-based `POS`. |
| `WREPLACE` | `WREPLACE(STR1, STR2, L, P)`  | Replaces `L` characters in `STR1` starting at the 1-based `P` with `STR2`. |
| `WRIGHT`   | `WRIGHT(STR, SIZE)`           | Rightmost `SIZE` characters of `STR`. |

**`WCONCAT` is implemented here, unlike `CONCAT`.** The intrinsic that makes a
plugin `CONCAT` unreachable is dispatched by an exact ordinal name match on
`"CONCAT"` (`Engine.Expressions.cs`); `"WCONCAT"` does not hit it, and no other
intrinsic mentions `WCONCAT`. A `WCONCAT` call therefore falls all the way
through to the native-function registry. `CliRunnerWideStringPluginTests`
pins this down by asserting that, with no plugin loaded, `WCONCAT` reports as
*unresolved* rather than quietly concatenating.

**One "character" is one UTF-16 code unit**, matching what TwinCAT's `WSTRING`
stores and what .NET's `string.Length`/`Substring` operate on - so no
surrogate-aware handling is added. A non-BMP character (a surrogate pair)
therefore counts as 2, in both TwinCAT and here; counting Unicode scalar
values instead would make these functions disagree with the PLC they are
standing in for.

That makes `CharacterMeasure.Wide` exact. `CharacterMeasure.Narrow` is the
same code-unit measure today but is only an approximation: TwinCAT counts a
narrow `STRING` in bytes, so the narrow half disagrees with the PLC on any
input above U+007F. That is tracked separately (TcXunit-ielv) and is why the
shared body takes its arithmetic from a measure rather than calling
`string.Length`/`Substring` directly - fixing it should be a change to one
field, not an unpicking of the shared body.
