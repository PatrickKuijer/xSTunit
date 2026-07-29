using System;
using System.Collections.Generic;
using System.Linq;

namespace TcXunit.Interpreter
{
    public sealed partial class Engine
    {
        public object Evaluate(Expr expr, Frame frame)
        {
            switch (expr)
            {
                case IntLiteralExpr i:
                    return i.Value;
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
                    if (cell == null && !TryResolveGlobalCell(id.Name, out cell))
                        throw new InvalidOperationException($"Unknown variable '{id.Name}'");
                    return cell.Value;
                }
                case ThisRefExpr:
                    return frame.Instance;
                case SuperRefExpr:
                    return frame.Instance;
                case DerefExpr deref:
                    return ((Pointer)Evaluate(deref.Inner, frame)).Target.Value;
                case IndexExpr index:
                {
                    var array = (ArrayValue)Evaluate(index.Receiver, frame);
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

                    // EnumTypeName.Member where EnumTypeName names a
                    // user-defined ENUM DUT rather than a variable
                    // (TcXunit-rk3) - same "receiver identifier doesn't
                    // resolve as a variable" shape as the built-in-enum
                    // check above, checked right alongside it so a
                    // user-defined enum resolves the same way a built-in
                    // one already does.
                    if (fieldAccess.Receiver is IdentifierExpr dutEnumTypeId &&
                        frame.ResolveCell(dutEnumTypeId.Name) == null &&
                        _registry.TryGetEnumMembers(dutEnumTypeId.Name, out var dutEnumMembers))
                    {
                        if (!dutEnumMembers.TryGetValue(fieldAccess.FieldName, out var dutEnumValue))
                            throw new InvalidOperationException($"Unknown enum member '{dutEnumTypeId.Name}.{fieldAccess.FieldName}'");
                        return dutEnumValue;
                    }

                    // GvlName.field where GvlName isn't a variable/field in
                    // scope but a registered GVL (TcXunit-71o) - same
                    // "receiver identifier doesn't resolve as a variable"
                    // shape as the enum check above.
                    if (TryGetGvlFields(fieldAccess, frame, out var gvlFields))
                    {
                        if (!gvlFields.TryGetValue(fieldAccess.FieldName, out var gvlCell))
                            throw new InvalidOperationException($"Unknown field '{fieldAccess.FieldName}'");
                        return gvlCell.Value;
                    }

                    var receiverValue = Evaluate(fieldAccess.Receiver, frame);
                    var fields = FieldsOf(receiverValue);
                    if (fields.TryGetValue(fieldAccess.FieldName, out var cell))
                        return cell.Value;

                    // No VAR-block field matches - the receiver may be an FB
                    // instance exposing a PROPERTY of this name instead
                    // (TcXunit-sxv); a get-only PROPERTY was never
                    // materialized into Fields at NewInstance time, so this
                    // falls back to running its Get accessor rather than
                    // treating the miss as "Unknown field".
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
                    return new ArrayValue(new List<(int, int)> { (0, elements.Length - 1) }, null, elements);
                }
                case BinaryExpr binary:
                    return EvaluateBinary(binary, frame);
                case UnaryExpr unary:
                    return EvaluateUnary(unary, frame);
                case CallExpr call:
                    return EvaluateCall(call, frame);
                default:
                    throw new NotSupportedException($"Expression type {expr.GetType().Name} not supported");
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
                    case float f: return -f;
                    case double d: return -d;
                    default:
                        throw new NotSupportedException($"Unary '-' requires a numeric operand, got {value?.GetType().Name}");
                }
            }

