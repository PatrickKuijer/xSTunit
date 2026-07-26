using System;
using System.Linq;

namespace TcXunit.Interpreter
{
    public sealed partial class Engine
    {
        private object DefaultValue(VarDecl decl, FbInstance owningInstance)
        {
            if (ArrayTypeInfo.IsArrayType(decl.TypeName))
                return BuildArrayDefault(decl, owningInstance);

            var structAst = _registry.GetStruct(decl.TypeName);
            if (structAst != null)
                return BuildStructDefault(structAst, decl.DefaultValueText, owningInstance);

            if (_registry.Get(decl.TypeName) != null || NativeTimerTypes.Contains(decl.TypeName) || decl.TypeName == "Loopback" || NativeEdgeTriggerTypes.Contains(decl.TypeName))
                return NewInstance(decl.TypeName);

            if (decl.DefaultValueText != null)
                return Evaluate(Parser.ParseExpression(decl.DefaultValueText), new Frame(owningInstance, decl.TypeName));

            if (StringTypeInfo.IsStringType(decl.TypeName))
                return "";

            if (decl.TypeName == "BOOL")
                return false;

            if (decl.TypeName == "REAL")
                return 0f;

            if (decl.TypeName == "LREAL")
                return 0d;

            if (decl.TypeName == "TIME")
                return 0u;

            if (decl.TypeName == "LTIME")
                return 0ul;

            if (decl.TypeName.StartsWith("POINTER TO") || decl.TypeName.StartsWith("REFERENCE TO"))
                return null;

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
                instance.Fields[field.Name] = new Cell { Value = DefaultValue(field, owningInstance), DeclaredTypeName = field.TypeName };

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
            var (dimensions, elementTypeName) = ArrayTypeInfo.Parse(decl.TypeName);
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
