using System;
using System.Collections.Generic;
using System.Linq;
using xStunit.Runner;

namespace xStunit.Interpreter
{
    public sealed partial class Engine
    {
        public void ExecuteStatements(IReadOnlyList<Stmt> statements, Frame frame)
        {
            foreach (var stmt in statements)
                ExecuteStatement(stmt, frame);
        }

        private void ExecuteStatement(Stmt stmt, Frame frame)
        {
            // The frame's "you are here" marker, at statement granularity.
            // Nested bodies (IF/FOR/CASE arms) come back through here with their
            // own line, so the marker always names the innermost statement
            // running; a callee runs against its own Frame, so a call never
            // clobbers its caller's marker. Unconditional on purpose: a
            // statement with no line (Line == 0, hand-built AST) must degrade to
            // "unknown" rather than leave the previous statement's line standing
            // and misreport it.
            frame.CurrentLine = stmt.Line;

            switch (stmt)
            {
                case NoOpStmt:
                    break;
                case AssignStmt assign:
                    SetLValue(assign.Target, Evaluate(assign.Value, frame), frame);
                    break;
                case RefAssignStmt refAssign:
                    BindRef(refAssign.Target, ResolveCellForLValue(refAssign.Value, frame), frame);
                    break;
                case IfStmt ifStmt:
                    if ((bool)Evaluate(ifStmt.Condition, frame))
                        ExecuteStatements(ifStmt.Then, frame);
                    else
                        ExecuteStatements(ifStmt.Else, frame);
                    break;
                case ExprStmt exprStmt:
                    Evaluate(exprStmt.Call, frame);
                    break;
                case ForStmt forStmt:
                    ExecuteFor(forStmt, frame);
                    break;
                case WhileStmt whileStmt:
                    ExecuteWhile(whileStmt, frame);
                    break;
                case RepeatStmt repeatStmt:
                    ExecuteRepeat(repeatStmt, frame);
                    break;
                case CaseStmt caseStmt:
                    ExecuteCase(caseStmt, frame);
                    break;
                case ExitStmt:
                    throw new LoopExitSignal();
                case ReturnStmt:
                    throw new MethodReturnSignal();
                default:
                    throw new UnsupportedConstructException(
                        stmt.GetType().Name, $"Statement type {stmt.GetType().Name} not supported");
            }
        }

        // Unwind signal for EXIT, caught only by the nearest enclosing
        // FOR/WHILE/REPEAT below, so it passes straight through any IF/CASE it
        // is lexically nested inside without being special-cased there.
        private sealed class LoopExitSignal : Exception
        {
        }

        // Unwind signal for RETURN, caught only at a body's outermost
        // ExecuteBody/ExecuteSuiteBody, so it passes straight through any
        // IF/FOR/WHILE it is lexically nested inside without being
        // special-cased there.
        private sealed class MethodReturnSignal : Exception
        {
        }

        private void ExecuteFor(ForStmt stmt, Frame frame)
        {
            var from = Convert.ToInt32(Evaluate(stmt.From, frame));
            var to = Convert.ToInt32(Evaluate(stmt.To, frame));
            var step = stmt.Step != null ? Convert.ToInt32(Evaluate(stmt.Step, frame)) : 1;

            if (step == 0)
                throw new InvalidOperationException("FOR loop step must not be zero");

            try
            {
                for (var i = from; step > 0 ? i <= to : i >= to; i += step)
                {
                    SetLValue(stmt.Var, i, frame);
                    ExecuteStatements(stmt.Body, frame);
                }
            }
            catch (LoopExitSignal)
            {
            }
        }

        private void ExecuteWhile(WhileStmt stmt, Frame frame)
        {
            try
            {
                while ((bool)Evaluate(stmt.Condition, frame))
                    ExecuteStatements(stmt.Body, frame);
            }
            catch (LoopExitSignal)
            {
            }
        }

        private void ExecuteRepeat(RepeatStmt stmt, Frame frame)
        {
            try
            {
                do
                {
                    ExecuteStatements(stmt.Body, frame);
                } while (!(bool)Evaluate(stmt.Until, frame));
            }
            catch (LoopExitSignal)
            {
            }
        }

        private void ExecuteCase(CaseStmt stmt, Frame frame)
        {
            var selectorInt = ToCaseInt(Evaluate(stmt.Selector, frame));

            foreach (var arm in stmt.Arms)
            {
                if (arm.Labels.Any(label => CaseLabelMatches(label, selectorInt, frame)))
                {
                    ExecuteStatements(arm.Body, frame);
                    return;
                }
            }

            ExecuteStatements(stmt.ElseBody, frame);
        }

        private bool CaseLabelMatches(CaseLabel label, int selectorInt, Frame frame)
        {
            var from = ToCaseInt(Evaluate(label.From, frame));
            if (!label.IsRange)
                return from == selectorInt;

            var to = ToCaseInt(Evaluate(label.To, frame));
            return selectorInt >= from && selectorInt <= to;
        }

