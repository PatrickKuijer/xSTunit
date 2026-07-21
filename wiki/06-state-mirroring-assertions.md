# State-mirroring assertions: AssertConverges / AssertConvergesAndLatches

Status: **pending** — ticket `TcXunit-w5x.15.9` still open (ready, not
started as of 2026-07-21). Nothing below exists in the interpreter yet;
this page documents the *design* from `TcXunit-w5x.15`'s spec so you know
the intended shape while writing fixtures that will need it. Don't ship a
fixture that depends on this working until the ticket closes — for now,
hand-roll the polling loop with [StepCycles](02-step-cycles.md) if you
need this today.

## Intended shape

```
AssertConverges(master, proxy, fieldNames, maxCycles);
```

- Owns its own stepping loop internally: `master.StepCycles(1);
  proxy.StepCycles(1);` per iteration, fixed master-then-proxy order, up
  to `maxCycles`.
- Compares the named fields (`fieldNames`, string-keyed) between `master`
  and `proxy` after each iteration.
- On convergence within `maxCycles`: passes.
- On non-convergence: throws with a per-field diff of master vs. proxy
  final values — not a bare pass/fail.

```
AssertConvergesAndLatches(master, proxy, fieldNames, maxCycles);
```

- Separate first-class method (not a caller-composed wrapper) for the
  "flips exactly once and stays latched" barrier pattern — e.g. a
  registration/sync flag that should go `FALSE -> TRUE` exactly once and
  never flip back.

## Known scope limits (by design, not a gap to work around)

- Two-instance pairs only (`master`/`proxy`). Groups of three or more, or
  non-fixed stepping orders, are explicitly out of scope until a concrete
  test needs them — don't expect this to generalize to N instances.
- No whole-struct diff and no caller-supplied comparison predicate — it's
  string-keyed field lookup through existing `FbInstance` field access.

## Hand-rolled equivalent until this lands

```
FOR i := 1 TO maxCycles DO
    master.StepCycles(1);
    proxy.StepCycles(1);
    IF master.SomeField = proxy.SomeField THEN
        converged := TRUE;
        EXIT;
    END_IF
END_FOR

AssertTrue(Condition := converged, Message := 'master/proxy did not converge');
```
