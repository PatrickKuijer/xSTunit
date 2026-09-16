using System;
using System.Collections.Generic;
using System.Linq;
using xStunit.Runner;

namespace xStunit.Interpreter
{
    public sealed partial class Engine
    {
        public object Evaluate(Expr expr, Frame frame)
        {
            switch (expr)
            {
                case IntLiteralExpr i:
                    return i.Value;
                case LintLiteralExpr l:
                    return l.Value;
                case UlintLiteralExpr ul:
                    return ul.Value;
                case RealLiteralExpr r:
                    return r.Value;
                case LrealLiteralExpr lr:
                    return lr.Value;
                case TimeLiteralExpr t:
                    return t.Value;
                case LtimeLiteralExpr lt:
                    return lt.Value;
                case DateLiteralExpr d:
                    return d.Value;
                case DateAndTimeLiteralExpr dt:
                    return dt.Value;
                case TimeOfDayLiteralExpr tod:
                    return tod.Value;
                case StringLiteralExpr s:
                    return s.Value;
                case BoolLiteralExpr b:
                    return b.Value;
                case IdentifierExpr id:
                {
                    var cell = frame.ResolveCell(id.Name);
                    if (cell == null)
                    {
                        if (!TryResolveGlobalCell(id.Name, out cell))
                            throw new InvalidOperationException($"Unknown variable '{id.Name}'");
                        NoteGlobalRead(cell);
                    }

                    return cell.Value;
                }
                case ThisRefExpr:
                    return frame.Instance;
                case SuperRefExpr:
                    return frame.Instance;
                case DerefExpr deref:
                {
                    // The pointee is reached without passing the identifier or
                    // GvlName.field branches, so this is where a pointer-
                    // mediated read of a global gets noted.
                    var target = ((Pointer)Evaluate(deref.Inner, frame)).Target;
                    NoteGlobalRead(target);
                    return target.Value;
                }
                case IndexExpr index:
                {
                    var receiverValue = Evaluate(index.Receiver, frame);
                    if (receiverValue is string str)
                        return GetStringByte(str, ResolveStringIndex(index.Indices, frame));

                    var array = (ArrayValue)receiverValue;
                    return array.Elements[FlattenIndex(array, index.Indices, frame)];
                }
                case FieldAccessExpr fieldAccess:
                {
                    // Type.Member where Type names a built-in enum rather
                    // than a variable (e.g. TcEventSeverity.Warning) -
                    // resolve the member's underlying value directly,
                    // bypassing the variable/field lookup below (which
                    // would otherwise throw on the receiver identifier).
                    if (fieldAccess.Receiver is IdentifierExpr enumTypeId &&
                        frame.ResolveCell(enumTypeId.Name) == null &&
                        BuiltinEnums.Types.TryGetValue(enumTypeId.Name, out var enumMembers))
                    {
                        if (!enumMembers.TryGetValue(fieldAccess.FieldName, out var enumValue))
                            throw new InvalidOperationException($"Unknown enum member '{enumTypeId.Name}.{fieldAccess.FieldName}'");
                        return enumValue;
                    }

                    // The same shape for a user-defined ENUM DUT, so it resolves
                    // exactly as a built-in enum does.
                    if (fieldAccess.Receiver is IdentifierExpr dutEnumTypeId &&
                        frame.ResolveCell(dutEnumTypeId.Name) == null &&
                        _registry.TryGetEnumMembers(dutEnumTypeId.Name, out var dutEnumMembers))
                    {
                        if (!dutEnumMembers.TryGetValue(fieldAccess.FieldName, out var dutEnumValue))
                            throw new InvalidOperationException($"Unknown enum member '{dutEnumTypeId.Name}.{fieldAccess.FieldName}'");
                        return dutEnumValue;
                    }

                    // And again for GvlName.field, where GvlName is a registered
                    // GVL rather than a variable in scope.
                    if (TryGetGvlFields(fieldAccess, frame, out var gvlFields))
                    {
                        if (!gvlFields.TryGetValue(fieldAccess.FieldName, out var gvlCell))
                            throw new InvalidOperationException($"Unknown field '{fieldAccess.FieldName}'");

                        NoteGlobalRead(gvlCell);
                        return gvlCell.Value;
                    }

                    var receiverValue = Evaluate(fieldAccess.Receiver, frame);
                    var fields = FieldsOf(receiverValue, fieldAccess.FieldName);
                    if (fields.TryGetValue(fieldAccess.FieldName, out var cell))
                        return cell.Value;

                    // No VAR-block field matches, but a PROPERTY is never
                    // materialized into Fields at NewInstance time, so a miss
                    // here falls back to running its Get accessor rather than
                    // reporting "Unknown field".
                    if (receiverValue is FbInstance fbReceiver &&
                        TryFindProperty(fbReceiver.ActualTypeName, fieldAccess.FieldName, out var definingType, out var property))
                        return InvokePropertyGet(fbReceiver, definingType, property);

                    throw new InvalidOperationException($"Unknown field '{fieldAccess.FieldName}'");
                }
                case StructLiteralExpr structLit:
                {
                    // No declared struct type is known in a bare expression
                    // context, so this builds an untyped instance straight
                    // from the given fields (no default-merge) - the typed,
                    // default-merging path is BuildStructDefault, used when
                    // a VarDecl's declared type is a known StructAst.
                    var instance = new StructInstance(null);
                    foreach (var init in structLit.FieldInits)
                        instance.Fields[init.Name] = new Cell { Value = Evaluate(init.Value, frame) };
                    return instance;
                }
                case ArrayLiteralExpr arrayLit:
                {
                    // No declared bounds are known in a bare expression
                    // context, so this defaults to a single 0-based
                    // dimension sized to the literal - BuildArrayDefault is
                    // the typed path that overlays onto declared bounds.
                    var elements = arrayLit.Elements.Select(e => Evaluate(e, frame)).ToArray();
                    return new ArrayValue(
                        new List<(int, int)> { (0, elements.Length - 1) }, null, elements, Cell.Unbounded);
                }
                case BinaryExpr binary:
                    return EvaluateBinary(binary, frame);
                case UnaryExpr unary:
                    return EvaluateUnary(unary, frame);
                case CallExpr call:
                    return EvaluateCall(call, frame);
                default:
                    throw new UnsupportedConstructException(
                        expr.GetType().Name, $"Expression type {expr.GetType().Name} not supported");
            }
        }

