# In-process transport loopback + fault injection

Status: **built**. Tickets: `TcXunit-w5x.15.5` (Loopback/Transmit),
`TcXunit-w5x.15.8` (fault vocabulary). Source:
`src/xStunit.Interpreter/LoopbackHost.cs`. Tests:
`tests/xStunit.Interpreter.Tests/LoopbackFaultTests.cs`,
`LoopbackTests.cs`.

## Shape

One `Loopback` instance = one fixed link between exactly two fields (any
two fields on any two FB instances — no naming/attribute convention).

```
VAR
    fbLink : Loopback;
    txFb   : FB_Payload;
    rxFb   : FB_Payload;
END_VAR
```

```
fbLink.Transmit(source := txFb.Buffer, sink := rxFb.Buffer);
```

- `Transmit` performs a **discrete copy** (`sink.Value = source.Value`),
  not live aliasing — it only runs the moment you call it, never
  implicitly tied to `StepCycles`.
- `source` / `sink` are passed as named args pointing at ordinary fields —
  the link doesn't care what FB types `txFb`/`rxFb` are.

## Observable contract

Two fields published on the `Loopback` instance itself, same pattern as a
timer's `Q`/`ET`:

- `LinkUp : BOOL` — `FALSE` only under `Drop()`. Defaults `TRUE`.
- `LastUpdateTime` — the clock snapshot taken on every **successful**
  `Transmit()` copy. Defaults `0`. Use `Engine.Clock.Total - fbLink.LastUpdateTime`
  against your own threshold for a watchdog/staleness test.

## Fault vocabulary

One active fault mode at a time — setting a new one clears whatever was
pending before (verified by `SettingNewFault_ClearsPreviousFaultState`).

| Call | Effect |
|---|---|
| `fbLink.Drop()` | `LinkUp := FALSE`; `Transmit()` becomes a no-op (sink untouched, `LastUpdateTime` frozen). Hard drop. |
| `fbLink.Restore()` | Clears any fault, `LinkUp := TRUE`. |
| `fbLink.Freeze()` | `LinkUp` stays `TRUE`, but sink stops updating on `Transmit()`. This *is* "stale but still connected" — no separate fault type for that. |
| `fbLink.SetDelay(n)` | Next transmitted values enqueue into a FIFO; sink receives the **oldest** queued value once the queue reaches depth `n`. Rides your own `Transmit()` call sequence — no wall-clock scheduler. |
| `fbLink.Duplicate()` | One-shot: next `Transmit()` re-sends the last successfully transmitted value, ignoring `source`'s current value. |
| `fbLink.Corrupt(value)` | One-shot: next `Transmit()` sets sink to `value` instead of `source.Value`. |

## Watchdog / staleness pattern

`Freeze()` and `Drop()` both leave the sink un-updated, but only `Drop()`
reports the link itself as down. A watchdog assertion should read
`LinkUp` to distinguish "stale but connected" (`Freeze`) from "hard drop"
(`Drop`) — don't reach into `Loopback`'s internal FIFO/one-shot state,
`LinkUp`/`LastUpdateTime` are the whole observable contract.

## Rules

- No implicit wiring — every copy needs an explicit `Transmit()` call in
  your test method's ST body.
- `Transmit` for `STRUCT` payloads is **pending**
  ([07-struct-array-and-builders.md](07-struct-array-and-builders.md),
  `TcXunit-w5x.15.10`) — today's `Transmit` is a scalar `sink.Value =
  source.Value` copy. Struct support will switch to a per-field clone to
  preserve the copy-not-alias contract; don't rely on struct-field
  transmission until that ticket closes.
