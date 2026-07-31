using System;
using System.Linq;

namespace TcXunit.Interpreter
{
    public sealed partial class Engine
    {
        private object DefaultValue(VarDecl decl, FbInstance owningInstance)
        {
            // Resolve through any ALIAS DUT (TcXunit-6hg, e.g. T_MaxString ->
            // STRING(255)) once up front so every check below - ARRAY,
            // STRUCT, FB/native-timer instance, STRING, scalar - operates on
            // the underlying type text without needing its own alias-aware
            // branch.
            var typeName = _registry.ResolveAlias(decl.TypeName);

            if (ArrayTypeInfo.IsArrayType(typeName))
                return BuildArrayDefault(decl, owningInstance);

            var structAst = _registry.GetStruct(typeName);
            if (structAst != null)
                return BuildStructDefault(structAst, decl.DefaultValueText, owningInstance);

            if (_registry.Get(typeName) != null || IsNativeFbTypeName(typeName))
                return NewInstance(typeName);

            // TcXunit-5qs: a bare (unsuffixed) decimal literal like 2.5 lexes
            // as a REAL/float (Lexer.cs numeric-literal scan, RealLiteral is
            // the no-suffix default), so 'lrGain : LREAL := 2.5;' would
            // otherwise store a boxed float into a Cell declared LREAL - no
            // exception, just silent REAL-precision arithmetic for the rest
            // of that variable's life. Route through CoerceForAssignment
            // against the type's own zero value (same widening rule as every
            // other assignment) so the initializer widens the same way
            // 'lrGain := 2.5;' would post-declaration.
            if (decl.DefaultValueText != null)
            {
                var initValue = Evaluate(Parser.ParseExpression(decl.DefaultValueText), new Frame(owningInstance, typeName));
                return IecNumericType.TryGetDefault(typeName, out var initNumericDefault)
                    ? CoerceForAssignment(initNumericDefault, initValue)
                    : initValue;
            }

            // The elementary non-numeric defaults (STRING/W?STRING(n), BOOL,
            // TIME/LTIME, DATE-family) are owned by IecElementaryDefault
            // (TcXunit-mvbk/-om7f), shared with SeedReturnCell so the two
            // seeding sites can't drift apart.
            if (IecElementaryDefault.TryGetDefault(typeName, out var elementaryDefault))
                return elementaryDefault;

            // TcXunit-fzm: IEC 61131-3 type names are case-insensitive, so
            // the POINTER TO/REFERENCE TO prefix check below matches
            // case-insensitively - the same decision as IecNumericType,
            // StringTypeInfo, ArrayTypeInfo, and TypeRegistry. The native-FB
            // base-type check above (IsNativeFbTypeName,
            // Engine.NativeHost.cs) is a separate lookup family (native stub
            // instantiation, not elementary-type defaulting) - matched
            // case-insensitively too, via NativeTimerTypes/
            // NativeEdgeTriggerTypes's OrdinalIgnoreCase comparer and its
            // OrdinalIgnoreCase NativeLoopbackType compare (TcXunit-nch).

            if (typeName.StartsWith("POINTER TO", StringComparison.OrdinalIgnoreCase)
                || typeName.StartsWith("REFERENCE TO", StringComparison.OrdinalIgnoreCase))
                return null;

            if (IecNumericType.TryGetDefault(typeName, out var numericDefault))
                return numericDefault;

            return 0;
        }

        // Builds a STRUCT default: every declared field at its own
        // DefaultValue first, then overlays the struct literal initializer
        // (if any) on top - unset fields keep the type's default value per
        // TwinCAT's documented partial-initialization behavior
        // (TcXunit-w5x.15.6 / T7).
        private StructInstance BuildStructDefault(StructAst structAst, string literalText, FbInstance owningInstance)
        {
            var instance = new StructInstance(structAst.Name);
            foreach (var field in structAst.Fields)
            {
                instance.Fields[field.Name] = new Cell { Value = DefaultValue(field, owningInstance), DeclaredTypeName = field.TypeName };
                instance.FieldTypeNames[field.Name] = field.TypeName;
            }

            if (literalText != null && Parser.ParseExpression(literalText) is StructLiteralExpr lit)
                OverlayStruct(instance, lit, new Frame(owningInstance, structAst.Name));

            return instance;
        }

