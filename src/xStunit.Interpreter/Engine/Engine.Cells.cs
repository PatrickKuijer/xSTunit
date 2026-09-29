using System;
using System.Collections.Generic;
using System.Linq;
using xStunit.Runner;

namespace xStunit.Interpreter
{
    public sealed partial class Engine
    {
        // Transmit is native, so source/sink have no VarBlockParser decls for
        // BindParams to bind against: resolve them by fixed name first
        // (fbLink.Transmit(source:=..., sink:=...)), then by IEC positional
        // order.
        private Cell ResolveNamedOrPositionalCell(
            string paramName,
            int posIndex,
            IReadOnlyList<Expr> positionalArgs,
            IReadOnlyList<NamedArg> namedArgs,
            Frame callerFrame)
        {
            // posIndex is a by-value parameter here (one fixed declared
            // position per call), so passing it by ref to the shared binder is
            // safe: the increment lands on this local copy and is discarded.
            if (ArgBinder.TryResolveArg(
                paramName,
                name => ArgBinder.FindNamed(namedArgs, name),
                positionalArgs,
                ref posIndex,
                out var argExpr))
                return ResolveCellForLValue(argExpr, callerFrame);

            throw new InvalidOperationException($"Transmit missing required argument '{paramName}'");
        }

        private static Expr ResolveNamedOrPositionalArg(
            string methodName,
            string paramName,
            int posIndex,
            IReadOnlyList<Expr> positionalArgs,
            IReadOnlyList<NamedArg> namedArgs)
        {
            // Same fixed-position usage as ResolveNamedOrPositionalCell above.
            if (ArgBinder.TryResolveArg(
                paramName,
                name => ArgBinder.FindNamed(namedArgs, name),
                positionalArgs,
                ref posIndex,
                out var argExpr))
                return argExpr;

            throw new InvalidOperationException($"{methodName} missing required argument '{paramName}'");
        }

        private Cell ResolveCellForLValue(Expr expr, Frame frame)
        {
            if (expr is IdentifierExpr id)
            {
                var cell = frame.ResolveCell(id.Name);
                if (cell == null && !TryResolveGlobalCell(id.Name, out cell))
                    throw new InvalidOperationException($"Unknown variable '{id.Name}'");
                return cell;
            }

            if (expr is FieldAccessExpr fieldAccess)
            {
                if (TryGetGvlFields(fieldAccess, frame, out var gvlFields))
                {
                    if (!gvlFields.TryGetValue(fieldAccess.FieldName, out var gvlCell))
                        throw new InvalidOperationException($"Unknown field '{fieldAccess.FieldName}'");
                    return gvlCell;
                }

                var fields = FieldsOf(Evaluate(fieldAccess.Receiver, frame), fieldAccess.FieldName);
                if (!fields.TryGetValue(fieldAccess.FieldName, out var cell))
                    throw new InvalidOperationException($"Unknown field '{fieldAccess.FieldName}'");
                return cell;
            }

            if (expr is IndexExpr index)
            {
                var receiverValue = Evaluate(index.Receiver, frame);
                if (receiverValue is string)
                {
                    var parentCell = ResolveCellForLValue(index.Receiver, frame);
                    return new StringByteCell(parentCell, ResolveStringIndex(index.Indices, frame));
                }

                var array = (ArrayValue)receiverValue;
                return new ArrayElementCell(array, FlattenIndex(array, index.Indices, frame));
            }

            throw new NotSupportedException("Only plain identifiers, field access, and array indexing are supported as REF=/ADR()/Transmit() targets in v1");
        }

        // ResolveCellForLValue, except that a bare array (identifier or field,
        // no index) decays to the address of its first element: ADR(arr) means
        // "address of arr[lowbound]" in IEC 61131-3, and only an
        // element-targeting Cell can be pointer-arithmetic'd.
        private Cell ResolveCellForAdr(Expr expr, Frame frame)
        {
            var cell = ResolveCellForLValue(expr, frame);
            if (!(cell is ArrayElementCell) && cell.Value is ArrayValue array)
                return new ArrayElementCell(array, 0);
            return cell;
        }