        private static void SetVariable(string name, object value, Frame frame)
        {
            var cell = frame.ResolveCell(name);
            if (cell == null)
            {
                cell = new Cell();
                frame.Locals[name] = cell;
            }
            cell.Value = CoerceForAssignment(cell.Value, value, cell.DeclaredTypeName);
        }

        // Assignment-target dispatch: identifiers go through SetVariable (may
        // implicitly declare a local), field/index targets write directly into
        // the already-allocated Cell/array slot they resolve to.
        private void SetLValue(Expr target, object value, Frame frame)
        {
            switch (target)
            {
                case IdentifierExpr id:
                    SetVariable(id.Name, value, frame);
                    break;
                case FieldAccessExpr fieldAccess:
                {
                    if (TryGetGvlFields(fieldAccess, frame, out var gvlFields))
                    {
                        if (!gvlFields.TryGetValue(fieldAccess.FieldName, out var gvlCell))
                            throw new InvalidOperationException($"Unknown field '{fieldAccess.FieldName}'");
                        gvlCell.Value = CoerceForAssignment(gvlCell.Value, value, gvlCell.DeclaredTypeName);
                        break;
                    }

                    var receiverValue = Evaluate(fieldAccess.Receiver, frame);
                    var fields = FieldsOf(receiverValue, fieldAccess.FieldName);
                    if (fields.TryGetValue(fieldAccess.FieldName, out var cell))
                    {
                        cell.Value = CoerceForAssignment(cell.Value, value, cell.DeclaredTypeName);
                        break;
                    }

                    // Same PROPERTY fallback as the read side
                    // (Engine.Expressions.cs's FieldAccessExpr case): an
                    // assignment target that isn't in Fields may instead be a
                    // PROPERTY's Set accessor.
                    if (receiverValue is FbInstance fbReceiver &&
                        TryFindProperty(fbReceiver.ActualTypeName, fieldAccess.FieldName, out var definingType, out var property))
                    {
                        InvokePropertySet(fbReceiver, definingType, property, value);
                        break;
                    }

                    throw new InvalidOperationException($"Unknown field '{fieldAccess.FieldName}'");
                }
                case IndexExpr index:
                {
                    // The receiver is evaluated for its *value*, not resolved
                    // via ResolveCellForLValue, because a FieldAccessExpr
                    // receiver may resolve through a PROPERTY getter, which
                    // ResolveCellForLValue's FieldAccessExpr case doesn't
                    // support. The Cell is resolved only for the STRING branch
                    // below, where System.String's immutability means writing a
                    // byte replaces the parent Cell's Value wholesale.
                    var receiverValue = EvaluateIndexReceiver(index.Receiver, frame);
                    if (receiverValue is string str)
                    {
                        var receiverCell = ResolveCellForLValue(index.Receiver, frame);
                        receiverCell.Value = SetStringByte(str, ResolveStringIndex(index.Indices, frame), Convert.ToInt32(value));
                        break;
                    }

                    var array = (ArrayValue)receiverValue;
                    var flat = FlattenIndex(array, index.Indices, frame);
                    // An array element has no Cell of its own to carry a
                    // declared type, so the element type is taken from the
                    // ARRAY declaration itself.
                    array.SetElement(flat, CoerceForAssignment(array.Elements[flat], value, array.ElementTypeName));
                    break;
                }
                default:
                    throw new UnsupportedConstructException(
                        target.GetType().Name, $"Assignment target {target.GetType().Name} not supported");
            }
        }

        // Mirrors SetLValue's target shapes (identifier / field access / array
        // index), but a REF= aliases the destination's storage onto sourceCell
        // rather than copying a value into it.
        //
        // Identifier and field-access targets live in a Dictionary<string, Cell>
        // (Locals, instance Fields, struct Fields, GVL fields), so true aliasing
        // means replacing the dictionary's Cell entry itself: every later read
        // of that name resolves straight to sourceCell, and a write through it
        // mutates sourceCell.Value, which is what the REF='s RHS still points
        // at.
        //
        // An array-index target cannot receive a swapped Cell, because
        // ArrayValue.Elements holds raw values rather than Cell wrappers. Storing
        // sourceCell.Value into the element still aliases correctly for the
        // motivating case, an array of FB/STRUCT references, since those are CLR
        // reference types and the stored value IS the shared target object - but
        // unlike the dictionary-backed targets it will not propagate a later
        // write back to a scalar REFERENCE TO's original variable.
        private void BindRef(Expr target, Cell sourceCell, Frame frame)
        {
            switch (target)
            {
                case IdentifierExpr id:
                    // Method-local REFERENCE TO vars are pre-populated into
                    // frame.Locals by BindParams, so a hit there means the
                    // target really is local. A name that instead belongs to an
                    // instance field has to be written through to
                    // instance.Fields, or the binding dies with this per-call
                    // Frame instead of persisting across calls.
                    if (!frame.Locals.ContainsKey(id.Name) &&
                        frame.Instance != null &&
                        frame.Instance.Fields.ContainsKey(id.Name))
                        frame.Instance.Fields[id.Name] = sourceCell;
                    else
                        frame.Locals[id.Name] = sourceCell;
                    break;

                case FieldAccessExpr fieldAccess:
                {
                    if (TryGetGvlFields(fieldAccess, frame, out var gvlFields))
                    {
                        if (!gvlFields.ContainsKey(fieldAccess.FieldName))
                            throw new InvalidOperationException($"Unknown field '{fieldAccess.FieldName}'");
                        gvlFields[fieldAccess.FieldName] = sourceCell;
                        break;
                    }

                    var fields = FieldsOf(Evaluate(fieldAccess.Receiver, frame), fieldAccess.FieldName);
                    if (!fields.ContainsKey(fieldAccess.FieldName))
                        throw new InvalidOperationException($"Unknown field '{fieldAccess.FieldName}'");
                    fields[fieldAccess.FieldName] = sourceCell;
                    break;
                }

                case IndexExpr index:
                {
                    var array = (ArrayValue)Evaluate(index.Receiver, frame);
                    var flat = FlattenIndex(array, index.Indices, frame);
                    array.SetElement(flat, sourceCell.Value);
                    break;
                }

                default:
                    throw new UnsupportedConstructException(
                        target.GetType().Name, $"REF= target {target.GetType().Name} not supported");
            }
        }

