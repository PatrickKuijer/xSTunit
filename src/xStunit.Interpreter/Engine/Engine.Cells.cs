using System;
using System.Collections.Generic;
using System.Linq;

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
                name => namedArgs.FirstOrDefault(a => a.Name == name)?.Value,
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
                name => namedArgs.FirstOrDefault(a => a.Name == name)?.Value,
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

                var fields = FieldsOf(Evaluate(fieldAccess.Receiver, frame));
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

                var fields = FieldsOf(receiver);
                return fields.TryGetValue(fieldAccess.FieldName, out var fieldCell) ? fieldCell.DeclaredTypeName : null;
            }

            return null;
        }

        // FbInstance and StructInstance are both named-field containers of
        // Cells, so FieldAccessExpr reads either the same way.
        private static Dictionary<string, Cell> FieldsOf(object receiver) => receiver switch
        {
            FbInstance fb => fb.Fields,
            StructInstance st => st.Fields,
            _ => throw new NotSupportedException($"Cannot access fields on {receiver?.GetType().Name}"),
        };
    }
}
