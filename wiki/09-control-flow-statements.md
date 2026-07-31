# FOR / WHILE / REPEAT / CASE / EXIT

Status: **built**. Source: `Stmt.cs` (`ForStmt`/`WhileStmt`/`RepeatStmt`/
`CaseStmt`/`CaseArm`/`CaseLabel`/`ExitStmt`), `Parser.cs` (`ParseFor`/
`ParseWhile`/`ParseRepeat`/`ParseCase`), `Engine.cs` (`ExecuteFor`/
`ExecuteWhile`/`ExecuteRepeat`/`ExecuteCase`, `LoopExitSignal`),
`ControlFlowStatementTests.cs` in `tests/xStunit.Interpreter.Tests`.
Landed as `TcXunit-mym.3`.

Also added as a prerequisite: unary minus (`-x`) — needed for negative FOR
steps and negative literals in CASE/ELSE bodies. Previously the interpreter
had no unary minus at all.

## FOR

```
FOR i := 1 TO 5 DO
    sum := sum + i;
END_FOR

FOR i := 10 TO 0 BY -2 DO
    (* 10, 8, 6, 4, 2, 0 *)
END_FOR
```

- `BY step` is optional, defaults to `1`.
- `lo > hi` with the default `+1` step runs zero iterations (no infinite
  loop, no throw). Same applies in reverse for a negative step whose `lo <
  hi`.
- `step = 0` throws `InvalidOperationException`.
- The loop variable is an ordinary identifier resolved through the same
  `SetVariable`/`Frame` path as any assignment — no special FOR-scoped
  variable. It stays readable after the loop ends, holding whatever value
  it was last set to on the final executed iteration (not one past the
  boundary).

## WHILE

```
WHILE cond DO
    ...
END_WHILE
```

Condition checked before every iteration, including the first — zero
iterations if false to start.

## REPEAT

```
REPEAT
    ...
UNTIL cond
END_REPEAT
```

Body always runs at least once; stops once `cond` becomes true.

## CASE

```
CASE selector OF
1: result := 10;
2, 3: result := 20;
4..6: result := 30;
ELSE
result := -1;
END_CASE
```

- Each arm's label list is comma-separated; a label is a single constant
  expression or a `lo..hi` range.
- Selector and labels match as integers (`BOOL` labels/selectors coerce
  `TRUE`/`FALSE` to `1`/`0`).
- First matching arm wins — no C-style fallthrough between arms.
- No match and no `ELSE`: executes nothing, doesn't throw.

## EXIT

```
WHILE TRUE DO
    IF count = 3 THEN
        EXIT;
    END_IF
    count := count + 1;
END_WHILE
```

- Stops the nearest enclosing `FOR`/`WHILE`/`REPEAT` immediately; execution
  continues with the statement after that loop.
- Reaches through any `IF`/`CASE` it's lexically nested inside on its way
  out — only ever caught by a loop, never by a branch construct.
- In nested loops, exits only the innermost one.
- Implemented as a dedicated `LoopExitSignal` exception, caught by a
  `try`/`catch` wrapping each loop's iteration — deliberately its own
  signal type, distinct from any future `RETURN`-unwind mechanism, so the
  two don't interfere with each other's catch site.

## Out of scope

- `CONTINUE` — not standard IEC 61131-3 ST, skipped unless a real POU needs
  it.
- `FOR` over a non-integer (e.g. enum-typed) loop variable.