        private object EvaluateUnary(UnaryExpr unary, Frame frame)
        {
            var value = Evaluate(unary.Operand, frame);

            if (unary.Op == "NOT")
            {
                if (value is bool b)
                    return !b;
                if (value is int i)
                    return ~i;
                throw new NotSupportedException($"Operator 'NOT' requires a BOOL or INT operand, got {value?.GetType().Name}");
            }

            if (unary.Op == "-")
            {
                switch (value)
                {
                    case int i: return -i;
                    case long l: return -l;
                    // The one negatable ulong: long.MinValue's magnitude is
                    // one past long.MaxValue, so a literal can only spell it
                    // unsigned, and '-9223372036854775808' would otherwise be
                    // the single LINT value no source text could produce.
                    case ulong u when u == (ulong)long.MaxValue + 1: return long.MinValue;
                    case float f: return -f;
                    case double d: return -d;
                    default:
                        throw new NotSupportedException($"Unary '-' requires a numeric operand, got {value?.GetType().Name}");
                }
            }

            throw new UnsupportedConstructException(unary.Op, $"Unary operator '{unary.Op}' not supported");
        }

        private object EvaluateBinary(BinaryExpr binary, Frame frame)
        {
            // Dispatched before either operand is touched: unlike every other
            // BinaryExpr below, AND_THEN/OR_ELSE must not evaluate their RHS
            // eagerly. See EvaluateShortCircuit.
            if (binary.Op == "AND_THEN" || binary.Op == "OR_ELSE")
                return EvaluateShortCircuit(binary, frame);

            var leftVal = Evaluate(binary.Left, frame);
            var rightVal = Evaluate(binary.Right, frame);

            if ((binary.Op == "+" || binary.Op == "-") && (leftVal is Pointer || rightVal is Pointer))
                return EvaluatePointerArithmetic(binary.Op, leftVal, rightVal, frame);

            if ((binary.Op == "=" || binary.Op == "<>") &&
                (leftVal is Pointer || rightVal is Pointer || leftVal is FbInstance || rightVal is FbInstance ||
                 leftVal is UnassignedInterfaceReference || rightVal is UnassignedInterfaceReference ||
                 leftVal == null || rightVal == null))
                return EvaluatePointerEquality(binary.Op, leftVal, rightVal);

            if (binary.Op == "AND" || binary.Op == "OR" || binary.Op == "XOR")
                return EvaluateBitstring(binary.Op, leftVal, rightVal);

            if (binary.Op == "MOD")
                return EvaluateMod(leftVal, rightVal);

            // BOOL only supports equality/inequality in IEC 61131-3 (no
            // ordering, no arithmetic) - handle it here so it doesn't fall
            // through to the int cast below.
            if (leftVal is bool lbEq && rightVal is bool rbEq && (binary.Op == "=" || binary.Op == "<>"))
                return binary.Op == "=" ? lbEq == rbEq : lbEq != rbEq;

            // Any other BOOL usage - BOOL mixed with a non-BOOL operand
            // (TRUE = 1), or an operator with no BOOL semantics (TRUE < FALSE) -
            // has no valid IEC 61131-3 meaning, and would otherwise reach the
            // int-cast path below and throw a bare InvalidCastException.
            if (leftVal is bool || rightVal is bool)
                throw new NotSupportedException(
                    $"Operator '{binary.Op}' is not supported between {leftVal?.GetType().Name ?? "null"} and " +
                    $"{rightVal?.GetType().Name ?? "null"}; BOOL only supports '=' and '<>' against another BOOL");

