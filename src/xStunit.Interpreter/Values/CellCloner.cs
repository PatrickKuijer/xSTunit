using System;

namespace xStunit.Interpreter
{
    // Deep-clones STRUCT/ARRAY payloads so a copy is a copy: both hold their
    // contents in mutable CLR reference types, so reusing the reference would
    // alias the source and make later writes visible through both. Everything
    // else (numerics, bool, string) is boxed value-type or CLR-immutable, and
    // the reference is safe to hand back as-is.
    //
    // An FbInstance is a reference unless its declared type says otherwise: an
    // interface variable holding one points at it, an FB-typed STRUCT member
    // owns it. cloneFb is asked with the member's declared type and returns
    // either a copy or the instance itself; a null cloneFb shares every
    // instance.
    internal static class CellCloner
    {
        public static object CloneValue(object value, Func<FbInstance, string, FbInstance> cloneFb = null) =>
            CloneValue(value, null, cloneFb);

        public static object CloneValue(object value, string declaredTypeName, Func<FbInstance, string, FbInstance> cloneFb)
        {
            if (value is StructInstance structInstance)
            {
                // A UNION has one storage, so copying it means copying those
                // bytes: rebuilding it field by field would hand the copy
                // independent members and lose the overlay.
                if (structInstance.Overlay != null)
                    return structInstance.Overlay.CloneInstance();

                var clone = new StructInstance(structInstance.TypeName);
                foreach (var field in structInstance.Fields)
                {
                    structInstance.FieldTypeNames.TryGetValue(field.Key, out var memberTypeName);
                    if (memberTypeName != null)
                        clone.FieldTypeNames[field.Key] = memberTypeName;

                    if (AddressTypeInfo.IsReferenceType(memberTypeName))
                    {
                        clone.Fields[field.Key] = field.Value;
                        continue;
                    }

                    // The clone carries the source's declared type and
                    // capacity, or a copied-in STRUCT would stop truncating its
                    // own string fields while the value it was copied from
                    // still does.
                    clone.Fields[field.Key] = new Cell
                    {
                        Value = CloneValue(field.Value.Value, memberTypeName ?? field.Value.DeclaredTypeName, cloneFb),
                        DeclaredTypeName = field.Value.DeclaredTypeName,
                        StringCapacity = field.Value.StringCapacity,
                    };
                }
                return clone;
            }

            if (value is ArrayValue arrayValue)
            {
                var elements = new object[arrayValue.Elements.Length];
                for (var i = 0; i < elements.Length; i++)
                    elements[i] = CloneValue(arrayValue.Elements[i], arrayValue.ElementTypeName, cloneFb);
                return new ArrayValue(
                    arrayValue.Dimensions,
                    arrayValue.ElementTypeName,
                    elements,
                    arrayValue.ElementStringCapacity);
            }

            if (value is FbInstance fbInstance && cloneFb != null)
                return cloneFb(fbInstance, declaredTypeName);

            return value;
        }
    }
}
