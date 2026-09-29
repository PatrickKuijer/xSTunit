using System;
using System.Collections.Generic;

namespace xStunit.Interpreter
{
    // Execution context for one method/body invocation: the instance it runs
    // against (for field access and virtual dispatch), its own params+locals,
    // and the type that owns the executing method body. That last one is what
    // SUPER^ resolves against - a non-virtual call must start its search one
    // level above the *declaring* type, not above Instance.ActualTypeName.
    public sealed class Frame
    {
        public FbInstance Instance { get; }

        // IEC 61131-3 identifiers are case-insensitive, so a name has to
        // resolve however the body spells it. The comparer lives on the
        // dictionary rather than in a normalised key so the key keeps the
        // declared spelling, which is what diagnostics echo back. The same
        // holds for every name-keyed table in Frame, FbInstance and
        // StructInstance.
        public Dictionary<string, Cell> Locals { get; } = new Dictionary<string, Cell>(IecIdentifier.Comparer);
        public string DeclaringTypeName { get; }

        // Null when the frame runs a POU's own top-level body (suite body,
        // bare-invoked FB body, StepCycles cycle), which has no method to
        // name. Diagnostics only: nothing in name resolution or dispatch
        // reads it.
        public string MethodName { get; }

        // Declared IEC type text per local, populated once by BindParams and
        // never touched afterward. Same rationale as
        // FbInstance.FieldTypeNames: a REF= binding replaces the Locals entry
        // wholesale with the target's own Cell, so Cell.DeclaredTypeName
        // afterwards describes the target, not the local's declaration.
        public Dictionary<string, string> LocalTypeNames { get; } = new Dictionary<string, string>(IecIdentifier.Comparer);

        // Where this frame's body starts in its source file, copied from the
        // MethodAst/PouAst it came from, so CurrentFileLine can turn an
        // in-body line into a file line. Defaults to 1 for the same reason the
        // AST property does: with no file to speak of, the body IS the file.
        public int BodyStartLine { get; }

        // In-body line of the statement currently executing, stamped by
        // Engine.ExecuteStatement before each statement runs. Recorded eagerly
        // because it cannot be reconstructed afterwards: the interpreter's
        // only call stack is CLR recursion, already unwound by the time a
        // fault reaches the suite boundary.
        //
        // 0 means unknown, matching Stmt.Line's convention - a hand-built AST
        // that never went through the parser has no line to offer, and none is
        // reported rather than a fabricated one.
        public int CurrentLine { get; set; }

        // 1-based line in the source file, or
        // PlcSourceLocationException.UnknownLine when the executing statement
        // has no known line. Both operands are 1-based, hence the -1: naive
        // addition double-counts the body's first line.
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

        // The instance-owned table this call's VAR_INST locals came from; null
        // when the frame has none. RebindLocal writes through to it.
        private Dictionary<string, Cell> _methodInstanceCells;

        public void BindMethodInstanceCells(IReadOnlyList<VarDecl> decls, Dictionary<string, Cell> cells)
        {
            _methodInstanceCells = cells;
            foreach (var decl in decls)
            {
                Locals[decl.Name] = cells[decl.Name];
                LocalTypeNames[decl.Name] = decl.TypeName;
            }
        }

        // Replaces a local's Cell wholesale, as REF= does. A VAR_INST local is
        // replaced in the instance's table too, or the binding would die with
        // this call and the next call would find the old Cell.
        public void RebindLocal(string name, Cell cell)
        {
            Locals[name] = cell;
            if (_methodInstanceCells != null && _methodInstanceCells.ContainsKey(name))
                _methodInstanceCells[name] = cell;
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