            // STRING and WSTRING are both boxed System.String here
            // (StringTypeInfo tells them apart only at declaration time) and
            // have no numeric representation. IEC 61131-3 defines ordering for
            // ANY_STRING as ordinal comparison - TwinCAT compares string data
            // byte-by-byte - so all six operators are valid, unlike the BOOL
            // guard above.
            if (leftVal is string ls && rightVal is string rs)
                return binary.Op switch
                {
                    "=" => ls == rs,
                    "<>" => ls != rs,
                    "<" => string.CompareOrdinal(ls, rs) < 0,
                    ">" => string.CompareOrdinal(ls, rs) > 0,
                    "<=" => string.CompareOrdinal(ls, rs) <= 0,
                    ">=" => string.CompareOrdinal(ls, rs) >= 0,
                    _ => throw new NotSupportedException(
                        $"Operator '{binary.Op}' is not supported between STRING operands"),
                };

            // Mismatched STRING/non-STRING (e.g. sVal = 1) has no valid
            // IEC 61131-3 semantics; guard here for a descriptive message
            // instead of falling through to the int-cast numeric path.
            if (leftVal is string || rightVal is string)
                throw new NotSupportedException(
                    $"Operator '{binary.Op}' is not supported between {leftVal?.GetType().Name ?? "null"} and " +
                    $"{rightVal?.GetType().Name ?? "null"}; STRING can only be compared against another STRING");

            // INT->LONG->REAL->LREAL implicit widening: promote both operands to
            // their common arithmetic type, per TwinCAT's "smaller to larger is
            // implicit" arithmetic promotion rule.
            var (promotedLeft, promotedRight) = NumericCoercion.Promote(leftVal, rightVal);
            return promotedLeft switch
            {
                double dl => EvaluateNumeric(binary.Op, dl, (double)promotedRight),
                float fl => EvaluateNumeric(binary.Op, fl, (float)promotedRight),
                long ll => EvaluateNumeric(binary.Op, ll, (long)promotedRight),
                ulong ul => EvaluateNumeric(binary.Op, ul, (ulong)promotedRight),
                int il => EvaluateNumeric(binary.Op, il, (int)promotedRight),
                _ => throw new NotSupportedException($"Cannot use {promotedLeft?.GetType().Name} in numeric arithmetic"),
            };
        }

        // ADR(x) +/- offset. The offset moves in whole array elements, not raw
        // bytes: exact byte arithmetic when the pointee is a BYTE/SINT/USINT
        // array - the buffer-packing case MEMCPY/MEMSET/MEMMOVE exist for - and
        // an approximation for wider element types.
        //
        // A pointer at an array element (including ADR(arr), which decays to
        // element 0) steps on the real backing ArrayValue. A pointer to a scalar
        // or whole STRUCT has no element to step through, so its Cell is packed
        // into a synthetic BYTE-array view and stepped through that, crossing
        // STRUCT field and array-of-struct boundaries the way SIZEOF's layout
        // math does. That snapshot is good only for dereferencing: ptr^ := is
        // not a supported assignment target (Parser.RequireLValue), so there is
        // no live backing store to write through.
        private object EvaluatePointerArithmetic(string op, object leftVal, object rightVal, Frame frame)
        {
            if (op == "-" && leftVal is Pointer && rightVal is Pointer)
                throw new UnsupportedConstructException("-", "Pointer-minus-pointer is not supported");

            var (ptr, offsetVal) = leftVal is Pointer p ? (p, rightVal) : ((Pointer)rightVal, leftVal);
            // Convert.ToInt32 rather than a direct (int) cast, for the same
            // reason as FlattenIndex: the offset is an arbitrary user
            // expression, and a DINT/UDINT/LINT/ULINT/DWORD one boxes as long
            // (e.g. the UDINT loop index in the
            // 'FOR i := 0 TO inSize - 1 DO (ipA + i)^' buffer-compare idiom),
            // which an unboxing cast rejects rather than narrows.
            var delta = Convert.ToInt32(offsetVal);
            if (op == "-")
                delta = -delta;

            if (!(ptr.Target is ArrayElementCell aec))
            {
                if (ptr.Target.DeclaredTypeName == null)
                    throw new NotSupportedException(
                        "Pointer arithmetic (ADR(x) +/- offset) is only supported when the pointer targets an " +
                        "array element (e.g. ADR(byteBuf) or ADR(byteBuf[i])) or a variable/field with a known " +
                        "declared type; byte-offset into an untyped Cell isn't modeled.");

                var typeName = _registry.ResolveAlias(ptr.Target.DeclaredTypeName);
                var (view, _) = PackCellToByteView(ptr.Target, typeName, frame);
                aec = new ArrayElementCell(view, 0);
            }