        // __ISVALIDREF(ref): TwinCAT intrinsic, TRUE when a REFERENCE TO
        // variable currently aliases a valid target. An unbound REFERENCE TO
        // holds a null-valued Cell, while a REF=-bound one aliases the target's
        // own Cell, so a non-null resolved value means "valid".
        //
        // The declared-type check is what keeps that mapping honest: every
        // other declared type defaults to something non-null (0, FALSE, a
        // constructed FbInstance/StructInstance), so passing a plain variable
        // here by mistake would silently evaluate to TRUE instead of surfacing
        // the misuse.
        //
        // The check runs on the alias-resolved name so that a variable
        // declared through an ALIAS DUT (pData : PT_Byte, PT_Byte being
        // POINTER TO BYTE) answers as its underlying type does. TypeLayout's
        // SIZEOF and Engine.Defaults already size and null-default that
        // declaration as a pointer; testing the unresolved name here would
        // leave the one intrinsic that reads the null they establish unable to
        // see it. The message still names the type as written, since that is
        // the text to search the VAR block for.
        private bool IsValidRef(Expr expr, Frame frame)
        {
            var cell = ResolveCellForLValue(expr, frame);
            var typeName = ResolveDeclaredTypeName(expr, frame);
            if (!AddressTypeInfo.IsAddressType(_registry.ResolveAlias(typeName)))
            {
                throw new InvalidOperationException(
                    $"__ISVALIDREF requires a POINTER TO or REFERENCE TO variable, but got " +
                    $"'{typeName ?? "unknown"}'");
            }

            return cell.Value != null;
        }

        // Declared IEC type text of the *name* an expression refers to, which is
        // not the same as the DeclaredTypeName on the Cell
        // ResolveCellForLValue returns: for a REF=-bound REFERENCE TO/POINTER TO
        // variable that Cell is the *target's*, and so carries the target's
        // declared type rather than the reference variable's own.
        //
        // Locals and instance fields therefore consult the
        // LocalTypeNames/FieldTypeNames side tables, populated once at
        // declaration time and immune to later REF= aliasing. GVL members are
        // never REF= targets - the parser accepts only a bare identifier there -
        // so their Cell's DeclaredTypeName is trustworthy as it stands.
        private string ResolveDeclaredTypeName(Expr expr, Frame frame)
        {
            if (expr is IdentifierExpr id)
            {
                if (frame.LocalTypeNames.TryGetValue(id.Name, out var localType))
                    return localType;
                if (frame.Instance != null && frame.Instance.FieldTypeNames.TryGetValue(id.Name, out var fieldType))
                    return fieldType;
                if (TryResolveGlobalCell(id.Name, out var globalCell))
                    return globalCell.DeclaredTypeName;
                return null;
            }

            if (expr is FieldAccessExpr fieldAccess)
            {
                if (TryGetGvlFields(fieldAccess, frame, out var gvlFields))
                    return gvlFields.TryGetValue(fieldAccess.FieldName, out var gvlCell) ? gvlCell.DeclaredTypeName : null;

                var receiver = Evaluate(fieldAccess.Receiver, frame);
                if (receiver is FbInstance fb)
                    return fb.FieldTypeNames.TryGetValue(fieldAccess.FieldName, out var fbFieldType) ? fbFieldType : null;

                // A struct-member REF= target (stWidget.ipHandler REF= ...)
                // replaces that field's Cell in Fields wholesale, so the
                // side table is needed here for the same reason as above:
                // Cell.DeclaredTypeName would otherwise report the REF=-bound
                // target's type instead of the member's own declaration.
                if (receiver is StructInstance st)
                    return st.FieldTypeNames.TryGetValue(fieldAccess.FieldName, out var stFieldType) ? stFieldType : null;

                var fields = FieldsOf(receiver, fieldAccess.FieldName);
                return fields.TryGetValue(fieldAccess.FieldName, out var fieldCell) ? fieldCell.DeclaredTypeName : null;
            }

            return null;
        }

