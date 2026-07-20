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
