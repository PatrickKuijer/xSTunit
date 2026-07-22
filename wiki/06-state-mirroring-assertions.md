# State-mirroring assertions: AssertConverges / AssertConvergesAndLatches

Status: **built**. Source: `Engine.AssertConverges`/`AssertConvergesAndLatches`
in `src/TcXunit.Interpreter/Engine.cs`, dispatched as a native call
(same boundary as `StepCycles`) when a suite/fixture calls either name
with 4 positional args. Landed `TcXunit-w5x.15.9`.

## Shape

```
AssertConverges(master, proxy, fieldNames, maxCycles);
```

- Owns its own stepping loop internally: `master.StepCycles(1);
  proxy.StepCycles(1);` per iteration, fixed master-then-proxy order, up
  to `maxCycles`.
- Compares the named fields (`fieldNames`, string-keyed) between `master`
  and `proxy` after each iteration.
- On convergence within `maxCycles`: passes (returns normally).
- On non-convergence: throws `ConvergenceAssertionException` with a
  per-field diff of master vs. proxy final values — not a bare pass/fail,
  and not a queued `TcUnit`-style `TEST_FINISHED()` failure. This is an
  immediate exception, so it aborts the test method it's called from
  rather than letting later asserts in the same `TEST()` bracket run.

```
AssertConvergesAndLatches(master, proxy, fieldNames, maxCycles);
```

- Separate first-class method (not a caller-composed wrapper) for the
  "flips exactly once and stays latched" barrier pattern — e.g. a
  registration/sync flag that should go `FALSE -> TRUE` exactly once and
  never flip back.
- Once fields converge, they must stay converged every remaining cycle up
  to `maxCycles`; diverging again after latching throws
  `ConvergenceAssertionException` with the cycle it latched at and the
  cycle it diverged again at. Never converging within `maxCycles` also
  throws.

## Known scope limits (by design, not a gap to work around)

- Two-instance pairs only (`master`/`proxy`). Groups of three or more, or
  non-fixed stepping orders, are explicitly out of scope until a concrete
  test needs them — don't expect this to generalize to N instances.
- No whole-struct diff and no caller-supplied comparison predicate — it's
  string-keyed field lookup through existing `FbInstance` field access.
- `master`/`proxy`/`fieldNames`/`maxCycles` are positional-only (no named
  args) — the dispatcher matches on `positionalArgs.Count == 4`.
