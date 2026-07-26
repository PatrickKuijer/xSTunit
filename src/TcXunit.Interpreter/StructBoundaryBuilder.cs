using System;
using System.Collections.Generic;
using System.Linq;

namespace TcXunit.Interpreter
{
    // Pure-introspection STRUCT test-data builder (TcXunit-w5x.15.10 / T7's
    // design): walks a STRUCT type's declared fields via TypeRegistry, no
    // per-struct registration. Every field defaults to an in-range value;
    // named overrides push just those fields to a Boundary (single-field
    // sweep semantics - a test wanting several fields at their boundary
    // together asks for it explicitly via multiple overrides).
    public sealed class StructBoundaryBuilder
    {
        private readonly TypeRegistry _registry;

        public StructBoundaryBuilder(TypeRegistry registry)
        {
            _registry = registry;
        }

        public StructInstance Build(string structTypeName, params (string Field, Boundary Boundary)[] overrides)
        {
            var structAst = _registry.GetStruct(_registry.ResolveAlias(structTypeName))
                ?? throw new InvalidOperationException($"Unknown struct type '{structTypeName}'");

            var overrideMap = new Dictionary<string, Boundary>();
            foreach (var (field, boundary) in overrides)
            {
                if (structAst.Fields.All(f => f.Name != field))
                    throw new InvalidOperationException($"Unknown field '{field}' on struct '{structTypeName}'");
                overrideMap[field] = boundary;
            }

            var instance = new StructInstance(structAst.Name);
            foreach (var field in structAst.Fields)
            {
                instance.Fields[field.Name] = new Cell
                {
                    Value = overrideMap.TryGetValue(field.Name, out var boundary)
                        ? BoundaryValue(field, boundary)
                        : InRangeDefault(field)
                };
            }

            return instance;
        }

        // Every field's normal (non-boundary) value: nested structs recurse
        // with no overrides of their own, arrays fill every element at its
        // element type's in-range default - matching Engine.BuildStructDefault/
        // BuildArrayDefault's "0/false/empty" defaults without needing an
        // Engine instance (the builder runs before any FbInstance exists).
        private object InRangeDefault(VarDecl field)
        {
            // Resolve through any ALIAS DUT (TcXunit-6hg) once up front, same
            // rationale as Engine.DefaultValue.
            var typeName = _registry.ResolveAlias(field.TypeName);

            if (ArrayTypeInfo.IsArrayType(typeName))
                return BuildArrayInRange(typeName);

            var nestedStruct = _registry.GetStruct(typeName);
            if (nestedStruct != null)
                return Build(typeName);

            if (StringTypeInfo.IsStringType(typeName))
                return "";

            if (typeName == "BOOL")
                return false;

            if (IecNumericType.TryGetDefault(typeName, out var numericDefault))
                return numericDefault;

            return 0;
        }

        private object BoundaryValue(VarDecl field, Boundary boundary)
        {
            var typeName = _registry.ResolveAlias(field.TypeName);

            if (StringTypeInfo.IsStringType(typeName))
            {
                var length = StringTypeInfo.ParseLength(typeName);
                return boundary == Boundary.Min ? "" : new string('X', length);
            }

            if (IecNumericType.TryGetBounds(typeName, out var bounds))
                return boundary == Boundary.Min ? bounds.Min : bounds.Max;

            throw new NotSupportedException(
                $"Field '{field.Name}' of type '{field.TypeName}' has no boundary value - " +
                "ARRAY bounds are fixed at declaration and STRUCT fields have no scalar boundary; " +
                "target a numeric/STRING field (e.g. a paired count field) instead.");
        }

        private ArrayValue BuildArrayInRange(string arrayTypeName)
        {
            var (dimensions, elementTypeName) = ArrayTypeInfo.Parse(arrayTypeName);
            var count = dimensions.Aggregate(1, (acc, d) => acc * (d.Hi - d.Lo + 1));
            var elementDecl = new VarDecl(null, elementTypeName, null, VarSection.Local);

            var elements = new object[count];
            for (var i = 0; i < count; i++)
                elements[i] = InRangeDefault(elementDecl);

            return new ArrayValue(dimensions, elementTypeName, elements);
        }
    }
}
