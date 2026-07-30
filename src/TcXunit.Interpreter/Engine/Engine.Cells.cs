using System;
using System.Collections.Generic;
using System.Linq;

namespace TcXunit.Interpreter
{
    public sealed partial class Engine
    {
        // source/sink aren't AST method params (Transmit is native, no
        // VarBlockParser decls to bind against) - resolved by fixed name
        // first (fbLink.Transmit(source:=..., sink:=...)), falling back to
        // IEC positional order same as BindParams.
        private Cell ResolveNamedOrPositionalCell(
            string paramName,
            int posIndex,
            IReadOnlyList<Expr> positionalArgs,
            IReadOnlyList<NamedArg> namedArgs,
            Frame callerFrame)
        {
            var match = namedArgs.FirstOrDefault(a => a.Name == paramName);
            if (match != null)
                return ResolveCellForLValue(match.Value, callerFrame);
            if (posIndex < positionalArgs.Count)
                return ResolveCellForLValue(positionalArgs[posIndex], callerFrame);

            throw new InvalidOperationException($"Transmit missing required argument '{paramName}'");
        }

        private static Expr ResolveNamedOrPositionalArg(
            string methodName,
            string paramName,
            int posIndex,
            IReadOnlyList<Expr> positionalArgs,
            IReadOnlyList<NamedArg> namedArgs)
        {
            var match = namedArgs.FirstOrDefault(a => a.Name == paramName);
            if (match != null)
                return match.Value;
            if (posIndex < positionalArgs.Count)
                return positionalArgs[posIndex];

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

        // ADR() target resolution: same as ResolveCellForLValue, except a bare
        // array (identifier or field, no index) decays to the address of its
        // first element - ADR(arr) means "address of arr[lowbound]" in IEC
        // 61131-3, and only an element-targeting Cell can be pointer-arithmetic'd
        // (see EvaluateBinary's Pointer +/- handling, TcXunit-sej.2).
        private Cell ResolveCellForAdr(Expr expr, Frame frame)
        {
            var cell = ResolveCellForLValue(expr, frame);
            if (!(cell is ArrayElementCell) && cell.Value is ArrayValue array)
                return new ArrayElementCell(array, 0);
            return cell;
        }

        // __ISVALIDREF(ref): TwinCAT intrinsic returning TRUE when a REFERENCE
        // TO variable currently aliases a valid target, FALSE when unassigned.
        // An unbound REFERENCE TO defaults to a null-valued Cell (DefaultValue
        // returns null for REFERENCE TO/POINTER TO), while a REF=-bound
        // reference aliases the target's own Cell (whose value is the FB/struct
        // /scalar it points at) - so a non-null resolved value maps to "valid".
        //
        // __ISVALIDREF only makes sense on a POINTER TO/REFERENCE TO variable
        // (TcXunit-6lh): every other declared type's DefaultValue is non-null
        // (0, FALSE, a constructed FbInstance/StructInstance, ...), so without
        // this check a copy-paste/typo mistake in test ST code - passing a
        // plain variable instead of a reference/pointer - would silently
        // evaluate to TRUE instead of surfacing the misuse, exactly the kind
        // of bug this framework exists to catch. Mirrors the "must be a
        // POINTER TO BYTE" argument-type validation in RequirePointerArg.
        private bool IsValidRef(Expr expr, Frame frame)
        {
            var cell = ResolveCellForLValue(expr, frame);
            var typeName = ResolveDeclaredTypeName(expr, frame);
            if (typeName == null ||
                !(typeName.StartsWith("POINTER TO") || typeName.StartsWith("REFERENCE TO")))
            {
                throw new InvalidOperationException(
                    $"__ISVALIDREF requires a POINTER TO or REFERENCE TO variable, but got " +
                    $"'{typeName ?? "unknown"}'");
            }

            return cell.Value != null;
        }

        // Declared IEC type text of the *name* an expression refers to - not
        // to be confused with ResolveCellForLValue's resolved Cell, which
        // for a REF=-bound REFERENCE TO/POINTER TO variable is the *target's*
        // Cell (aliasing, TcXunit-t6p) and so no longer carries the
        // reference variable's own declared type. Locals/instance fields
        // consult the FieldTypeNames/LocalTypeNames side tables (populated
        // once at declaration time, immune to later REF= aliasing);
        // GVL members and STRUCT fields are never REF= targets (the parser
        // only accepts a bare identifier as a REF= target), so their Cell's
        // DeclaredTypeName is always trustworthy.
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

                // TcXunit-6t0: a STRUCT's own FieldTypeNames side table,
                // same reasoning as FbInstance.FieldTypeNames above - a
                // struct-member REF= target (stWidget.ipHandler REF= ...)
                // replaces that field's Cell in Fields wholesale, so
                // Cell.DeclaredTypeName below would otherwise reflect the
                // REF=-bound target instead of the member's own declaration.
                if (receiver is StructInstance st)
                    return st.FieldTypeNames.TryGetValue(fieldAccess.FieldName, out var stFieldType) ? stFieldType : null;

                var fields = FieldsOf(receiver);
                return fields.TryGetValue(fieldAccess.FieldName, out var fieldCell) ? fieldCell.DeclaredTypeName : null;
            }

            return null;
        }

        // FbInstance and StructInstance are both "named-field container of
        // Cells" (T7's struct Cell-shape decision) - FieldAccessExpr reads
        // either the same way.
        private static Dictionary<string, Cell> FieldsOf(object receiver) => receiver switch
        {
            FbInstance fb => fb.Fields,
            StructInstance st => st.Fields,
            _ => throw new NotSupportedException($"Cannot access fields on {receiver?.GetType().Name}"),
        };
    }
}
