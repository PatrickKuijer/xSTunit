using System;

namespace xStunit.Interpreter
{
    // The value a declaration starts at when its declared type alone settles
    // it: the "0/false/empty" rule, and an ARRAY with every element already at
    // its own default. Both sites that build declarations come through here -
    // the Engine, which resolves a bound against a live Frame, and
    // StructBoundaryBuilder, which runs before any FbInstance exists and
    // resolves one out of TypeRegistry's already-parsed GVL declarations - so
    // the two differ only in the resolveBound delegate they pass and in what an
    // element's own default is.
    internal static class DeclaredDefault
    {
        // Pass an alias-resolved type name; an ALIAS DUT names another type and
        // it is that type's rule that applies. A name matching no rule at all
        // lands on the integer 0, which is what an unrecognised declaration has
        // always defaulted to.
        public static object ForElementaryType(string resolvedTypeName)
        {
            if (IecElementaryDefault.TryGetDefault(resolvedTypeName, out var elementaryDefault))
                return elementaryDefault;

            return IecNumericType.TryGetDefault(resolvedTypeName, out var numericDefault) ? numericDefault : 0;
        }

        // elementDefault is handed a synthetic element declaration rather than
        // the element type name, so a caller whose defaults need a whole VarDecl
        // - the Engine's do, for the initializer text - can reuse its ordinary
        // per-declaration path unchanged.
        public static ArrayValue NewArray(
            TypeRegistry registry,
            string arrayTypeName,
            Func<string, int> resolveBound,
            Func<VarDecl, object> elementDefault)
        {
            var (dimensions, elementTypeName) = ArrayTypeInfo.Parse(arrayTypeName, resolveBound);
            var elementDecl = new VarDecl(null, elementTypeName, null, VarSection.Local);

            var elements = new object[ArrayTypeInfo.ElementCount(dimensions)];
            for (var i = 0; i < elements.Length; i++)
                elements[i] = elementDefault(elementDecl);

            return new ArrayValue(
                dimensions,
                elementTypeName,
                elements,
                StringTypeInfo.ResolveCapacity(registry.ResolveAlias(elementTypeName), resolveBound));
        }
    }
}
