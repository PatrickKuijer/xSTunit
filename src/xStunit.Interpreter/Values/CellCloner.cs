namespace xStunit.Interpreter
{
    // Deep-clones STRUCT/ARRAY payloads so a copy is a copy: both hold their
    // contents in mutable CLR reference types, so reusing the reference would
    // alias the source and make later writes visible through both. Everything
    // else (numerics, bool, string) is boxed value-type or CLR-immutable, and
    // the reference is safe to hand back as-is.
    internal static class CellCloner
    {
        public static object CloneValue(object value)
        {
            if (value is StructInstance structInstance)
            {
                var clone = new StructInstance(structInstance.TypeName);
                // The clone carries the source's declared type and capacity, or
                // a copied-in STRUCT would stop truncating its own string
                // fields while the value it was copied from still does.
                foreach (var field in structInstance.Fields)
                    clone.Fields[field.Key] = new Cell
                    {
                        Value = CloneValue(field.Value.Value),
                        DeclaredTypeName = field.Value.DeclaredTypeName,
                        StringCapacity = field.Value.StringCapacity,
                    };
                return clone;
            }

            if (value is ArrayValue arrayValue)
            {
                var elements = new object[arrayValue.Elements.Length];
                for (var i = 0; i < elements.Length; i++)
                    elements[i] = CloneValue(arrayValue.Elements[i]);
                return new ArrayValue(
                    arrayValue.Dimensions,
                    arrayValue.ElementTypeName,
                    elements,
                    arrayValue.ElementStringCapacity);
            }

            return value;
        }
    }
}