        // AssertEquals(ANY) dispatches on the TYPE CLASS (see
        // StringTypeInfo.TypeClass), not the declared type. The collapse
        // happens here rather than inside ResolveDeclaredTypeName because
        // SIZEOF still needs the full declaration, capacity and all.
        //
        // A literal argument has no declaration to read a type off at all, and
        // an untyped argument fails on the type class before the values are
        // ever compared. Two disjoint groups of literal carry the typing, in
        // three passes that each read only what the pass before them settled -
        // which is what makes the answer the same in either argument order.
        //
        // Pass 2, SELF-TYPED: a literal with one unambiguous class of its own
        // (BOOL and the TIME/DATE family) takes it, and never adopts - T#1s
        // opposite a UDINT must stay a mismatch, TIME being no kind of integer.
        // A string literal belongs here too: the only open question is which of
        // the two string keywords it takes, and since the lexer folds '...' and
        // "..." into one token, only a declared side can ever say WSTRING.
        //
        // Pass 3, ADOPTING: a numeric literal has no width of its own - 8 is an
        // INT against an INT and a DINT against a DINT - so it can only take
        // the other side's class, bounded to its own family so that a REAL
        // literal cannot land on INT and an integer literal cannot land on
        // TIME or STRING.
        private (string Expected, string Actual) ResolveAnyTypeClasses(Expr expectedExpr, Expr actualExpr, Frame frame)
        {
            expectedExpr = UnwrapSignedLiteral(expectedExpr);
            actualExpr = UnwrapSignedLiteral(actualExpr);

            var declaredExpected = CanonicalTypeClass(StringTypeInfo.TypeClass(ResolveDeclaredTypeName(expectedExpr, frame)));
            var declaredActual = CanonicalTypeClass(StringTypeInfo.TypeClass(ResolveDeclaredTypeName(actualExpr, frame)));

            var expected = declaredExpected ?? SelfTypedLiteralClass(expectedExpr, declaredActual);
            var actual = declaredActual ?? SelfTypedLiteralClass(actualExpr, declaredExpected);

            expected = expected ?? AdoptableLiteralClass(expectedExpr, actual);
            actual = actual ?? AdoptableLiteralClass(actualExpr, expected);

            // With nothing declared on either side there is nothing to adopt
            // from, and the runner would otherwise report a missing type ''
            // that names neither the call nor the reason.
            if (expected == null && actual == null
                && (IsNumericLiteral(expectedExpr) || IsNumericLiteral(actualExpr)))
            {
                throw new UnsupportedConstructException(
                    "AssertEquals",
                    "AssertEquals(ANY) cannot type an argument pair with nothing declared on either " +
                    "side: a numeric literal has no width of its own. Compare against a declared " +
                    "variable, or call the typed AssertEquals_<TYPE> overload.");
            }

            return (expected, actual);
        }

        // DT and TOD are accepted declaration text for the same types the DT#
        // and TOD# literals produce, but are not type classes in their own
        // right; without this the new self-typing would turn a var declared DT
        // into a mismatch against its own literal.
        private static string CanonicalTypeClass(string typeClass)
        {
            if (IecIdentifier.Matches(typeClass, "DT"))
                return "DATE_AND_TIME";
            if (IecIdentifier.Matches(typeClass, "TOD"))
                return "TIME_OF_DAY";
            return typeClass;
        }

        // A leading minus parses as a UnaryExpr over the literal rather than
        // folding into it, so every negative expected value would otherwise
        // reach the assert untyped. The sign is carried into the literal here
        // because the range bound below has to see the value that will be
        // compared, not its magnitude.
        private static Expr UnwrapSignedLiteral(Expr expr)
        {
            if (!(expr is UnaryExpr unary) || unary.Op != "-")
                return expr;

            switch (UnwrapSignedLiteral(unary.Operand))
            {
                case IntLiteralExpr i: return new IntLiteralExpr(-i.Value);
                case LintLiteralExpr l: return new LintLiteralExpr(-l.Value);
                case RealLiteralExpr r: return new RealLiteralExpr(-r.Value);
                case LrealLiteralExpr lr: return new LrealLiteralExpr(-lr.Value);
                default: return expr;
            }
        }

        private static string SelfTypedLiteralClass(Expr expr, string otherDeclaredClass)
        {
            switch (expr)
            {
                case BoolLiteralExpr _: return "BOOL";
                case TimeLiteralExpr _: return "TIME";
                case LtimeLiteralExpr _: return "LTIME";
                case DateLiteralExpr _: return "DATE";
                case DateAndTimeLiteralExpr _: return "DATE_AND_TIME";
                case TimeOfDayLiteralExpr _: return "TIME_OF_DAY";
                case StringLiteralExpr _: return otherDeclaredClass == "WSTRING" ? "WSTRING" : "STRING";
                default: return null;
            }
        }

