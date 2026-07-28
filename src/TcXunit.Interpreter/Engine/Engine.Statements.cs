using System;
using System.Collections.Generic;
using System.Linq;

namespace TcXunit.Interpreter
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
            switch (stmt)
            {
                case AssignStmt assign:
                    SetLValue(assign.Target, Evaluate(assign.Value, frame), frame);
                    break;
                case RefAssignStmt refAssign:
                    // Method-local REFERENCE TO vars are pre-populated into
                    // frame.Locals by BindParams, so a hit there means the
                    // target is genuinely local. Otherwise, if it names an
                    // instance field (e.g. declared in the FB's VAR block),
                    // write through to instance.Fields so the binding
                    // persists across calls (TcXunit-t6p) - Frame is
                    // per-call and would otherwise silently drop it.
                    if (!frame.Locals.ContainsKey(refAssign.TargetName) &&
                        frame.Instance != null &&
                        frame.Instance.Fields.ContainsKey(refAssign.TargetName))
                        frame.Instance.Fields[refAssign.TargetName] = ResolveCellForLValue(refAssign.Value, frame);
                    else
                        frame.Locals[refAssign.TargetName] = ResolveCellForLValue(refAssign.Value, frame);
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
                    throw new NotSupportedException($"Statement type {stmt.GetType().Name} not supported");
            }
        }

        // Internal unwind signal for EXIT (TcXunit-mym.3): caught only by the
        // nearest enclosing FOR/WHILE/REPEAT's try/catch below, so it passes
        // straight through any IF/CASE it's lexically nested inside without
        // being special-cased there.
        private sealed class LoopExitSignal : Exception
        {
        }

        // Internal unwind signal for RETURN (TcXunit-mym.2): caught only at
        // CallMethod's and RunSuite's top-level ExecuteStatements call, so it
        // passes straight through any IF/FOR/WHILE/etc it's lexically nested
        // inside without being special-cased there.
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
                    SetVariable(stmt.VarName, i, frame);
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
            cell.Value = CoerceForAssignment(cell.Value, value);
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
                        gvlCell.Value = CoerceForAssignment(gvlCell.Value, value);
                        break;
                    }

                    var receiverValue = Evaluate(fieldAccess.Receiver, frame);
                    var fields = FieldsOf(receiverValue);
                    if (fields.TryGetValue(fieldAccess.FieldName, out var cell))
                    {
                        cell.Value = CoerceForAssignment(cell.Value, value);
                        break;
                    }

                    // Same PROPERTY fallback as the read side
                    // (Engine.Expressions.cs's FieldAccessExpr case,
                    // TcXunit-sxv): a plain field assignment target that
                    // isn't in Fields may instead be a PROPERTY's Set
                    // accessor.
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
                    var array = (ArrayValue)Evaluate(index.Receiver, frame);
                    var flat = FlattenIndex(array, index.Indices, frame);
                    array.Elements[flat] = CoerceForAssignment(array.Elements[flat], value);
                    break;
                }
                default:
                    throw new NotSupportedException($"Assignment target {target.GetType().Name} not supported");
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
                // Convert.ToInt32 rather than a direct (int) cast: index
                // expressions can evaluate to a boxed long (DINT/UDINT/LINT-
                // typed index variables box as long per NumericCoercion) or
                // uint (TIME/DATE-typed, unlikely but possible), and a direct
                // (int) cast throws InvalidCastException on those boxed
                // types instead of narrowing them (real-usage find, TcXunit-
                // iyd.5).
                var idx = Convert.ToInt32(Evaluate(indexExprs[d], frame));
                if (idx < lo || idx > hi)
                    throw new IndexOutOfRangeException($"Array index {idx} out of bounds [{lo}..{hi}] in dimension {d}");

                var dimSize = hi - lo + 1;
                flat = flat * dimSize + (idx - lo);
            }
            return flat;
        }

        // Delegates to NumericCoercion (TcXunit-6af.2), the shared promotion/
        // narrowing rule also used by Engine.Expressions.cs's EvaluateBinary -
        // this used to reimplement the same rule independently by inspecting
        // the CLR type already sitting in the Cell.
        private static object CoerceForAssignment(object existing, object incoming) =>
            NumericCoercion.CoerceForAssignment(existing, incoming);
    }
}