            var newIndex = aec.Index + delta;
            if (newIndex < 0 || newIndex >= aec.Array.Elements.Length)
                throw new IndexOutOfRangeException(
                    $"Pointer arithmetic moved index to {newIndex}, out of bounds [0..{aec.Array.Elements.Length - 1}]");

            return new Pointer(new ArrayElementCell(aec.Array, newIndex));
        }

        // The IEC 61131-3 null-pointer-check idiom is 'IF ipSrc = 0 THEN', which
        // straddles two representations: an unbound POINTER TO x Cell holds C#
        // null (see DefaultValue), never int 0, while the literal on the other
        // side is an int. Two ADR-bound pointers compare by target-Cell
        // identity, and a bound pointer is never null or zero, so it compares
        // unequal to both.
        //
        // An interface-typed (or plain FB-reference) variable follows the same
        // "= 0 means unassigned" idiom - 'IF (iipHandler <> 0) AND
        // iipHandler.bDoWork(...) THEN'. Once assigned, either kind holds the
        // concrete FB's FbInstance directly, so FbInstance gets the same
        // null-check semantics as Pointer, with two assigned interface
        // variables comparing by referenced-instance identity.
        //
        // Unassigned, the two kinds are spelled differently and both have to be
        // recognized here: a variable of a LOADED interface type holds an
        // UnassignedInterfaceReference, while one whose type name no .TcIO
        // declared still falls through DefaultValue to int 0 and reaches this
        // method only when the other operand forces it to.
        private static object EvaluatePointerEquality(string op, object leftVal, object rightVal)
        {
            bool equal;
            if (leftVal is Pointer leftPtr && rightVal is Pointer rightPtr)
                equal = PointerTargetsEqual(leftPtr.Target, rightPtr.Target);
            else if (leftVal is FbInstance leftFb && rightVal is FbInstance rightFb)
                equal = ReferenceEquals(leftFb, rightFb);
            else if (leftVal is UnassignedInterfaceReference && rightVal is UnassignedInterfaceReference)
                equal = true;
            else if (leftVal is UnassignedInterfaceReference)
                equal = rightVal == null || IsNumericZero(rightVal);
            else if (rightVal is UnassignedInterfaceReference)
                equal = leftVal == null || IsNumericZero(leftVal);
            else if (leftVal is Pointer || rightVal is Pointer || leftVal is FbInstance || rightVal is FbInstance)
                equal = false;
            else if (leftVal == null && rightVal == null)
                equal = true;
            else if (leftVal == null)
                equal = IsNumericZero(rightVal);
            else
                equal = IsNumericZero(leftVal);

            return op == "=" ? equal : !equal;
        }

        // A pointer compared against any numeric zero - int, REAL or LREAL - is
        // the null-check idiom, whatever the literal's numeric type.
        private static bool IsNumericZero(object val)
        {
            return (val is int i && i == 0)
                || (val is float f && f == 0f)
                || (val is double d && d == 0d);
        }

        // ADR(x) builds a fresh ArrayElementCell on every call, so two pointers
        // to the "same" element are distinct instances and must be compared by
        // backing ArrayValue + Index. ADR(x) on a scalar or struct field returns
        // the actual field Cell, so reference equality is right there.
        private static bool PointerTargetsEqual(Cell left, Cell right)
        {
            if (left is ArrayElementCell leftAec && right is ArrayElementCell rightAec)
                return ReferenceEquals(leftAec.Array, rightAec.Array) && leftAec.Index == rightAec.Index;

            return ReferenceEquals(left, right);
        }

        // The intrinsics (MEMCPY/MEMSET/MEMMOVE, CONCAT) are native, with no
        // VarBlockParser decls for BindParams to reconcile named args against,
        // so this does that binding for them: each declared param by name first
        // (MEMCPY(destAddr := ipDst, srcAddr := ipSrc, inSrcSize)), with
        // PositionalArgs consumed left-to-right only for the params not named.
        private static IReadOnlyDictionary<string, Expr> ResolveIntrinsicArgs(
            IReadOnlyList<string> paramNamesInDeclOrder,
            IReadOnlyList<Expr> positionalArgs,
            IReadOnlyList<NamedArg> namedArgs)
        {
            var resolved = new Dictionary<string, Expr>();
            var posIndex = 0;
            foreach (var paramName in paramNamesInDeclOrder)
            {
                if (ArgBinder.TryResolveArg(
                    paramName,
                    name => namedArgs.FirstOrDefault(a => a.Name == name)?.Value,
                    positionalArgs,
                    ref posIndex,
                    out var value))
                    resolved[paramName] = value;
            }
            return resolved;
        }