            throw new NotSupportedException($"Unary operator '{unary.Op}' not supported");
        }

        private object EvaluateBinary(BinaryExpr binary, Frame frame)
        {
            // AND_THEN/OR_ELSE (IEC 61131-3 §2.4.5, the short-circuit forms of
            // AND/OR) must not evaluate their RHS at all when the LHS already
            // decides the result - unlike every other BinaryExpr, which is
            // evaluated eagerly below. That's what lets the standard
            // "guard AND_THEN indexed-access" idiom skip the index expression
            // when the guard is false. Bitstring (BYTE/WORD/DWORD) operands
            // have no short-circuit meaning, so those still evaluate both
            // sides and fall back to plain bitwise AND/OR.
            if (binary.Op == "AND_THEN" || binary.Op == "OR_ELSE")
                return EvaluateShortCircuit(binary, frame);

            var leftVal = Evaluate(binary.Left, frame);
            var rightVal = Evaluate(binary.Right, frame);

            if ((binary.Op == "+" || binary.Op == "-") && (leftVal is Pointer || rightVal is Pointer))
                return EvaluatePointerArithmetic(binary.Op, leftVal, rightVal, frame);

            if ((binary.Op == "=" || binary.Op == "<>") &&
                (leftVal is Pointer || rightVal is Pointer || leftVal is FbInstance || rightVal is FbInstance ||
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

            // TcXunit-80v: any other BOOL usage reaching this point has no
            // valid IEC 61131-3 semantics - either BOOL mixed with a
            // non-BOOL operand (e.g. TRUE = 1), or a BOOL operand with an
            // operator that has no BOOL semantics (e.g. TRUE < FALSE).
            // Without this guard both cases fall through to the int-cast
            // numeric path below and throw an unhelpful raw
            // InvalidCastException; guard here for a descriptive message,
            // mirroring EvaluateBitstring's mismatched-type guard.
            if (leftVal is bool || rightVal is bool)
                throw new NotSupportedException(
                    $"Operator '{binary.Op}' is not supported between {leftVal?.GetType().Name ?? "null"} and " +
                    $"{rightVal?.GetType().Name ?? "null"}; BOOL only supports '=' and '<>' against another BOOL");

            // TcXunit-ixh: STRING/WSTRING operands (both are represented as
            // boxed System.String - StringTypeInfo distinguishes them only at
            // declaration time) have no numeric representation, so without
            // this guard they fall through to the int-cast path below and
            // throw a raw InvalidCastException. IEC 61131-3 defines ordering
            // for ANY_STRING as ordinal/lexicographic comparison (TwinCAT
            // compares string data byte-by-byte), so all six comparison
            // operators are supported here, unlike the BOOL guard above which
            // only allows '=' and '<>'.
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

        // ADR(x) +/- offset: offset moves in whole array elements, not raw
        // bytes - correct as literal byte arithmetic when the pointee is a
        // BYTE/SINT/USINT array (the buffer-packing case MEMCPY/MEMSET/MEMMOVE
        // exist for), an approximation for wider element types. A pointer
        // whose target is an array element (ArrayElementCell, including the
        // ADR(arr)-decays-to-element-0 case) steps directly on the real
        // backing ArrayValue. A pointer to a scalar or whole STRUCT has no
        // array element to step through, but does have a known declared
        // type (Cell.DeclaredTypeName) - reuse the MEMCPY/MEMSET byte-layout
        // packer (PackCellToByteView, Engine.ByteLayout.cs) to snapshot it
        // into a synthetic BYTE-array view and step through that instead,
        // walking across STRUCT field/array-of-struct boundaries the same
        // way SIZEOF's layout math does (TcXunit-sej.2). Read-only: ptr^ :=
        // isn't a supported assignment target yet (Parser.RequireLValue), so
        // there is no live backing store to write through for this case, only
        // a fresh-packed snapshot good for dereferencing.
        private object EvaluatePointerArithmetic(string op, object leftVal, object rightVal, Frame frame)
        {
            if (op == "-" && leftVal is Pointer && rightVal is Pointer)
                throw new NotSupportedException("Pointer-minus-pointer is not supported");

            var (ptr, offsetVal) = leftVal is Pointer p ? (p, rightVal) : ((Pointer)rightVal, leftVal);
            // Convert.ToInt32 rather than a direct (int) cast: the offset is
            // an arbitrary user expression that boxes as long when it is a
            // DINT/UDINT/LINT/ULINT/DWORD value (e.g. the UDINT loop index in
            // the 'FOR i := 0 TO inSize - 1 DO (ipA + i)^' buffer-compare
            // idiom) or uint for TIME/DATE, and a direct (int) cast throws
            // InvalidCastException on those boxed types instead of narrowing
            // them (same widening-unbox pitfall fixed in FlattenIndex,
            // TcXunit-iyd.5).
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

        // Pointer '='/'<>' comparison (TcXunit-dur): the standard IEC 61131-3
        // null-pointer-check idiom is 'IF ipSrc = 0 THEN'. An unbound/default
        // POINTER TO x Cell holds C# null (see DefaultValue), never int 0, so
        // the null side of the comparison is a null reference, not a numeric
        // zero - but the literal on the other side is still the int 0. Two
        // real (ADR-bound) pointers are compared by target-Cell identity;
        // a bound pointer is never "null"/zero, so it compares unequal to
        // both null and any int literal.
        //
        // TcXunit-dba: an interface-typed (or plain FB-reference) variable
        // follows the same "= 0 is the assigned/null check" idiom (e.g.
        // 'IF (iipHandler <> 0) AND iipHandler.bDoWork(...) THEN'),
        // but it has no dedicated Pointer/null representation of its own -
        // an unassigned interface field has no POU registered under its
        // interface type name (TcPouParser never parses <Itf> POUs), so
        // DefaultValue's lookups all miss and it falls through to the
        // int-0 default; once assigned (itf := concreteFb), the field holds
        // the concrete FB's FbInstance directly. Route FbInstance through
        // the same null-check semantics as Pointer: assigned (FbInstance)
        // compares unequal to zero/null, and two assigned interface
        // variables compare by referenced-instance identity.
        private static object EvaluatePointerEquality(string op, object leftVal, object rightVal)
        {
            bool equal;
            if (leftVal is Pointer leftPtr && rightVal is Pointer rightPtr)
                equal = PointerTargetsEqual(leftPtr.Target, rightPtr.Target);
            else if (leftVal is FbInstance leftFb && rightVal is FbInstance rightFb)
                equal = ReferenceEquals(leftFb, rightFb);
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

        // A pointer compared to any numeric-zero literal - int (BYTE/WORD/
        // DINT/etc.), REAL (float), or LREAL (double) - is the null-check
        // idiom regardless of the literal's numeric type (TcXunit-3zc).
        private static bool IsNumericZero(object val)
        {
            return (val is int i && i == 0)
                || (val is float f && f == 0f)
                || (val is double d && d == 0d);
        }

        // ADR(x) builds a fresh ArrayElementCell wrapper on every call
        // (TcXunit-sej.2), so two pointers to the "same" element are two
        // distinct ArrayElementCell instances - compare the underlying
        // ArrayValue + Index instead of Cell reference identity for that
        // case; fall back to reference equality for a plain scalar/struct
        // field Cell (ADR(x) on those returns the actual field Cell).
        private static bool PointerTargetsEqual(Cell left, Cell right)
        {
            if (left is ArrayElementCell leftAec && right is ArrayElementCell rightAec)
                return ReferenceEquals(leftAec.Array, rightAec.Array) && leftAec.Index == rightAec.Index;

            return ReferenceEquals(left, right);
        }

        // MEMCPY/MEMSET/MEMMOVE (TcXunit-sej.3): dest/src must be pointers to
        // an array element (see ResolveCellForAdr/ArrayElementCell) - this is
        // the POINTER TO BYTE over ARRAY OF BYTE buffer-packing case these
        // intrinsics exist for. n counts elements (== bytes for a BYTE/SINT/
        // USINT-element array); out-of-range access throws naturally via the
        // backing Elements[] indexer.
        //
        // TcXunit-996: these are native intrinsics (no VarBlockParser decls
        // to bind against, unlike FB/method calls), so named args aren't
        // reconciled by BindParams - resolve each declared param (destAddr/
        // srcAddr/value/n) by name first (e.g. MEMCPY(destAddr := ipDst,
        // srcAddr := ipSrc, inSrcSize)), consuming PositionalArgs in
        // left-to-right order only for params *not* given by name (mirrors
        // BindParams' shared posIndex - a positional arg's PositionalArgs
        // slot depends on how many preceding params were named, not on the
        // param's declared signature position).
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

        // CONCAT(STR1, STR2, ..., STR10): TwinCAT's Tc2_Standard signature -
        // STR1/STR2 required, STR3..STR10 optional trailing args - resolved
        // by name or IEC positional order same as the other intrinsics
        // (ResolveIntrinsicArgs), then appended in this declared order
        // regardless of how the caller mixed named/positional args.
        private static readonly string[] ConcatParamNames =
            { "STR1", "STR2", "STR3", "STR4", "STR5", "STR6", "STR7", "STR8", "STR9", "STR10" };

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

        // Mirrors the STRING-only guard in EvaluateBinary ("STRING can only
        // be compared against another STRING") - CONCAT's STR* args are
        // ANY_STRING per the IEC signature, so a non-string arg is a type
        // error, not something to coerce via Convert.ToString.
        private string RequireStringArg(string methodName, string paramName, Expr argExpr, Frame frame)
        {
            var value = Evaluate(argExpr, frame);
            if (!(value is string s))
                throw new NotSupportedException(
                    $"{methodName} argument '{paramName}' must be a STRING, got {value?.GetType().Name ?? "null"}");
            return s;
        }

        // MEMCPY (overlapSafe: false) copies forward regardless of overlap,
        // same as the C intrinsic it mirrors. MEMMOVE (overlapSafe: true)
        // detects a forward overlap (dest inside [src, src+count) on the same
        // backing array) and copies backward instead, so a "shift buffer
        // down after consuming its head" pattern doesn't clobber source
        // elements before they're read.
        //
        // dest/src may target either a real ArrayElementCell or, since
        // TcXunit-4vn, a plain scalar/STRUCT Cell (ADR(struct.field) or
        // ADR(scalarVar)) - ResolveByteTarget hands back a byte-array view
        // over either shape uniformly; only dest's Commit (writing the final
        // bytes back into a scalar/STRUCT Cell) matters, since src is only
        // read from.
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
            _ => throw new NotSupportedException($"Operator '{op}' not supported"),
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
            _ => throw new NotSupportedException($"Operator '{op}' not supported"),
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
            _ => throw new NotSupportedException($"Operator '{op}' not supported"),
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
            _ => throw new NotSupportedException($"Operator '{op}' not supported"),
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
            _ => throw new NotSupportedException($"Operator '{op}' not supported"),
        };

        // Evaluates AND_THEN/OR_ELSE. For BOOL operands this is a genuine
        // short-circuit: the RHS expression is never evaluated once the LHS
        // already determines the result (AND_THEN stops on FALSE, OR_ELSE
        // stops on TRUE) - this is what protects a guard-then-index idiom
        // like 'guard AND_THEN arr[i]' from indexing out of range when the
        // guard is false. Bitstring (BYTE/WORD/DWORD/INT) operands have no
        // short-circuit meaning in the standard, so both sides are evaluated
        // and the result falls back to plain bitwise AND/OR.
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
                    _ => throw new NotSupportedException($"Operator '{op}' not supported"),
                };

            if (left is int li && right is int ri)
                return op switch
                {
                    "AND" => li & ri,
                    "OR" => li | ri,
                    "XOR" => li ^ ri,
                    _ => throw new NotSupportedException($"Operator '{op}' not supported"),
                };

            if ((left is long || right is long) && (left is long || left is int) && (right is long || right is int))
                return op switch
                {
                    "AND" => NumericCoercion.ToLong(left) & NumericCoercion.ToLong(right),
                    "OR" => NumericCoercion.ToLong(left) | NumericCoercion.ToLong(right),
                    "XOR" => NumericCoercion.ToLong(left) ^ NumericCoercion.ToLong(right),
                    _ => throw new NotSupportedException($"Operator '{op}' not supported"),
                };

            if ((left is ulong || right is ulong) && (left is ulong || left is int) && (right is ulong || right is int))
                return op switch
                {
                    "AND" => NumericCoercion.ToULong(left) & NumericCoercion.ToULong(right),
                    "OR" => NumericCoercion.ToULong(left) | NumericCoercion.ToULong(right),
                    "XOR" => NumericCoercion.ToULong(left) ^ NumericCoercion.ToULong(right),
                    _ => throw new NotSupportedException($"Operator '{op}' not supported"),
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

                if (TryEvaluateCast(call, frame, out var castResult))
                    return castResult;

                return CallMethod(frame.Instance, call.MethodName, call.PositionalArgs, call.NamedArgs, frame, null);
            }

            if (call.Receiver is ThisRefExpr)
                return CallMethod(frame.Instance, call.MethodName, call.PositionalArgs, call.NamedArgs, frame, null);

            if (call.Receiver is SuperRefExpr)
            {
                var baseType = _registry.Get(frame.DeclaringTypeName)?.BaseTypeName;
                return CallMethod(frame.Instance, call.MethodName, call.PositionalArgs, call.NamedArgs, frame, baseType);
            }

            var receiverInstance = (FbInstance)Evaluate(call.Receiver, frame);
            return CallMethod(receiverInstance, call.MethodName, call.PositionalArgs, call.NamedArgs, frame, null);
        }

        private static readonly HashSet<string> IntegerCastTargets = new HashSet<string>
        {
            "SINT", "USINT", "INT", "UINT", "DINT", "UDINT", "LINT", "ULINT", "BYTE", "WORD", "DWORD", "LWORD",
        };

        // Recognizes explicit <from>_TO_<to> conversion calls (e.g. LREAL_TO_INT)
        // per TwinCAT's narrowing-cast naming convention. Not a real method, so
        // it's intercepted here before falling through to CallMethod/native-bridge
        // dispatch.
        private bool TryEvaluateCast(CallExpr call, Frame frame, out object result)
        {
            result = null;

            var separator = call.MethodName.IndexOf("_TO_", StringComparison.Ordinal);
            if (separator < 0 || call.PositionalArgs.Count != 1)
                return false;

            var toType = call.MethodName.Substring(separator + 4);
            var value = Evaluate(call.PositionalArgs[0], frame);

            if (toType == "REAL")
                result = Convert.ToSingle(value);
            else if (toType == "LREAL")
                result = Convert.ToDouble(value);
            else if (IntegerCastTargets.Contains(toType))
                result = Convert.ToInt32(value);
            else
                return false;

            return true;
        }
    }
}