        // Row-major flattening against the array's declared per-dimension
        // lo..hi bounds, matching ArrayTypeInfo's dimension order.
        private int FlattenIndex(ArrayValue array, IReadOnlyList<Expr> indexExprs, Frame frame)
        {
            if (indexExprs.Count != array.Dimensions.Count)
                throw new InvalidOperationException(
                    $"Array has {array.Dimensions.Count} dimension(s) but {indexExprs.Count} index/indices given");

            var flat = 0;
            for (var d = 0; d < array.Dimensions.Count; d++)
            {
                var (lo, hi) = array.Dimensions[d];
                // Convert.ToInt32 rather than a direct (int) cast: an index
                // expression can evaluate to a boxed long (DINT/UDINT/LINT box
                // as long per NumericCoercion) or uint (TIME/DATE), and an
                // unboxing cast throws InvalidCastException on those instead of
                // narrowing them.
                var idx = Convert.ToInt32(Evaluate(indexExprs[d], frame));
                if (idx < lo || idx > hi)
                    throw new IndexOutOfRangeException($"Array index {idx} out of bounds [{lo}..{hi}] in dimension {d}");

                var dimSize = hi - lo + 1;
                flat = flat * dimSize + (idx - lo);
            }
            return flat;
        }

        private static object CoerceForAssignment(object existing, object incoming, string declaredTypeName) =>
            NumericCoercion.CoerceForAssignment(existing, incoming, declaredTypeName);

        // TwinCAT ST extension: a STRING can be indexed directly (s[n],
        // 0-based) to read or write individual bytes, most commonly
        // "IF s[0] = 0 THEN" to test for an empty string, since a STRING is a
        // null-terminated byte buffer internally.
        //
        // Bounds are the string's *current* content, not its declared capacity:
        // index == Length reads or writes the terminator one past the last
        // character, and anything beyond that is out of range. A write that
        // appends is still capped by the declared capacity, which the parent
        // Cell applies when the rebuilt string lands back on it.
        private int ResolveStringIndex(IReadOnlyList<Expr> indexExprs, Frame frame)
        {
            if (indexExprs.Count != 1)
                throw new InvalidOperationException($"STRING indexing takes exactly one index, got {indexExprs.Count}");
            return Convert.ToInt32(Evaluate(indexExprs[0], frame));
        }

        // Shared by GetStringByte/SetStringByte and StringByteCell (the
        // REF=/ADR()/Transmit() counterpart in Engine.Cells.cs).
        internal static void ValidateStringIndex(string str, int idx)
        {
            if (idx < 0 || idx > str.Length)
                throw new IndexOutOfRangeException($"String index {idx} out of bounds [0..{str.Length}]");
        }

        internal static int GetStringByte(string str, int idx)
        {
            ValidateStringIndex(str, idx);
            return idx < str.Length ? NarrowStringByte.FromChar(str[idx]) : 0;
        }

        // byteValue == 0 truncates at idx (writing the terminator early,
        // TwinCAT's idiomatic way to shorten a string in place); a non-zero
        // byte either replaces the character at idx or, when idx is exactly
        // one past the current content, appends a new character.
        internal static string SetStringByte(string str, int idx, int byteValue)
        {
            ValidateStringIndex(str, idx);
            if (byteValue == 0)
                return str.Substring(0, idx);

            var ch = NarrowStringByte.ToChar(byteValue);
            return idx < str.Length
                ? str.Substring(0, idx) + ch + str.Substring(idx + 1)
                : str + ch;
        }
    }
}