        // TwinCAT's Tc2_Standard CONCAT signature: STR1/STR2 required,
        // STR3..STR10 optional. Appended in this declared order however the
        // caller mixed named and positional args.
        private static readonly string[] ConcatParamNames =
            { "STR1", "STR2", "STR3", "STR4", "STR5", "STR6", "STR7", "STR8", "STR9", "STR10" };

        private static readonly string[] AdvanceClockParamNames = { "Duration" };

        // IEC 61131-3's ABS operator names its single input IN, so
        // ABS(IN := x) binds the same way ABS(x) does.
        private static readonly string[] AbsParamNames = { "IN" };

        // Tc2_System's TestAndSet names its VAR_IN_OUT operand Lock.
        private static readonly string[] TestAndSetParamNames = { "Lock" };

        // Tc2_System's TestAndSet: answers with the operand's PRIOR value and
        // leaves it TRUE, so the first caller sees FALSE (it took the lock) and
        // every later one sees TRUE until the holder clears the flag.
        //
        // An intrinsic rather than a native-function plugin, despite being a
        // compiled-only library function like F_CheckSum16: the plugin contract
        // hands an implementation evaluated argument VALUES, and this one's
        // entire purpose is to write its VAR_IN_OUT operand back. Resolving the
        // Cell is what makes the write land where the next reader looks, and
        // only the engine can do that.
        //
        // Atomicity needs no modelling here. The interpreter runs one PLC task
        // on one thread, so the read and the write cannot be torn apart by
        // anything the code under test can observe - which is exactly the
        // guarantee the real primitive buys on hardware.
        private object TestAndSet(Expr lockExpr, Frame frame)
        {
            var cell = ResolveCellForLValue(lockExpr, frame);
            var priorValue = Convert.ToBoolean(cell.Value);
            cell.Value = true;
            return priorValue;
        }

        // ABS is overloaded over ANY_NUM and returns its argument's own type,
        // not a widened one: ABS of a DINT is a DINT, of a REAL a REAL. The
        // interpreter's type model is the CLR box (see IecNumericType), so
        // preserving the type means switching on the box and returning the
        // same shape - never routing everything through double, which would
        // turn an INT result into an LREAL and break the next assignment or
        // assertion.
        //
        // The unsigned tiers (ULINT/LWORD as ulong) are already non-negative,
        // so ABS is identity there. UINT/WORD share the int box with the
        // signed types and UDINT/DWORD share the long box; Math.Abs is a
        // no-op on their always-non-negative values, so one branch per box is
        // correct for both. int.MinValue/long.MinValue have no positive
        // counterpart and Math.Abs throws OverflowException, which surfaces
        // through the normal fault path attributed to the PLC call site -
        // the honest outcome, since the result is not representable.
        private static object EvaluateAbs(object value) => value switch
        {
            double d => (object)Math.Abs(d),
            float f => Math.Abs(f),
            long l => Math.Abs(l),
            ulong ul => ul,
            int i => Math.Abs(i),
            _ => throw new NotSupportedException(
                $"ABS requires a numeric (ANY_NUM) argument, got {value?.GetType().Name}"),
        };

        // The one ST-visible way to move the shared Clock: a suite (or any
        // other interpreted body) calls AdvanceClock(T#100ms) the same way it
        // calls SIZEOF/CONCAT, rather than needing a C# harness between
        // StepCycles calls. Duration's CLR shape already says which unit it
        // is in - TimeLiteral parses TIME to uint (ms) and LTIME to ulong
        // (ns, Clock's own base unit) - so a ulong means nanoseconds and
        // anything else (a TIME literal or a bare integer cycle-style count)
        // means milliseconds.
        private void AdvanceClock(object duration)
        {
            if (duration is ulong ns)
                Clock.AdvanceNs((long)ns);
            else
                Clock.AdvanceMs(Convert.ToInt64(duration));
        }

        private static Expr RequireIntrinsicArg(string methodName, string paramName, IReadOnlyDictionary<string, Expr> args)
        {
            if (args.TryGetValue(paramName, out var value))
                return value;

            throw new InvalidOperationException($"{methodName} missing required argument '{paramName}'");
        }

        private Pointer RequirePointerArg(string methodName, string paramName, IReadOnlyDictionary<string, Expr> args, Frame frame)
        {
            var value = Evaluate(RequireIntrinsicArg(methodName, paramName, args), frame);
            if (!(value is Pointer ptr))
                throw new InvalidOperationException(
                    $"{methodName} argument '{paramName}' must be a POINTER TO BYTE (e.g. ADR(buf) or ADR(buf[i])), got {value?.GetType().Name}");
            return ptr;
        }

        // CONCAT's STR* args are ANY_STRING per the IEC signature, so a
        // non-string arg is a type error rather than something to coerce with
        // Convert.ToString - the same stance as EvaluateBinary's STRING guard.
        private string RequireStringArg(string methodName, string paramName, Expr argExpr, Frame frame)
        {
            var value = Evaluate(argExpr, frame);
            if (!(value is string s))
                throw new NotSupportedException(
                    $"{methodName} argument '{paramName}' must be a STRING, got {value?.GetType().Name ?? "null"}");
            return s;
        }