        // Builds an ARRAY default: every element at the declared element
        // type's DefaultValue, then overlays the array literal initializer
        // (if any) positionally - unset trailing elements keep the element
        // type's default, matching the array literal's own partial-init
        // shorthand (TcXunit-w5x.15.6).
        private ArrayValue BuildArrayDefault(VarDecl decl, FbInstance owningInstance)
        {
            var (dimensions, elementTypeName) = ArrayTypeInfo.Parse(
                decl.TypeName, boundText => ResolveArrayBound(boundText, owningInstance));
            var count = dimensions.Aggregate(1, (acc, d) => acc * (d.Hi - d.Lo + 1));
            var elementDecl = new VarDecl(null, elementTypeName, null, VarSection.Local);

            var elements = new object[count];
            for (var i = 0; i < count; i++)
                elements[i] = DefaultValue(elementDecl, owningInstance);

            var array = new ArrayValue(dimensions, elementTypeName, elements);

            if (decl.DefaultValueText != null && Parser.ParseExpression(decl.DefaultValueText) is ArrayLiteralExpr lit)
                OverlayArray(array, lit, new Frame(owningInstance, elementTypeName));

            return array;
        }

        // Resolves a non-literal ARRAY bound (e.g. a GVL-qualified constant
        // like "cRemoteClientConfig.MAX_REMOTE_ITEMS") through the normal
        // Evaluate() path (TcXunit-654): array bounds are IEC 61131-3
        // constant expressions, not just bare integer literals, but they're
        // parsed from a raw type-name string at VarDecl/DefaultValue time -
        // outside the AST's normal expression positions - so ArrayTypeInfo
        // can't evaluate them itself. owningInstance may be null (e.g. while
        // computing a GVL's own default values at Engine construction, or a
        // top-level suite field with no enclosing instance yet); Evaluate's
        // GVL lookup (TryGetGvlFields/TryResolveGlobalCell) doesn't need a
        // non-null Frame.Instance, only DeclaringTypeName-free global scope.
        private int ResolveArrayBound(string boundText, FbInstance owningInstance)
        {
            var value = Evaluate(Parser.ParseExpression(boundText), new Frame(owningInstance, null));
            return Convert.ToInt32(value);
        }

        // Applies a struct/array literal's per-field/per-element expression
        // on top of an already-defaulted value: nested struct/array fields
        // recurse into the matching overlay so partial initializers compose
        // (e.g. an ARRAY-of-STRUCT field overriding only some elements).
        // Anything else is a plain evaluate + assignment-coerce.
        private object OverlayOrEvaluate(object existingDefault, Expr expr, Frame frame)
        {
            if (expr is StructLiteralExpr structLit && existingDefault is StructInstance structInst)
            {
                OverlayStruct(structInst, structLit, frame);
                return structInst;
            }

            if (expr is ArrayLiteralExpr arrayLit && existingDefault is ArrayValue arrayVal)
            {
                OverlayArray(arrayVal, arrayLit, frame);
                return arrayVal;
            }

            return CoerceForAssignment(existingDefault, Evaluate(expr, frame));
        }

        private void OverlayStruct(StructInstance instance, StructLiteralExpr lit, Frame frame)
        {
            foreach (var init in lit.FieldInits)
            {
                if (!instance.Fields.TryGetValue(init.Name, out var cell))
                    throw new InvalidOperationException($"Unknown field '{init.Name}' on struct '{instance.TypeName}'");
                cell.Value = OverlayOrEvaluate(cell.Value, init.Value, frame);
            }
        }

        private void OverlayArray(ArrayValue array, ArrayLiteralExpr lit, Frame frame)
        {
            for (var i = 0; i < lit.Elements.Count && i < array.Elements.Length; i++)
                array.Elements[i] = OverlayOrEvaluate(array.Elements[i], lit.Elements[i], frame);
        }
    }
}
