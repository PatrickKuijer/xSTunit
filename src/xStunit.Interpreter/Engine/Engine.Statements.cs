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

        private void SetVariable(string name, object value, Frame frame)
        {
            var cell = frame.ResolveCell(name);
            if (cell == null && !TryResolveGlobalCell(name, out cell))
                throw new InvalidOperationException($"Unknown variable '{name}'");

            var declaredTypeName = ShapingTypeName(ResolveDeclaredTypeOfName(name, frame) ?? cell.DeclaredTypeName, cell);
            value = ShapeLiteral(value, declaredTypeName, frame, name);
            RejectArrayShapeMismatch(cell, value, name);
            AssignToCell(cell, value);
        }

        // Assignment-target dispatch: identifiers go through SetVariable, which
        // requires the name to resolve; field/index targets write directly into
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
                        var gvlName = ((IdentifierExpr)fieldAccess.Receiver).Name;
                        _globalTypeNames[gvlName].TryGetValue(fieldAccess.FieldName, out var gvlType);
                        gvlType = ShapingTypeName(gvlType ?? gvlCell.DeclaredTypeName, gvlCell);
                        value = ShapeLiteral(value, gvlType, frame, TargetName(target));
                        RejectArrayShapeMismatch(gvlCell, value, TargetName(target));
                        AssignToCell(gvlCell, value);
                        break;
                    }

                    var receiverValue = Evaluate(fieldAccess.Receiver, frame);
                    RejectAssignmentIntoEmptyVariable(fieldAccess.Receiver, receiverValue);
                    var fields = FieldsOf(receiverValue, fieldAccess.FieldName);
                    if (fields.TryGetValue(fieldAccess.FieldName, out var cell))
                    {
                        var fieldType = ShapingTypeName(DeclaredFieldTypeName(receiverValue, fieldAccess.FieldName, cell), cell);
                        value = ShapeLiteral(value, fieldType, frame, TargetName(target));
                        RejectArrayShapeMismatch(cell, value, TargetName(target));
                        AssignToCell(cell, value);
                        break;
                    }

                    // Same PROPERTY fallback as the read side
                    // (Engine.Expressions.cs's FieldAccessExpr case): an
                    // assignment target that isn't in Fields may instead be a
                    // PROPERTY's Set accessor.
                    if (receiverValue is FbInstance fbReceiver &&
                        TryFindProperty(fbReceiver.ActualTypeName, fieldAccess.FieldName, out var definingType, out var property))
                    {
                        var propertyType = _registry.GetReturnTypeName(property.DeclarationText);
                        value = ShapeLiteral(value, propertyType, frame, TargetName(target));
                        InvokePropertySet(fbReceiver, definingType, property, CopyValue(value));
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
                    RejectAssignmentIntoEmptyVariable(index.Receiver, receiverValue);
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
                    var existingElement = array.Elements[flat];
                    value = ShapeLiteral(value, array.ElementTypeName, frame, TargetName(target));
                    array.SetElement(
                        flat,
                        CoerceForAssignment(
                            existingElement,
                            StoredValue(existingElement, value, array.ElementTypeName),
                            array.ElementTypeName));
                    break;
                }
                default:
                    throw new UnsupportedConstructException(
                        target.GetType().Name, $"Assignment target {target.GetType().Name} not supported");
            }
        }

        // A struct/array literal evaluates to an untyped value (StructInstance
        // with no TypeName, ArrayValue with no ElementTypeName) because the
        // expression alone knows no declared type. Assignment is where the
        // target's declared type is known, so the literal is rebuilt here over
        // that type's defaults, recursing through nested literals.
        private object ShapeLiteral(object value, string declaredTypeName, Frame frame, string targetName)
        {
            if (declaredTypeName == null)
                return value;

            var resolved = _registry.ResolveAlias(declaredTypeName);
            switch (value)
            {
                case StructInstance literal when literal.TypeName == null:
                {
                    var structAst = _registry.GetStruct(resolved);
                    if (structAst == null)
                        return value;

                    var filled = BuildStructDefault(structAst, null, frame.Instance);
                    foreach (var pair in literal.Fields)
                    {
                        if (!filled.Fields.TryGetValue(pair.Key, out var fieldCell))
                            throw new InvalidOperationException($"Unknown field '{pair.Key}' on struct '{filled.TypeName}'");

                        var fieldType = filled.FieldTypeNames.TryGetValue(pair.Key, out var declared) ? declared : fieldCell.DeclaredTypeName;
                        fieldCell.Value = CoerceForAssignment(
                            fieldCell.Value,
                            ShapeLiteral(pair.Value.Value, fieldType, frame, targetName + "." + pair.Key),
                            fieldType);
                    }

                    return filled;
                }
                case ArrayValue literal when literal.ElementTypeName == null:
                {
                    if (!ArrayTypeInfo.IsArrayType(resolved) || ArrayTypeInfo.IsOpenArrayType(resolved))
                        return value;

                    var filled = DeclaredDefault.NewArray(
                        _registry,
                        resolved,
                        boundText => ResolveArrayBound(boundText, frame.Instance),
                        elementDecl => DefaultValue(elementDecl, frame.Instance));
                    if (literal.Elements.Length > filled.Elements.Length)
                        throw new InvalidOperationException(
                            $"Array literal of {literal.Elements.Length} elements does not fit '{targetName}' ({DescribeShape(filled)})");

                    for (var i = 0; i < literal.Elements.Length; i++)
                        filled.SetElement(
                            i,
                            CoerceForAssignment(
                                filled.Elements[i],
                                ShapeLiteral(literal.Elements[i], filled.ElementTypeName, frame, targetName + "[...]"),
                                filled.ElementTypeName));
                    return filled;
                }
                default:
                    return value;
            }
        }

        // The type a literal is shaped against and an array assignment is
        // checked against: a REFERENCE TO declaration stands for its target's
        // type, and an open-bound ARRAY[*] stands for the concrete bounds of
        // the array currently bound to the cell.
        private static string ShapingTypeName(string declaredTypeName, Cell cell)
        {
            if (declaredTypeName == null)
                return null;

            var typeName = ReferenceToPrefix.Replace(declaredTypeName, "");
            if (!ArrayTypeInfo.IsOpenArrayType(typeName) || !(cell.Value is ArrayValue bound))
                return typeName;

            if (!ArrayTypeInfo.TryGetElementTypeName(typeName, out var elementTypeName))
                return typeName;

            var bounds = string.Join(",", bound.Dimensions.Select(d => $"{d.Lo}..{d.Hi}"));
            return $"ARRAY[{bounds}] OF {elementTypeName}";
        }

        private static readonly System.Text.RegularExpressions.Regex ReferenceToPrefix =
            new System.Text.RegularExpressions.Regex(
                @"^\s*REFERENCE\s+TO\s+",
                System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static void RejectArrayShapeMismatch(Cell cell, object value, string targetName)
        {
            if (!(cell.Value is ArrayValue target) || !(value is ArrayValue source))
                return;

            if (target.Dimensions.SequenceEqual(source.Dimensions))
                return;

            throw new InvalidOperationException(
                $"Cannot assign {DescribeShape(source)} to '{targetName}' declared {DescribeShape(target)}");
        }

        private static string DescribeShape(ArrayValue array) =>
            "ARRAY[" + string.Join(",", array.Dimensions.Select(d => $"{d.Lo}..{d.Hi}")) + "]";

        private static string TargetName(Expr target)
        {
            switch (target)
            {
                case IdentifierExpr id:
                    return id.Name;
                case ThisRefExpr _:
                    return "THIS^";
                case SuperRefExpr _:
                    return "SUPER^";
                case DerefExpr deref:
                    return TargetName(deref.Inner) + "^";
                case FieldAccessExpr field:
                    return TargetName(field.Receiver) + "." + field.FieldName;
                case IndexExpr index:
                    return TargetName(index.Receiver) + "[...]";
                default:
                    return "assignment target";
            }
        }

        private static string DeclaredFieldTypeName(object receiver, string fieldName, Cell cell)
        {
            switch (receiver)
            {
                case FbInstance fb when fb.FieldTypeNames.TryGetValue(fieldName, out var fbType):
                    return fbType;
                case StructInstance st when st.FieldTypeNames.TryGetValue(fieldName, out var structType):
                    return structType;
                default:
                    return cell.DeclaredTypeName;
            }
        }

        private static void RejectAssignmentIntoEmptyVariable(Expr receiver, object receiverValue)
        {
            if (receiverValue == null && receiver is IdentifierExpr id)
            {
                throw new InvalidOperationException(
                    $"Cannot assign into a member of '{id.Name}': it holds no value yet.");
            }
        }

        private bool TryRebindGlobal(string name, Cell sourceCell)
        {
            foreach (var fields in _globals.Values)
            {
                if (fields.ContainsKey(name))
                {
                    fields[name] = sourceCell;
                    return true;
                }
            }

            return false;
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
                    // A name that belongs to an instance field has to be written
                    // through to instance.Fields, or the binding dies with this
                    // per-call Frame instead of persisting across calls. A bare GVL
                    // member rebinds the global.
                    if (frame.Locals.ContainsKey(id.Name))
                        frame.RebindLocal(id.Name, sourceCell);
                    else if (frame.Instance != null && frame.Instance.Fields.ContainsKey(id.Name))
                        frame.Instance.Fields[id.Name] = sourceCell;
                    else if (!TryRebindGlobal(id.Name, sourceCell))
                        throw new InvalidOperationException($"Unknown variable '{id.Name}'");
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
