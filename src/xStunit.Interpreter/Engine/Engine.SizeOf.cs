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

        // SIZEOF's argument is normally an operand with a declared type, but
        // when it doesn't resolve as one, the identifier's own text is tried as a
        // type name - so SIZEOF(ST_Msg) and SIZEOF(DINT), naming a type rather
        // than a variable of that type, work too.
        private string ResolveTypeNameForSizeOf(Expr argExpr, Frame frame)
        {
            var declaredType = ResolveOperandTypeName(argExpr, frame);
            if (declaredType != null)
                return declaredType;

            if (argExpr is IdentifierExpr id)
                return id.Name;

            throw new NotSupportedException(
                "SIZEOF() argument must be a variable, field, array element, pointer dereference, or bare type name; " +
                $"got {argExpr.GetType().Name}");
        }

        // TwinCAT sizes p^ and a[i] at compile time from the declarations
        // alone, so this walks the declared types and never evaluates the
        // pointer or the index: the answer must not change when the pointer
        // is 0, points past its array, or the index is out of range, and
        // evaluating either would turn those into runtime faults.
        //
        // A REFERENCE TO is looked through before indexing because indexing
        // one indexes its referent; a dereference only applies to a POINTER
        // TO, so a reference or non-address operand of ^ has no type here.
        private string ResolveOperandTypeName(Expr expr, Frame frame)
        {
            switch (expr)
            {
                case DerefExpr deref:
                    var pointerType = ResolveAliasedOperandTypeName(deref.Inner, frame);
                    return AddressTypeInfo.TryGetPointeeTypeName(pointerType, out var pointee) ? pointee : null;

                case IndexExpr index:
                    var receiverType = ResolveAliasedOperandTypeName(index.Receiver, frame);
                    if (AddressTypeInfo.TryGetReferentTypeName(receiverType, out var referent))
                        receiverType = _registry.ResolveAlias(referent);
                    return receiverType != null && ArrayTypeInfo.TryGetElementTypeName(receiverType, out var element)
                        ? element
                        : null;

                default:
                    return ResolveDeclaredTypeName(expr, frame);
            }
        }

        private string ResolveAliasedOperandTypeName(Expr expr, Frame frame) =>
            _registry.ResolveAlias(ResolveOperandTypeName(expr, frame));

        // Built per call rather than held as a field: the bound resolver closes
        // over frame, which is the only frame-dependent part of the layout
        // rules, and parsing a bound expression dwarfs the allocation.
        private TypeLayout LayoutFor(Frame frame) =>
            new TypeLayout(
                _registry, _target, boundText => Convert.ToInt32(Evaluate(Parser.ParseExpression(boundText), frame)));

        private (int Size, int Align) SizeOfType(string typeName, Frame frame) =>
            LayoutFor(frame).SizeOf(typeName);
    }
}
