# Native-function plugins

Every library shipped with TwinCAT (`Tc2_Utilities`, `Tc2_Standard`, …) is
compiled-only. There is no `.TcPOU` to parse, so a suite whose call chain
reaches `F_CheckSum16` can never resolve it the way it resolves a `FUNCTION`
POU in your own tree.

A native-function plugin supplies those functions from outside the
interpreter, so they don't have to be hand-written into the intrinsic
if-chain in `Engine.Expressions.cs` one at a time.

## Why a plugin rather than a PR

The implementation of a vendor or in-house library function usually belongs to
whoever owns the requirement, not to this repo:

- **Proprietary logic stays private.** A company's real checksum/protocol/
  business behavior lives in that company's own plugin assembly, in that
  company's own repository. Nothing about it lands here.
- **Grow-on-demand stays honest.** xStunit implements what its fixtures
  actually exercise. A function only one downstream project needs is that
  project's plugin, not xStunit's permanent maintenance burden.

## Writing one

Reference `xStunit.Interpreter` with `Private="false"` (see this project's
`.csproj` for why that matters) and implement `IXstunitNativeFunction`:

```csharp
using xStunit.Interpreter.Extensibility;

public sealed class CheckSum16Function : IXstunitNativeFunction
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

Each `Require*` accessor takes the parameter's **name** and its **position**,
because TwinCAT lets a caller supply arguments either way — resolution tries
the name first, then the slot.

Return values must use the interpreter's CLR representation for the declared
IEC type: `BOOL`→`bool`, `SINT`/`USINT`/`BYTE`/`INT`/`UINT`/`WORD`/`DINT`→`int`,
`UDINT`/`DWORD`/`LINT`→`long`, `ULINT`/`LWORD`→`ulong`, `REAL`→`float`,
`LREAL`→`double`, `STRING`→`string`. Return `null` for no return value. The
`Require*` accessors already produce these shapes, so reading arguments through
them and returning one of their results stays consistent by construction.

## Using one

```bash
dotnet build samples/xStunit.SamplePlugins -c Release
xstunit <path-to-POUs> --plugins samples/xStunit.SamplePlugins/bin/Release/netstandard2.0
```

Every `*.dll` in the directory is scanned for `IXstunitNativeFunction`
implementations with a public parameterless constructor. A DLL that isn't a
managed assembly, fails type load, or collides on a function name already
registered is **skipped and reported** — never fatal, matching how unloadable
POUs are handled.

## Guarantees worth knowing

- **A plugin can never shadow real source.** Registered functions are consulted
  last, after intrinsics, methods, bare-invoked FB fields, native hosts, and
  real global `FUNCTION` POUs. If your tree contains the source, the source
  wins. A plugin only fills a hole that would otherwise be an error.
- **Plugins can't reach into program state.** Arguments arrive already
  evaluated as CLR values; a plugin never sees the `Engine`, `TypeRegistry`, or
  `Frame`, cannot evaluate ST, and cannot observe the caller's scope. Pointer
  access via `RequireBytes` is **read-only**.
- **Unresolved and unplugged is still a clear error**, naming the function and
  suggesting a plugin — not a `NullReferenceException`.
- **Loading a DLL runs its code.** Point `--plugins` only at a directory you
  control.

## About the sample's checksum

`CheckSum16Function` borrows the real name `F_CheckSum16` but its algorithm is a
plain additive sum chosen to be verifiable by hand in a fixture — **it is not
Beckhoff's**. Beckhoff ships `Tc2_Utilities` compiled and doesn't document the
internals, so no faithful reproduction is possible from outside. A real
deployment must supply the actual algorithm (measured against a real PLC, or
from a vendor spec) in its own plugin. `F_DemoUpperCase` is an openly invented
name, so it makes no claim about any vendor function at all.
