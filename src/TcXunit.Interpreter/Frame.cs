using System.Collections.Generic;

namespace TcXunit.Interpreter
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

        // Declared IEC type text for each entry in Locals, indexed by name -
        // populated once by BindParams and never touched afterward, same
        // rationale as FbInstance.FieldTypeNames: a REF= binding of a
        // method-local REFERENCE TO/POINTER TO replaces its Locals[name]
        // Cell wholesale with the target's own Cell (Engine.ExecuteStatement's
        // RefAssignStmt case), so Cell.DeclaredTypeName after that reflects
        // the target, not the local's own declaration. __ISVALIDREF
        // (TcXunit-6lh) needs this table instead.
        public Dictionary<string, string> LocalTypeNames { get; } = new Dictionary<string, string>();

        public Frame(FbInstance instance, string declaringTypeName)
        {
            Instance = instance;
            DeclaringTypeName = declaringTypeName;
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