        // MEMCPY (overlapSafe: false) copies forward regardless of overlap, like
        // the C intrinsic it mirrors. MEMMOVE (overlapSafe: true) detects a
        // forward overlap - dest inside [src, src+count) on the same backing
        // array - and copies backward instead, so shifting a buffer down after
        // consuming its head doesn't clobber source elements before they're
        // read.
        //
        // Only dest's Commit is invoked, since src is only read from.
        private Pointer MemCopy(Pointer dest, Pointer src, int count, bool overlapSafe, Frame frame)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "MEMCPY/MEMMOVE count must be >= 0");

            var methodName = overlapSafe ? "MEMMOVE" : "MEMCPY";
            var (destArray, destIndex, destCommit) = ResolveByteTarget(dest, methodName, "destAddr", frame);
            var (srcArray, srcIndex, _) = ResolveByteTarget(src, methodName, "srcAddr", frame);

            var backward = overlapSafe
                && ReferenceEquals(destArray, srcArray)
                && destIndex > srcIndex
                && destIndex < srcIndex + count;

            if (backward)
            {
                for (var i = count - 1; i >= 0; i--)
                    destArray.Elements[destIndex + i] = srcArray.Elements[srcIndex + i];
            }
            else
            {
                for (var i = 0; i < count; i++)
                    destArray.Elements[destIndex + i] = srcArray.Elements[srcIndex + i];
            }

            destCommit?.Invoke();

