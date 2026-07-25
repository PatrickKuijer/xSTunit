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
                var array = (ArrayValue)Evaluate(index.Receiver, frame);
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
        private bool IsValidRef(Expr expr, Frame frame)
        {
            return ResolveCellForLValue(expr, frame).Value != null;
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
