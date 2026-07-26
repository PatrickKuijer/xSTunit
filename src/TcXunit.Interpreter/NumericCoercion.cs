using System;

namespace TcXunit.Interpreter
{
    // Shared IEC 61131-3 numeric promotion/narrowing rule (TcXunit-6af.2),
    // extracted out of Engine.Expressions.cs (EvaluateBinary's widening
    // ladder) and Engine.Statements.cs (CoerceForAssignment), which used to
    // reimplement the same "smaller to larger is implicit" rule twice - one
    // as an operand-widening switch, one as a target-Cell-type inspection.
    // Operates on plain boxed CLR values only - no Engine/Frame/TypeRegistry
    // dependency - so it is unit-testable in isolation.
    public static class NumericCoercion
    {
        // Widens both operands to their common arithmetic type, following
        // int -> long -> float -> double ("smaller to larger is implicit").
        // Caller must have already ruled out non-numeric operands (BOOL,
        // Pointer, null).
        public static (object Left, object Right) Promote(object left, object right)
        {
            if (left is double || right is double)
                return (ToDouble(left), ToDouble(right));

            if (left is float || right is float)
                return (ToFloat(left), ToFloat(right));

            if (left is long || right is long)
                return (ToLong(left), ToLong(right));

            // ULINT/LWORD cells box as ulong (TcXunit-6af.1), same tier as
            // LINT/UDINT/DWORD's long - kept as its own branch rather than
            // folded into the long branch above because long and ulong don't
            // implicitly mix (ToLong/ToULong each only accept int alongside
            // their own type), matching the deliberate long+float/double
            // non-mixing above.
            if (left is ulong || right is ulong)
                return (ToULong(left), ToULong(right));

            return ((int)left, (int)right);
        }

        public static double ToDouble(object value) => value switch
        {
            double d => d,
            float f => f,
            int i => i,
            _ => throw new NotSupportedException($"Cannot use {value?.GetType().Name} in numeric arithmetic"),
        };

        public static float ToFloat(object value) => value switch
        {
            float f => f,
            int i => i,
            _ => throw new NotSupportedException($"Cannot use {value?.GetType().Name} in numeric arithmetic"),
        };

        public static long ToLong(object value) => value switch
        {
            long l => l,
            int i => i,
            _ => throw new NotSupportedException($"Cannot use {value?.GetType().Name} in numeric arithmetic"),
        };

        // A negative int has no unsigned 64-bit representation - reject it
        // explicitly rather than silently wrapping it into a huge ulong via
        // an unchecked cast (an int literal/expression combined with a
        // ULINT/LWORD operand is only meaningful when it's non-negative).
        public static ulong ToULong(object value) => value switch
        {
            ulong ul => ul,
            int i when i >= 0 => (ulong)i,
            int negative => throw new NotSupportedException(
                $"Cannot widen negative value {negative} to an unsigned 64-bit type (ULINT/LWORD)"),
            _ => throw new NotSupportedException($"Cannot use {value?.GetType().Name} in numeric arithmetic"),
        };

        // INT->REAL->LREAL widens implicitly on assignment (inferred from the
        // target cell's current CLR type, since Cell carries no declared-type
        // tag of its own); the reverse requires an explicit X_TO_Y cast
        // produced by TryEvaluateCast, which never returns a wider CLR type
        // than the cast target - so a rejection here means the assignment
        // skipped a cast.
        public static object CoerceForAssignment(object existing, object incoming)
        {
            if (existing is int && incoming is float)
                throw new InvalidOperationException("Implicit narrowing from REAL to INT is not allowed; use REAL_TO_INT(...)");
            if (existing is int && incoming is double)
                throw new InvalidOperationException("Implicit narrowing from LREAL to INT is not allowed; use LREAL_TO_INT(...)");
            if (existing is float && incoming is double)
                throw new InvalidOperationException("Implicit narrowing from LREAL to REAL is not allowed; use LREAL_TO_REAL(...)");

            if (existing is float && incoming is int intForFloat)
                return (float)intForFloat;
            if (existing is double && (incoming is int || incoming is float))
                return ToDouble(incoming);
            // UDINT/DWORD/LINT cells box as long (TcXunit-6af.1); an int
            // literal/expression assigned into one must widen the same way.
            if (existing is long && incoming is int intForLong)
                return (long)intForLong;
            // ULINT/LWORD cells box as ulong (TcXunit-6af.1); mirror the long
            // case above, but reject a negative incoming int explicitly
            // (InvalidOperationException, matching this method's other
            // rejections above) instead of silently wrapping it via a cast -
            // a negative int has no unsigned 64-bit representation.
            if (existing is ulong && incoming is int intForULong)
            {
                if (intForULong < 0)
                    throw new InvalidOperationException(
                        $"Cannot assign negative value {intForULong} to a ULINT/LWORD (unsigned 64-bit) variable");
                return (ulong)intForULong;
            }

            return incoming;
        }
    }
}
