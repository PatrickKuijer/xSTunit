# Tc2_Standard string function plugins

`Tc2_Standard` is compiled-only, like every library TwinCAT ships. A suite
whose call chain reaches `DELETE`, `FIND`, `INSERT`, `LEFT`, `LEN`, `MID`,
`REPLACE`, or `RIGHT` can't resolve them the way it resolves a `FUNCTION` POU
in your own tree (see `samples/TcXunit.SamplePlugins` for the general
native-function-plugin extension point, TcXunit-6k2).

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
