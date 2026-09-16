# Native plugins

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

## Stateful function blocks

A vendor library `FUNCTION_BLOCK` is not a function: it carries state from one
PLC cycle to the next, publishes `VAR_OUTPUT`s a later cycle reads back, and
usually runs a `bExecute`/`bBusy`/`bError` handshake spanning several
invocations. `IXstunitNativeFunctionBlock` is the FB-shaped sibling of
`IXstunitNativeFunction` for exactly that — see `DemoHandshakeBlock.cs`:

```csharp
public sealed class DemoHandshakeBlock : IXstunitNativeFunctionBlock
{
    private int _remaining;                 // survives between invocations

    public string TypeName => "FB_DemoHandshake";

    // Every slot ST touches, inputs included - nothing allocates one later.
    public IReadOnlyList<NativeFieldDeclaration> Fields => new[]
    {
        new NativeFieldDeclaration("bExecute", false),
        new NativeFieldDeclaration("bBusy", false),
    };

    // What fbDemo(TRUE) binds to, in IEC declaration order.
    public IReadOnlyList<string> PositionalInputNames => new[] { "bExecute" };

    // What fbDemo.Abort() routes here; anything else is left to normal dispatch.
    public IReadOnlyList<string> MethodNames => new[] { "Abort" };

    // One per declared variable: two FBs in a tree must not be one FB.
    public IXstunitNativeFunctionBlock CreateInstance() => new DemoHandshakeBlock();

    public object Invoke(NativeFunctionBlockCall call) { /* read/write call fields */ }
}
```

One registered object plays two roles. The instance you register is the
**prototype** — only its `TypeName`/`Fields`/`PositionalInputNames`/
`MethodNames` are ever read. `CreateInstance()` then produces the per-variable
state object the interpreter actually drives, and only those receive `Invoke`.
Returning `this` silently makes every variable of that type the same block.

A bare `fbDemo(bExecute := TRUE)` arrives with its arguments already bound into
the instance's fields, so inputs are read with `call.GetField` rather than from
`call.Arguments`; outputs are published with `call.SetField` and ST reads them
straight back by dot access. A method call arrives with `call.MethodName` set
and its arguments in `call.Arguments`, and whatever it returns becomes the
expression's value in ST.

Unlike the pure-function surface, a block may write: `call.SetField` for its own
outputs, and `call.WriteBytes` for the `VAR_IN_OUT` buffer a read-shaped vendor
FB fills on the caller's behalf. It still never sees the `Engine`,
`TypeRegistry`, or the caller's `Frame`.

## Using one

```bash
dotnet build samples/xStunit.SamplePlugins -c Release
xstunit <path-to-POUs> --plugins samples/xStunit.SamplePlugins/bin/Release/netstandard2.0
```

Every `*.dll` in the directory is scanned for `IXstunitNativeFunction` and
`IXstunitNativeFunctionBlock` implementations with a public parameterless
constructor. A DLL that isn't a managed assembly, fails type load, or collides
on a name already registered is **skipped and reported** — never fatal, matching
how unloadable POUs are handled.

## Guarantees worth knowing

- **A plugin can never shadow real source.** Registered functions are consulted
  last, after intrinsics, methods, bare-invoked FB fields, native hosts, and
  real global `FUNCTION` POUs. A plugin block type is likewise only reached once
  the ancestry walk has left the type registry without finding source, and after
  every in-tree stub (`TON`, `R_TRIG`, `RS`, `CTU`, …) has been matched. If your
  tree contains the source, the source wins. A plugin only fills a hole that
  would otherwise be an error.
- **Plugins can't reach into program state.** Arguments arrive already
  evaluated as CLR values; a plugin never sees the `Engine`, `TypeRegistry`, or
  `Frame`, cannot evaluate ST, and cannot observe the caller's scope. Pointer
  access via `RequireBytes` is **read-only**; a function block additionally
  reaches its own instance's fields and, through `WriteBytes`, whatever buffer
  the caller explicitly pointed it at — nothing else.
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
from a vendor spec) in its own plugin. `F_DemoUpperCase` and `FB_DemoHandshake`
are openly invented names, so they make no claim about any vendor function or
block at all.
