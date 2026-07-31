using System.Collections.Generic;

namespace xStunit.Interpreter
{
    // Execution context for one method/body invocation: the instance it runs
    // against (for field access and virtual dispatch), its own params+locals,
    // and the type that owns the executing method body (for SUPER^ resolution
    // - a non-virtual call must start its search one level above *this*, not
    // above Instance.ActualTypeName).
    public sealed class Frame
    {
        public FbInstance Instance { get; }
        public Dictionary<string, Cell> Locals { get; } = new Dictionary<string, Cell>();
        public string DeclaringTypeName { get; }

        // METHOD this frame is executing, for failure attribution
        // (TcXunit-p3t.1) - null when the frame runs a POU's own top-level
        // body (suite body, bare-invoked FB body, StepCycles cycle), which
        // has no method to name. Diagnostics only: nothing in name
        // resolution or dispatch reads it.
        public string MethodName { get; }

        // Declared IEC type text for each entry in Locals, indexed by name -
        // populated once by BindParams and never touched afterward, same
        // rationale as FbInstance.FieldTypeNames: a REF= binding of a
        // method-local REFERENCE TO/POINTER TO replaces its Locals[name]
        // Cell wholesale with the target's own Cell (Engine.ExecuteStatement's
        // RefAssignStmt case), so Cell.DeclaredTypeName after that reflects
        // the target, not the local's own declaration. __ISVALIDREF
        // (TcXunit-6lh) needs this table instead.
        public Dictionary<string, string> LocalTypeNames { get; } = new Dictionary<string, string>();

        // Where this frame's body starts in its .TcPOU file - copied straight
        // from the MethodAst/PouAst the body came from (TcXunit-p3t.3), so
        // CurrentFileLine below can turn an in-body line into a file line
        // (TcXunit-p3t.4). Defaults to 1 for the same reason the AST property
        // does: with no file to speak of, the body IS the file.
        public int BodyStartLine { get; }

        // In-body line of the statement this frame is currently executing,
        // stamped by Engine.ExecuteStatement before each statement runs
        // (TcXunit-p3t.4). A plain field write per statement - the alternative,
        // reconstructing a location after the fact, is impossible here because
        // the interpreter's only call stack is CLR recursion that has already
        // unwound by the time a fault reaches the suite boundary.
        //
        // 0 means unknown, matching Stmt.Line's own convention: a hand-built
        // AST that never went through the parser has no line to offer, and no
        // line at all is reported rather than a fabricated one.
        public int CurrentLine { get; set; }

        // 1-based line in the .TcPOU file, or PlcSourceLocationException.
        // UnknownLine when the executing statement has no known line. Both
        // operands are 1-based, hence the -1: naive addition double-counts the
        // body's first line (see Token.Line).
        public int CurrentFileLine =>
            CurrentLine == PlcSourceLocationException.UnknownLine
                ? PlcSourceLocationException.UnknownLine
                : BodyStartLine + CurrentLine - 1;

        public Frame(FbInstance instance, string declaringTypeName, string methodName = null, int bodyStartLine = 1)
        {
            Instance = instance;
            DeclaringTypeName = declaringTypeName;
            MethodName = methodName;
            BodyStartLine = bodyStartLine;
        }

        public Cell ResolveCell(string identifier)
        {
            if (Locals.TryGetValue(identifier, out var local))
                return local;

            if (Instance != null && Instance.Fields.TryGetValue(identifier, out var field))
                return field;

            return null;
        }
    }
}
