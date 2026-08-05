using System;
using System.Collections.Generic;
using System.Linq;

namespace xStunit.Interpreter
{
    // Builds STRUCT test data by introspecting the type's declared fields via
    // TypeRegistry, so no struct ever needs registering here. Every field
    // gets an in-range value; a named override pushes just that field to a
    // Boundary, leaving the rest in range - single-field sweep semantics, so
    // a test wanting several fields at their boundary at once must say so
    // with several overrides.
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
                        : InRangeDefault(field),
                    DeclaredTypeName = field.TypeName,
                    StringCapacity = StringTypeInfo.ResolveCapacity(
                        _registry.ResolveAlias(field.TypeName), ResolveArrayBound),
                };
            }

            return instance;
        }

        // The "0/false/empty" rule and the every-element-defaulted ARRAY come
        // from DeclaredDefault, the same module the Engine's own defaults go
        // through; this side of the seam resolves a bound out of TypeRegistry
        // rather than a live Frame, because the builder runs before any
        // FbInstance exists. Nested structs recurse with no overrides of their
        // own.
        private object InRangeDefault(VarDecl field)
        {
            // Resolved once up front so every branch below sees the underlying
            // type, not an ALIAS DUT name - same as Engine.DefaultValue.
            var typeName = _registry.ResolveAlias(field.TypeName);

            if (ArrayTypeInfo.IsArrayType(typeName))
                return DeclaredDefault.NewArray(_registry, typeName, ResolveArrayBound, InRangeDefault);

            if (_registry.GetStruct(typeName) != null)
                return Build(typeName);

            return DeclaredDefault.ForElementaryType(typeName);
        }

        private object BoundaryValue(VarDecl field, Boundary boundary)
        {
            var typeName = _registry.ResolveAlias(field.TypeName);

            if (StringTypeInfo.IsStringType(typeName))
            {
                // A STRING size may be a non-literal constant expression (e.g.
                // a GVL-qualified constant), which is why this goes through
                // the same resolver ARRAY bounds use rather than parsing an
                // integer out of the type name.
                var length = StringTypeInfo.ParseLength(typeName, ResolveArrayBound);
                return boundary == Boundary.Min ? "" : new string('X', length);
            }

            if (IecNumericType.TryGetBounds(typeName, out var bounds))
                return boundary == Boundary.Min ? bounds.Min : bounds.Max;

            throw new NotSupportedException(
                $"Field '{field.Name}' of type '{field.TypeName}' has no boundary value - " +
                "ARRAY bounds are fixed at declaration and STRUCT fields have no scalar boundary; " +
                "target a numeric/STRING field (e.g. a paired count field) instead.");
        }

        // Resolves a non-literal ARRAY bound (e.g. a GVL-qualified constant
        // like "cRemoteClientConfig.MAX_REMOTE_ITEMS") without a running
        // Engine or Frame. Possible because a GVL constant is itself a
        // constant expression - a literal, arithmetic on literals, or another
        // GVL constant - so recursing through TypeRegistry's already-parsed
        // GVL declarations reaches an integer without Engine.Evaluate.
        private int ResolveArrayBound(string boundText) =>
            EvaluateConstExpr(Parser.ParseExpression(boundText));

        private int EvaluateConstExpr(Expr expr)
        {
            switch (expr)
            {
                case IntLiteralExpr intLit:
                    return intLit.Value;

                case UnaryExpr unary when unary.Op == "-":
                    return -EvaluateConstExpr(unary.Operand);

                case BinaryExpr binary:
                    var left = EvaluateConstExpr(binary.Left);
                    var right = EvaluateConstExpr(binary.Right);
                    switch (binary.Op)
                    {
                        case "+": return left + right;
                        case "-": return left - right;
                        case "*": return left * right;
                        case "/": return left / right;
                    }
                    break;

                case FieldAccessExpr fieldAccess when fieldAccess.Receiver is IdentifierExpr gvlIdent:
                    var gvlDecls = _registry.GetGvlDecls(gvlIdent.Name)
                        ?? throw new InvalidOperationException($"Unknown GVL '{gvlIdent.Name}' referenced in array bound");
                    var constDecl = gvlDecls.FirstOrDefault(d => d.Name == fieldAccess.FieldName)
                        ?? throw new InvalidOperationException($"Unknown constant '{fieldAccess.FieldName}' in GVL '{gvlIdent.Name}'");
                    if (constDecl.DefaultValueText == null)
                        throw new InvalidOperationException($"GVL constant '{gvlIdent.Name}.{fieldAccess.FieldName}' has no default value");
                    return EvaluateConstExpr(Parser.ParseExpression(constDecl.DefaultValueText));
            }

            throw new xStunit.Runner.UnsupportedConstructException(
                expr.GetType().Name,
                $"Unsupported constant array-bound expression of type '{expr.GetType().Name}'");
        }
    }
}
