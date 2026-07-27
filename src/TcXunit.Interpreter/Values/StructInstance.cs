using System.Collections.Generic;

namespace TcXunit.Interpreter
{
    // STRUCT value: a named-field container of Cells, same shape as
    // FbInstance.Fields but without a method table (T7's struct/array Cell
    // shape decision - TcXunit-w5x.15.6).
    public sealed class StructInstance
    {
        public string TypeName { get; }
        public Dictionary<string, Cell> Fields { get; } = new Dictionary<string, Cell>();

        public StructInstance(string typeName)
        {
            TypeName = typeName;
        }
    }
}
