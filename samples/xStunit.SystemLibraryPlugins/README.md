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
| `ADSLOGSTR` / `ADSLOGDINT` / `ADSLOGLREAL` | functions | recorded into a sink a suite can assert on |
| `F_AdsLogCount` / `F_AdsLogClear` / `F_AdsLogMask` / `F_AdsLogFormat` / `F_AdsLog*Arg` | functions | **xStunit's own**, not vendor symbols — the read side of that sink |
| `FB_IecCriticalSection` | block | enter/leave bookkeeping; mutual exclusion is not modelled |
| `FB_FileOpen` / `FB_FileClose` / `FB_FileRead` / `FB_FileWrite` / `FB_FileGets` / `FB_FilePuts` / `FB_FileSeek` / `FB_FileTell` / `FB_EOF` / `FB_FileDelete` / `FB_FileRename` / `FB_CreateDir` / `FB_RemoveDir` | blocks | over an in-memory filesystem, never real disk |
| `F_FileSystemClear` / `F_FileSystemPutText` / `F_FileSystemGetText` / `F_FileSystemExists` / `F_FileSystemDirExists` / `F_FileSystemSize` / `F_FileSystemOpenHandleCount` | functions | **xStunit's own** — seed and inspect that filesystem from ST |

## Using it

```bash
dotnet build samples/xStunit.SystemLibraryPlugins -c Release
xstunit <path-to-POUs> --plugins samples/xStunit.SystemLibraryPlugins/bin/Release/netstandard2.0
```

## The ADS log sink

The `ADSLOG*` functions have no return value worth computing: their entire
effect on a real system is a side effect a suite cannot see. They are recorded
here instead, so a test can assert that a POU logged what it should have rather
than merely that the call did not fault.

What is recorded is the mask, the format string **unsubstituted**, and the
argument in its own type. TwinCAT's `%s`/`%d`/`%f` substitution is C printf
formatting whose exact rendering is not documented and has not been measured
against a real PLC, so nothing here claims one.

The sink is process-wide and outlives a suite — a native `FUNCTION` has no
instance to hang state on, and the CLI loads one set of plugins for a whole
run. A test asserting on a count calls `F_AdsLogClear()` first.

## What `FB_IecCriticalSection` does not model

xStunit runs one interpreted task and has no scheduler, so `Enter` can never be
blocked by another task and mutual exclusion is not simulated. What is
reproduced is the bookkeeping the vendor documents — `Leave` answers `FALSE`
for a section that was not previously entered — because an unbalanced
`Enter`/`Leave` on some error path is a real defect that shows up without any
concurrency at all. Nesting is counted rather than collapsed, so correct nested
code does not read as a failure.

## The file-access family

Backed by an in-memory filesystem, never real disk. A suite that wrote to the
machine would leave artefacts behind, race other runs, and behave differently
depending on what the running user may write — and it could not seed what a POU
is about to read. `F_FileSystem*` is that seed/inspect surface; there is no
vendor equivalent, because on a real system ST cannot reach the filesystem,
which is most of why file-touching POUs go untested.

**The handshake.** The invocation carrying the *rising edge* of `bExecute`
starts the command and reports `bBusy` TRUE having done nothing else; the *next*
invocation performs it, publishes the outputs and clears `bBusy`. One cycle of
latency, not zero — a real POU driving one of these is a state machine, and
"trigger, then advance once I have seen `bBusy`" is a common shape that an
instantly-completing block would leave waiting forever in a test while it worked
on a PLC. Longer would be arbitrary: there is no ADS round trip here, so any
particular number of cycles would be a fiction a test then had to encode.

**Not modelled:** text-vs-binary line-ending translation (a file holds exactly
the bytes written to it), and `ePath`'s TwinCAT system directories (the virtual
filesystem has one flat namespace). `nErrId` values are xStunit's own — the
vendor does not publish the command-specific half — so only *zero vs non-zero*
is a safe thing for a suite to assert.

**The named constants are still missing.** `FOPEN_MODEREAD`, `PATH_GENERIC`,
`SEEK_SET`, `ADSLOG_MSGTYPE_ERROR` and `DEFAULT_ADS_TIMEOUT` live in
compiled-only Tc2_System GVLs and there is no plugin surface for a named value
yet, so the fixtures pass raw numbers where real source would not.

## Why these live in a plugin rather than the interpreter

Vendor-library behavior stays out of the shipped assemblies. The interpreter
owns the two contracts (`IXstunitNativeFunction`,
`IXstunitNativeFunctionBlock`) and nothing about any particular library; a
downstream project that needs a different version, a different vendor, or its
own in-house library supplies it the same way this project does.
