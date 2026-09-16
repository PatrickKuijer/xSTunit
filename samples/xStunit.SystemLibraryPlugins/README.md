# Tc2_System plugins

`Tc2_System` is the manufacturer-specific half of the TwinCAT base install —
ADS, file access, event logging, memory, bit/character/time helpers. Like every
library TwinCAT ships it is compiled-only, so a suite whose call chain reaches
`F_CreateAmsNetId` or instantiates `FB_FileOpen` can't resolve it the way it
resolves a POU in your own tree (see `samples/xStunit.SamplePlugins` for the
plugin extension points themselves).

Four members of the library are already interpreter intrinsics — `MEMCPY`,
`MEMSET`, `MEMMOVE` and `TestAndSet`. Native plugins are consulted only as a
last resort, after intrinsics, so re-implementing those here would be
unreachable code.

Behavior is taken from the vendor documentation, not guessed:
<https://infosys.beckhoff.com/content/1033/tcplclib_tc2_system/30862219.html>

## What's here

| Symbol | Shape | Notes |
|---|---|---|
| `F_CreateAmsNetId` | function | six octets → the dotted decimal `T_AmsNetID` text |
| `F_ToASC` / `F_ToCHR` | functions | character ↔ Latin-1 byte, on the interpreter's own narrow-STRING model |
| `F_GetSystemTime` / `F_GetTaskTime` | functions | the simulated clock, never wall time; 100 ns intervals since 1601 |
| `SETBIT32` / `CLEARBIT32` / `GETBIT32` / `CSETBIT32` | functions | `bitNo` wraps modulo 32, as documented |
| `MEMCMP` | function | `16#FF` for a null pointer or zero length, not a fault |

## Using it

```bash
dotnet build samples/xStunit.SystemLibraryPlugins -c Release
xstunit <path-to-POUs> --plugins samples/xStunit.SystemLibraryPlugins/bin/Release/netstandard2.0
```

## Why these live in a plugin rather than the interpreter

Vendor-library behavior stays out of the shipped assemblies. The interpreter
owns the two contracts (`IXstunitNativeFunction`,
`IXstunitNativeFunctionBlock`) and nothing about any particular library; a
downstream project that needs a different version, a different vendor, or its
own in-house library supplies it the same way this project does.
