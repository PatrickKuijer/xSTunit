using System;
using System.Linq;

namespace xStunit.Interpreter
{
    public sealed partial class Engine
    {
        private object DefaultValue(VarDecl decl, FbInstance owningInstance)
        {
            // Resolve any ALIAS DUT (e.g. T_MaxString -> STRING(255)) once up
            // front, so no check below needs an alias-aware branch of its own.
            var typeName = _registry.ResolveAlias(decl.TypeName);

            if (ArrayTypeInfo.IsArrayType(typeName))
                return BuildArrayDefault(decl, owningInstance);

            var structAst = _registry.GetStruct(typeName);
            if (structAst != null)
                return BuildStructDefault(structAst, decl.DefaultValueText, owningInstance);

            // Only a LOADED interface gets the sentinel. A name no .TcIO
            // declared is not known to be an interface at all, and still falls
            // through to the int-0 default at the bottom.
            if (_registry.GetInterface(typeName) != null)
                return new UnassignedInterfaceReference(typeName);

            if (_registry.Get(typeName) != null || IsNativeFbTypeName(typeName))
                return NewInstance(typeName);

            // A bare, unsuffixed decimal literal like 2.5 lexes as a REAL, so
            // 'lrGain : LREAL := 2.5;' would otherwise box a float into a Cell
            // declared LREAL - no exception, just silent REAL-precision
            // arithmetic for the rest of that variable's life. Coercing against
            // the declared type's own zero makes the initializer widen exactly
            // as 'lrGain := 2.5;' would after the declaration.
            if (decl.DefaultValueText != null)
            {
                var initValue = Evaluate(Parser.ParseExpression(decl.DefaultValueText), new Frame(owningInstance, typeName));
                return IecNumericType.TryGetDefault(typeName, out var initNumericDefault)
                    ? CoerceForAssignment(initNumericDefault, initValue)
                    : initValue;
            }

            // The elementary non-numeric defaults (STRING/W?STRING(n), BOOL,
            // TIME/LTIME, DATE-family) are owned by IecElementaryDefault,
            // shared with SeedReturnCell so the two seeding sites can't drift
            // apart.
            if (IecElementaryDefault.TryGetDefault(typeName, out var elementaryDefault))
                return elementaryDefault;

            // An address type starts unbound rather than at a value, which is
            // what makes __ISVALIDREF's null test meaningful.
            if (AddressTypeInfo.IsAddressType(typeName))
                return null;

            if (IecNumericType.TryGetDefault(typeName, out var numericDefault))
                return numericDefault;

            return 0;
        }

        // The declaration's own defaults first, then the struct literal
        // initializer (if any) overlaid on top, so unset fields keep the type's
        // default - TwinCAT's documented partial-initialization behaviour. The
        // literal overlays a UNION the same way, because it writes through the
        // member Cells either way.
        private StructInstance BuildStructDefault(StructAst structAst, string literalText, FbInstance owningInstance)
        {
            var instance = structAst.IsUnion
                ? BuildUnionDefault(structAst, owningInstance)
                : BuildFieldwiseDefault(structAst, owningInstance);

            if (literalText != null && Parser.ParseExpression(literalText) is StructLiteralExpr lit)
                OverlayStruct(instance, lit, new Frame(owningInstance, structAst.Name));

            return instance;
        }

        // A STRUCT's fields are independent storage, so each one is built at its
        // own DefaultValue.
        private StructInstance BuildFieldwiseDefault(StructAst structAst, FbInstance owningInstance)
        {
            var instance = new StructInstance(structAst.Name);
            foreach (var field in structAst.Fields)
            {
                instance.Fields[field.Name] = NewDeclaredCell(DefaultValue(field, owningInstance), field.TypeName, owningInstance);
                instance.FieldTypeNames[field.Name] = field.TypeName;
            }

            return instance;
        }

