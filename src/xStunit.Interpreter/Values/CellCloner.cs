namespace xStunit.Interpreter
{
    // Deep-clones STRUCT/ARRAY payload values so Transmit copies rather than
    // aliases (TcXunit-w5x.15.10 / T7's design: "Dictionary<string, Cell> is
    // a mutable reference type... STRUCT payload copy needs an explicit
    // per-field clone"). Everything else (numerics, bool, string) is a value
    // type or CLR-immutable, so the reference itself is safe to reuse as-is.
    internal static class CellCloner
    {
        public static object CloneValue(object value)
        {
            if (value is StructInstance structInstance)
            {
                var clone = new StructInstance(structInstance.TypeName);
                foreach (var field in structInstance.Fields)
                    clone.Fields[field.Key] = new Cell { Value = CloneValue(field.Value.Value) };
                return clone;
            }

            if (value is ArrayValue arrayValue)
            {
                var elements = new object[arrayValue.Elements.Length];
                for (var i = 0; i < elements.Length; i++)
                    elements[i] = CloneValue(arrayValue.Elements[i]);
                return new ArrayValue(arrayValue.Dimensions, arrayValue.ElementTypeName, elements);
            }

            return value;
        }
    }
}
