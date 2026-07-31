using System;

namespace xStunit.Interpreter
{
    // The IEC 61131-3 "smaller to larger is implicit" rule, shared by binary
    // operand widening and assignment coercion. Works on boxed CLR values
    // alone, with no Engine/Frame/TypeRegistry to consult, so the declared IEC
    // type is not available here - see IecNumericType for which types share a
    // box.
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

            // ULINT/LWORD box as ulong, the same tier as long - a separate
            // branch because signed and unsigned 64-bit have no common type
            // that holds both, so ToLong/ToULong each accept only int
            // alongside their own type.
            if (left is ulong || right is ulong)
                return (ToULong(left), ToULong(right));

            // TIME and the DATE family box as uint - unlike UDINT/DWORD, which
            // box as long specifically to avoid this. Widen to long rather
            // than falling through to the int unbox below, which throws: a
            // boxed uint cannot be unboxed as int.
            if (left is uint || right is uint)
                return (ToLong(left), ToLong(right));

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
            uint u => u,
            int i => i,
            _ => throw new NotSupportedException($"Cannot use {value?.GetType().Name} in numeric arithmetic"),
        };

        // A negative int has no unsigned 64-bit representation, so it is
        // rejected rather than allowed to wrap into a huge ulong.
        public static ulong ToULong(object value) => value switch
        {
            ulong ul => ul,
            int i when i >= 0 => (ulong)i,
            int negative => throw new NotSupportedException(
                $"Cannot widen negative value {negative} to an unsigned 64-bit type (ULINT/LWORD)"),
            _ => throw new NotSupportedException($"Cannot use {value?.GetType().Name} in numeric arithmetic"),
        };

        // INT->REAL->LREAL widens implicitly on assignment; the reverse needs
        // an explicit X_TO_Y cast. The target's type is inferred from the value
        // already in its slot, so 'existing' must be the target's current
        // contents. TryEvaluateCast never yields a wider CLR type than the
        // cast target, so a rejection here means the assignment skipped a cast.
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
            // UDINT/DWORD/LINT cells box as long, so an int assigned into one
            // has to widen to match.
            if (existing is long && incoming is int intForLong)
                return (long)intForLong;
            // ULINT/LWORD cells box as ulong - as above, except a negative int
            // is rejected rather than wrapped, and with this method's
            // InvalidOperationException rather than ToULong's
            // NotSupportedException.
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