        // A UNION has one storage, so it has one initial value. Its members are
        // seated as views onto a zeroed buffer, which already reads as every
        // member's own default, and only the members carrying a declared
        // initial value are written - in declaration order, the last one
        // written keeping the bytes it overlaps. Writing the unset members'
        // defaults too would be the same bytes and one more way for a member
        // type with no wire format to sink a declaration that never reads it.
        private StructInstance BuildUnionDefault(StructAst unionAst, FbInstance owningInstance)
        {
            var frame = new Frame(owningInstance, unionAst.Name);
            var instance = LayoutFor(frame).NewUnionInstance(unionAst);
            foreach (var field in unionAst.Fields)
            {
                if (field.DefaultValueText != null)
                    instance.Fields[field.Name].Value = DefaultValue(field, owningInstance);
            }

            return instance;
        }

        // Every element at the declared element type's DefaultValue, then the
        // array literal initializer (if any) overlaid positionally, so unset
        // trailing elements keep the element type's default - the array
        // literal's own partial-initialization shorthand.
        private ArrayValue BuildArrayDefault(VarDecl decl, FbInstance owningInstance)
        {
            var (dimensions, elementTypeName) = ArrayTypeInfo.Parse(
                decl.TypeName, boundText => ResolveArrayBound(boundText, owningInstance));
            var count = dimensions.Aggregate(1, (acc, d) => acc * (d.Hi - d.Lo + 1));
            var elementDecl = new VarDecl(null, elementTypeName, null, VarSection.Local);

            var elements = new object[count];
            for (var i = 0; i < count; i++)
                elements[i] = DefaultValue(elementDecl, owningInstance);

            var array = new ArrayValue(
                dimensions, elementTypeName, elements, ResolveStringCapacity(elementTypeName, owningInstance));

            if (decl.DefaultValueText != null && Parser.ParseExpression(decl.DefaultValueText) is ArrayLiteralExpr lit)
                OverlayArray(array, lit, new Frame(owningInstance, elementTypeName));

            return array;
        }

        // Array bounds are IEC 61131-3 constant expressions, not just integer
        // literals (e.g. "cRemoteClientConfig.MAX_REMOTE_ITEMS"), but they are
        // parsed out of a raw type-name string rather than an AST expression
        // position, so ArrayTypeInfo cannot evaluate them itself.
        //
        // owningInstance may be null - a GVL's own default values are computed
        // at Engine construction, and a top-level suite field has no enclosing
        // instance yet - which is fine because the GVL lookups Evaluate falls
        // back on need only global scope, not a Frame.Instance.
        private int ResolveArrayBound(string boundText, FbInstance owningInstance)
        {
            var value = Evaluate(Parser.ParseExpression(boundText), new Frame(owningInstance, null));
            return Convert.ToInt32(value);
        }

        // Resolves a declaration's STRING/WSTRING capacity through ALIAS DUTs
        // and through a constant-expression size, neither of which a Cell could
        // settle from the type text it holds. Every VarDecl-driven Cell and
        // every ARRAY gets its capacity from here, so Cell.StringCapacity and
        // PackValue's own truncation cannot disagree about a declaration.
        //
        // A size naming an identifier that does not resolve leaves the cell
        // Unbounded instead of sinking the declaration: GVL cells are allocated
        // before any GVL constant has a value, so a size expression referring
        // to one is genuinely unresolvable on the first pass, and the Engine
        // constructor re-resolves it once the constants converge. Anything else
        // the evaluator throws - an overflowing or non-numeric size - is left
        // to surface.
        private int ResolveStringCapacity(string declaredTypeName, FbInstance owningInstance)
        {
            try
            {
                return StringTypeInfo.ResolveCapacity(
                    _registry.ResolveAlias(declaredTypeName),
                    sizeText => ResolveArrayBound(sizeText, owningInstance));
            }
            catch (InvalidOperationException)
            {
                return Cell.Unbounded;
            }
        }

        // Every VarDecl-driven Cell construction goes through here, so no
        // declaration site can forget to resolve its capacity and silently
        // reintroduce a string that outgrows its declaration.
        private Cell NewDeclaredCell(object value, string declaredTypeName, FbInstance owningInstance) =>
            new Cell
            {
                Value = value,
                DeclaredTypeName = declaredTypeName,
                StringCapacity = ResolveStringCapacity(declaredTypeName, owningInstance),
            };

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
                array.SetElement(i, OverlayOrEvaluate(array.Elements[i], lit.Elements[i], frame));
        }
    }
}
