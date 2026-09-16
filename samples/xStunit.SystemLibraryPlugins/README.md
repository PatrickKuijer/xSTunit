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
| `ADSREAD` / `ADSWRITE` / `ADSRDWRT` / `ADSRDSTATE` | blocks | against a loopback device the suite scripts, never a real router |
| `F_AdsServerClear` / `F_AdsServerSetText` / `F_AdsServerGetText` / `F_AdsServerFail` / `F_AdsServerTimeout` / `F_AdsServerSetState` / `F_AdsServerWriteCount` | functions | **xStunit's own** — script and inspect that device |
| `FOPEN_MODE*` / `E_OpenPath` / `E_SeekOrigin` / `ADSLOG_MSGTYPE_*` / `DEFAULT_ADS_TIMEOUT` | constants | the library's GVL and ENUM values, so source names them as it really does |

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

## The ADS family

Backed by a loopback device the suite scripts. A test against a real router
would need one to exist, would depend on what some other device happened to
hold, and could not provoke the cases production ADS code actually gets wrong:
a timeout, an error on one variable while others answer, a write issued every
cycle instead of once. Those are the paths error handling never exercises, so
they are the ones worth being scriptable.

`F_AdsServerTimeout` is measured on the **simulated** clock: the request stays
`BUSY` until the suite has advanced time past `TMOUT`, then reports ADS
`16#745`. A one-second timeout therefore costs no wall-clock time and lands at
the same point every run. Unlike the file family's, these error codes are the
vendor's real ones — ADS return codes are published.

`ADSRDSTATE` names no index group or offset, so its scripted failure and
timeout are addressed at group 0, offset 0.

## The named constants

A library's constants live in its GVLs and ENUMs, which ship compiled just as
its POUs do. Supplying the blocks without them would only half-solve the
problem — real source never writes `nMode := 33`, it writes
`FOPEN_MODEREAD OR FOPEN_MODETEXT`, and an unresolved identifier skips the POU
just as an unresolved call does. `SystemLibraryConstants` publishes them through
`IXstunitNativeConstants`; an enum member resolves both bare and qualified
(`PATH_GENERIC` and `E_OpenPath.PATH_GENERIC`), as TwinCAT allows.

How sure the numbers are: `FOPEN_MODEBINARY` (16) and `FOPEN_MODETEXT` (32) are
documented and the other four continue the same bit sequence; `E_SeekOrigin` is
assumed to follow C's `SEEK_SET`/`SEEK_CUR`/`SEEK_END` = 0/1/2. Only the
relative behaviour is load-bearing here — the virtual filesystem reads the mode
bits — so a wrong absolute value would matter only to source that hard-codes
one.

## Why these live in a plugin rather than the interpreter

Vendor-library behavior stays out of the shipped assemblies. The interpreter
owns the two contracts (`IXstunitNativeFunction`,
`IXstunitNativeFunctionBlock`) and nothing about any particular library; a
downstream project that needs a different version, a different vendor, or its
own in-house library supplies it the same way this project does.
