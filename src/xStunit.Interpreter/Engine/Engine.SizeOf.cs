using System;

namespace xStunit.Interpreter
{
    public sealed partial class Engine
    {
        private int EvaluateSizeOf(Expr argExpr, Frame frame)
        {
            var typeName = ResolveTypeNameForSizeOf(argExpr, frame);
            var (size, _) = SizeOfType(typeName, frame);
            return size;
        }

        // SIZEOF's argument is normally a declared variable or field, but when
        // it doesn't resolve as one, the identifier's own text is tried as a
        // type name - so SIZEOF(ST_Msg) and SIZEOF(DINT), naming a type rather
        // than a variable of that type, work too.
        private string ResolveTypeNameForSizeOf(Expr argExpr, Frame frame)
        {
            var declaredType = ResolveDeclaredTypeName(argExpr, frame);
            if (declaredType != null)
                return declaredType;

            if (argExpr is IdentifierExpr id)
                return id.Name;

            throw new NotSupportedException(
                $"SIZEOF() argument must be a variable, field, or bare type name; got {argExpr.GetType().Name}");
        }

        // Built per call rather than held as a field: the bound resolver closes
        // over frame, which is the only frame-dependent part of the layout
        // rules, and parsing a bound expression dwarfs the allocation.
        private TypeLayout LayoutFor(Frame frame) =>
            new TypeLayout(_registry, boundText => Convert.ToInt32(Evaluate(Parser.ParseExpression(boundText), frame)));

        private (int Size, int Align) SizeOfType(string typeName, Frame frame) =>
            LayoutFor(frame).SizeOf(typeName);
    }
}