        private static readonly string[] IntegerTypeClasses =
        {
            "SINT", "USINT", "BYTE", "INT", "UINT", "WORD",
            "DINT", "DWORD", "UDINT", "LINT", "LWORD", "ULINT",
        };

        // The class is handed back exactly as the other side spelled it, so a
        // failure names the type the way its declaration does.
        //
        // The two real literals take one width each, and are not
        // interchangeable. An unsuffixed decimal lexes as a 32-bit float, so it
        // has already lost the mantissa an LREAL compare would need, and the
        // ANY overload compares with Delta := 0.0: letting it take LREAL would
        // fail on the VALUE while both sides looked right. The LREAL# form is
        // parsed as a double and carries the wide case instead.
        private static string AdoptableLiteralClass(Expr expr, string otherClass)
        {
            if (otherClass == null)
                return null;

            if (expr is RealLiteralExpr)
                return IecIdentifier.Matches(otherClass, "REAL") ? otherClass : null;

            if (expr is LrealLiteralExpr)
                return IecIdentifier.Matches(otherClass, "LREAL") ? otherClass : null;

            if (!IsIntegerLiteral(expr)
                || !IntegerTypeClasses.Contains(otherClass, IecIdentifier.Comparer))
                return null;

            return LiteralFitsWithin(expr, otherClass) ? otherClass : null;
        }

        // TwinCAT rejects an out-of-range literal at compile time and this
        // interpreter has no compile step, so the bound is the only thing
        // between AssertEquals(70000, nInt) and a silent pass: the assert
        // truncates both operands to the adopted width, which would compare
        // 70000 equal to an INT holding 4464.
        private static bool LiteralFitsWithin(Expr literal, string typeClass)
        {
            if (!IecNumericType.TryGetBounds(typeClass, out var bounds))
                return false;

            switch (literal)
            {
                case IntLiteralExpr i: return FitsSigned(i.Value, bounds);
                case LintLiteralExpr l: return FitsSigned(l.Value, bounds);
                case UlintLiteralExpr u: return FitsUnsigned(u.Value, bounds);
                default: return false;
            }
        }

        // ULINT and LWORD are the only classes whose bounds box as ulong, and
        // they are also the only ones a value above long.MaxValue can fit -
        // which is the only shape a UlintLiteralExpr is produced for at all.
        private static bool FitsSigned(long value, (object Min, object Max) bounds) =>
            bounds.Max is ulong
                ? value >= 0
                : value >= Convert.ToInt64(bounds.Min) && value <= Convert.ToInt64(bounds.Max);

        private static bool FitsUnsigned(ulong value, (object Min, object Max) bounds) =>
            bounds.Max is ulong max ? value <= max : value <= (ulong)Convert.ToInt64(bounds.Max);

        private static bool IsIntegerLiteral(Expr expr) =>
            expr is IntLiteralExpr || expr is LintLiteralExpr || expr is UlintLiteralExpr;

        private static bool IsNumericLiteral(Expr expr) =>
            IsIntegerLiteral(expr) || expr is RealLiteralExpr || expr is LrealLiteralExpr;

        // FbInstance and StructInstance are both named-field containers of
        // Cells, so FieldAccessExpr reads either the same way.
        //
        // memberName is the field being reached for, carried in only so an
        // unassigned interface reference can name it: reaching THROUGH a null
        // contract is a defect in the code under test, and "Cannot access
        // fields on UnassignedInterfaceReference" would describe this
        // interpreter's plumbing instead of the missing injection.
        private static Dictionary<string, Cell> FieldsOf(object receiver, string memberName) => receiver switch
        {
            FbInstance fb => fb.Fields,
            StructInstance st => st.Fields,
            UnassignedInterfaceReference unassigned => throw unassigned.Fault(memberName),
            _ => throw new NotSupportedException($"Cannot access fields on {receiver?.GetType().Name}"),
        };
    }
}
