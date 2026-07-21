using System.Collections.Generic;

namespace TcXunit.Interpreter
{
    // TYPE ... STRUCT ... END_STRUCT END_TYPE declaration - PouAst's
    // equivalent for struct types (TcXunit-w5x.15.6 / T7's design).
    public sealed class StructAst
    {
        public string Name { get; }
        public IReadOnlyList<VarDecl> Fields { get; }

        public StructAst(string name, IReadOnlyList<VarDecl> fields)
        {
            Name = name;
            Fields = fields;
        }
    }
}