            return dest;
        }

        private Pointer MemSet(Pointer dest, object value, int count, Frame frame)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "MEMSET count must be >= 0");

            var (destArray, destIndex, destCommit) = ResolveByteTarget(dest, "MEMSET", "destAddr", frame);
            var lowByte = Convert.ToInt32(value) & 0xFF;

            for (var i = 0; i < count; i++)
                destArray.Elements[destIndex + i] = lowByte;

            destCommit?.Invoke();

            return dest;
        }

        private static object EvaluateNumeric(string op, double left, double right) => op switch
        {
            "+" => left + right,
            "-" => left - right,
            "*" => left * right,
            "/" => left / right,
            "<" => left < right,
            ">" => left > right,
            "<=" => left <= right,
            ">=" => left >= right,
            "=" => left == right,
            "<>" => left != right,
            _ => throw new UnsupportedConstructException(op, $"Operator '{op}' not supported"),
        };

        private static object EvaluateNumeric(string op, float left, float right) => op switch
        {
            "+" => left + right,
            "-" => left - right,
            "*" => left * right,
            "/" => left / right,
            "<" => left < right,
            ">" => left > right,
            "<=" => left <= right,
            ">=" => left >= right,
            "=" => left == right,
            "<>" => left != right,
            _ => throw new UnsupportedConstructException(op, $"Operator '{op}' not supported"),
        };

        private static object EvaluateNumeric(string op, long left, long right) => op switch
        {
            "+" => left + right,
            "-" => left - right,
            "*" => left * right,
            "/" => left / right,
            "<" => left < right,
            ">" => left > right,
            "<=" => left <= right,
            ">=" => left >= right,
            "=" => left == right,
            "<>" => left != right,
            _ => throw new UnsupportedConstructException(op, $"Operator '{op}' not supported"),
        };

        private static object EvaluateNumeric(string op, ulong left, ulong right) => op switch
        {
            "+" => left + right,
            "-" => left - right,
            "*" => left * right,
            "/" => left / right,
            "<" => left < right,
            ">" => left > right,
            "<=" => left <= right,
            ">=" => left >= right,
            "=" => left == right,
            "<>" => left != right,
            _ => throw new UnsupportedConstructException(op, $"Operator '{op}' not supported"),
        };

        private static object EvaluateNumeric(string op, int left, int right) => op switch
        {
            "+" => left + right,
            "-" => left - right,
            "*" => left * right,
            "/" => left / right,
            "<" => left < right,
            ">" => left > right,
            "<=" => left <= right,
            ">=" => left >= right,
            "=" => left == right,
            "<>" => left != right,
            _ => throw new UnsupportedConstructException(op, $"Operator '{op}' not supported"),
        };

        // AND_THEN/OR_ELSE, IEC 61131-3 §2.4.5. For BOOL operands this is a
        // genuine short-circuit - the RHS is never evaluated once the LHS
        // decides the result - which is what protects the guard-then-index
        // idiom 'guard AND_THEN arr[i]' from indexing out of range when the
        // guard is false. Bitstring operands have no short-circuit meaning in
        // the standard, so those evaluate both sides and fall back to plain
        // bitwise AND/OR.
        private object EvaluateShortCircuit(BinaryExpr binary, Frame frame)
        {
            var isAndThen = binary.Op == "AND_THEN";
            var leftVal = Evaluate(binary.Left, frame);

            if (leftVal is bool lb)
            {
                if (isAndThen && !lb)
                    return false;
                if (!isAndThen && lb)
                    return true;

                var rightVal = Evaluate(binary.Right, frame);
                if (rightVal is bool rb)
                    return isAndThen ? lb && rb : lb || rb;

                throw new NotSupportedException(
                    $"Operator '{binary.Op}' requires matching BOOL operands, got BOOL and {rightVal?.GetType().Name}");
            }

            var rightValEager = Evaluate(binary.Right, frame);
            return EvaluateBitstring(isAndThen ? "AND" : "OR", leftVal, rightValEager);
        }

        // MOD/AND/OR/XOR are IEC 61131-3 bitstring/logical operators, not numeric
        // arithmetic - AND/OR/XOR operate on matching BOOL or INT operands; MOD is
        // integer-only (no REAL/LREAL remainder in the fixture's scope).
        private static object EvaluateBitstring(string op, object left, object right)
        {
            if (left is bool lb && right is bool rb)
                return op switch
                {
                    "AND" => lb && rb,
                    "OR" => lb || rb,
                    "XOR" => lb ^ rb,
                    _ => throw new UnsupportedConstructException(op, $"Operator '{op}' not supported"),
                };

            if (left is int li && right is int ri)
                return op switch
                {
                    "AND" => li & ri,
                    "OR" => li | ri,
                    "XOR" => li ^ ri,
                    _ => throw new UnsupportedConstructException(op, $"Operator '{op}' not supported"),
                };

            if ((left is long || right is long) && (left is long || left is int) && (right is long || right is int))
                return op switch
                {
                    "AND" => NumericCoercion.ToLong(left) & NumericCoercion.ToLong(right),
                    "OR" => NumericCoercion.ToLong(left) | NumericCoercion.ToLong(right),
                    "XOR" => NumericCoercion.ToLong(left) ^ NumericCoercion.ToLong(right),
                    _ => throw new UnsupportedConstructException(op, $"Operator '{op}' not supported"),
                };

            if ((left is ulong || right is ulong) && (left is ulong || left is int) && (right is ulong || right is int))
                return op switch
                {
                    "AND" => NumericCoercion.ToULong(left) & NumericCoercion.ToULong(right),
                    "OR" => NumericCoercion.ToULong(left) | NumericCoercion.ToULong(right),
                    "XOR" => NumericCoercion.ToULong(left) ^ NumericCoercion.ToULong(right),
                    _ => throw new UnsupportedConstructException(op, $"Operator '{op}' not supported"),
                };

            throw new NotSupportedException($"Operator '{op}' requires matching BOOL or INT operands, got {left?.GetType().Name} and {right?.GetType().Name}");
        }

        private static object EvaluateMod(object left, object right)
        {
            if (left is int li && right is int ri)
                return li % ri;

            if ((left is long || right is long) && (left is long || left is int) && (right is long || right is int))
                return NumericCoercion.ToLong(left) % NumericCoercion.ToLong(right);

            if ((left is ulong || right is ulong) && (left is ulong || left is int) && (right is ulong || right is int))
                return NumericCoercion.ToULong(left) % NumericCoercion.ToULong(right);

            throw new NotSupportedException($"Operator 'MOD' is integer-only, got {left?.GetType().Name} and {right?.GetType().Name}");
        }

        private object EvaluateCall(CallExpr call, Frame frame)
        {
            if (call.Receiver == null)
            {
                if (call.MethodName == "ADR")
                    return new Pointer(ResolveCellForAdr(call.PositionalArgs[0], frame));

                if (call.MethodName == "__ISVALIDREF")
                    return IsValidRef(call.PositionalArgs[0], frame);

                if (call.MethodName == "MEMCPY" || call.MethodName == "MEMMOVE")
                {
                    var args = ResolveIntrinsicArgs(new[] { "destAddr", "srcAddr", "n" }, call.PositionalArgs, call.NamedArgs);
                    return MemCopy(
                        RequirePointerArg(call.MethodName, "destAddr", args, frame),
                        RequirePointerArg(call.MethodName, "srcAddr", args, frame),
                        Convert.ToInt32(Evaluate(RequireIntrinsicArg(call.MethodName, "n", args), frame)),
                        overlapSafe: call.MethodName == "MEMMOVE",
                        frame);
                }

                if (call.MethodName == "MEMSET")
                {
                    var args = ResolveIntrinsicArgs(new[] { "destAddr", "value", "n" }, call.PositionalArgs, call.NamedArgs);
                    return MemSet(
                        RequirePointerArg(call.MethodName, "destAddr", args, frame),
                        Evaluate(RequireIntrinsicArg(call.MethodName, "value", args), frame),
                        Convert.ToInt32(Evaluate(RequireIntrinsicArg(call.MethodName, "n", args), frame)),
                        frame);
                }

                if (call.MethodName == "TestAndSet")
                {
                    var args = ResolveIntrinsicArgs(TestAndSetParamNames, call.PositionalArgs, call.NamedArgs);
                    return TestAndSet(RequireIntrinsicArg("TestAndSet", "Lock", args), frame);
                }

                if (call.MethodName == "AdvanceClock")
                {
                    var args = ResolveIntrinsicArgs(AdvanceClockParamNames, call.PositionalArgs, call.NamedArgs);
                    var duration = Evaluate(RequireIntrinsicArg("AdvanceClock", "Duration", args), frame);
                    AdvanceClock(duration);
                    return null;
                }

                if (call.MethodName == "SIZEOF")
                    return EvaluateSizeOf(call.PositionalArgs[0], frame);

                if (call.MethodName == "CONCAT")
                {
                    var args = ResolveIntrinsicArgs(ConcatParamNames, call.PositionalArgs, call.NamedArgs);
                    RequireIntrinsicArg("CONCAT", "STR1", args);
                    RequireIntrinsicArg("CONCAT", "STR2", args);

                    var sb = new System.Text.StringBuilder();
                    foreach (var paramName in ConcatParamNames)
                        if (args.TryGetValue(paramName, out var argExpr))
                            sb.Append(RequireStringArg("CONCAT", paramName, argExpr, frame));
                    return sb.ToString();
                }

                if (call.MethodName == "ABS")
                {
                    var args = ResolveIntrinsicArgs(AbsParamNames, call.PositionalArgs, call.NamedArgs);
                    return EvaluateAbs(Evaluate(RequireIntrinsicArg("ABS", "IN", args), frame));
                }

                if (TryEvaluateCast(call, frame, out var castResult))
                    return castResult;

                return CallMethod(frame.Instance, call.MethodName, call.PositionalArgs, call.NamedArgs, frame, null, unqualified: true);
            }

            if (call.Receiver is ThisRefExpr)
                return CallMethod(frame.Instance, call.MethodName, call.PositionalArgs, call.NamedArgs, frame, null);

            if (call.Receiver is SuperRefExpr)
            {
                var baseType = _registry.Get(frame.DeclaringTypeName)?.BaseTypeName;
                return CallMethod(frame.Instance, call.MethodName, call.PositionalArgs, call.NamedArgs, frame, baseType);
            }

            var receiver = Evaluate(call.Receiver, frame);
            if (receiver is UnassignedInterfaceReference unassignedReceiver)
                throw unassignedReceiver.Fault(call.MethodName);

            return CallMethod((FbInstance)receiver, call.MethodName, call.PositionalArgs, call.NamedArgs, frame, null);
        }

        private static readonly HashSet<string> IntegerCastTargets = new HashSet<string>
        {
            "SINT", "USINT", "INT", "UINT", "DINT", "UDINT", "LINT", "ULINT", "BYTE", "WORD", "DWORD", "LWORD",
        };

        // Recognizes explicit <from>_TO_<to> conversion calls (e.g.
        // LREAL_TO_INT) per TwinCAT's cast naming convention. These are not real
        // methods, so they are intercepted before CallMethod/native-bridge
        // dispatch; an unrecognized shape returns false and falls through to it.
        private bool TryEvaluateCast(CallExpr call, Frame frame, out object result)
        {
            result = null;

            var separator = call.MethodName.IndexOf("_TO_", StringComparison.Ordinal);
            if (separator < 0 || call.PositionalArgs.Count != 1)
                return false;

            var fromType = call.MethodName.Substring(0, separator);
            var toType = call.MethodName.Substring(separator + 4);
            var value = Evaluate(call.PositionalArgs[0], frame);

            // Explicit InvariantCulture provider: Convert.ToXXX(object) without
            // one parses string sources (e.g. STRING_TO_LREAL) against
            // CurrentCulture, which under a culture using '.' as the group
            // separator (e.g. de-DE) drops the decimal point instead of
            // erroring or parsing it correctly.
            if (toType == "REAL")
                result = Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
            else if (toType == "LREAL")
                result = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
            else if (IntegerCastTargets.Contains(toType))
                result = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
            // _TO_STRING is deliberately scoped to numeric source types only:
            // a non-numeric prefix like BOOL_TO_STRING or TIME_TO_STRING falls
            // through to CallMethod/native-bridge dispatch and keeps its
            // "Method not found" error rather than being silently formatted.
            //
            // Invariant culture, for the same reason as
            // ScalarAssertType.FormatDouble: a CurrentCulture of de-DE would
            // render '.' as ','. Plain digits - no attempt at TwinCAT-exact
            // digit-count or exponent parity.
            else if (toType == "STRING" &&
                     (fromType == "REAL" || fromType == "LREAL" || IntegerCastTargets.Contains(fromType)))
                result = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            else
                return false;

            return true;
        }
    }
}
