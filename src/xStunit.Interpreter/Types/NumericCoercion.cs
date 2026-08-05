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

            // long and ulong are the same width, so neither is "larger" and no
            // implicit widening rule spans them - but both halves of the
            // boundary are reachable without a cast: a literal takes the
            // narrowest box that holds it regardless of the declared type, and
            // UDINT/DWORD box as long while ULINT/LWORD box as ulong. Storing
            // the incoming box unchanged would leave the cell disagreeing with
            // its own declared type for the rest of its life, so the value is
            // converted where the other half of the range can hold it and
            // rejected where it cannot. The message names both type groups
            // because the box is all this method knows, and one box serves
            // several IEC types.
            if (existing is long && incoming is ulong ulongForLong)
            {
                if (ulongForLong > long.MaxValue)
                    throw new InvalidOperationException(
                        $"Cannot assign ULINT/LWORD value {ulongForLong} to a LINT/UDINT/DWORD variable: it is out of range for all three");
                return (long)ulongForLong;
            }
            if (existing is ulong && incoming is long longForULong)
            {
                if (longForULong < 0)
                    throw new InvalidOperationException(
                        $"Cannot assign negative LINT/UDINT/DWORD value {longForULong} to a ULINT/LWORD (unsigned 64-bit) variable");
                return (ulong)longForULong;
            }

            // TIME and the DATE family are the only things boxing as uint, and
            // nothing else reaches them as a value: Promote widens a uint
            // operand to long to do the arithmetic, and a plain integer literal
            // arrives as int, so a TIME cell is written from a tier above and a
            // tier below but never at its own width. Narrowing here is what
            // keeps a declared TIME the same box whether or not arithmetic
            // touched it.
            //
            // The narrowing wraps rather than throwing, because TwinCAT stores
            // TIME/DATE/DT/TOD in 32 bits: a sum past T#49d17h2m47s295ms rolls
            // over there, and a difference that goes negative rolls over the
            // other way. Rejecting the overflow would make this a stricter
            // machine than the one it stands in for, so a fixture that rolls a
            // meter over on the target rolls it over here too.
            if (existing is uint && incoming is long longForUInt)
                return unchecked((uint)longForUInt);
            if (existing is uint && incoming is int intForUInt)
                return unchecked((uint)intForUInt);

            return incoming;
        }

        // The overload for callers that know what the destination was declared
        // as. UDINT, DWORD and LINT all box as long, so the rules above cannot
        // tell a 32-bit cell from a 64-bit one; the declared type name is what
        // separates them. The two-argument entry point stands for "no
        // declaration behind this slot" and keeps the box-only behaviour.
        public static object CoerceForAssignment(object existing, object incoming, string declaredTypeName)
        {
            var coerced = CoerceForAssignment(existing, incoming);
            return coerced is long widened ? WrapToDeclaredRange(declaredTypeName, widened) : coerced;
        }

        // UDINT and DWORD box as long because their range overflows Int32, but
        // on the target they are 32 bits wide: a sum past 4294967295 rolls over
        // there. Wrapping rather than throwing is the same call the TIME/DATE
        // narrowing above makes - rejecting would leave this a stricter machine
        // than the one it stands in for. LINT shares the box and is genuinely
        // 64 bits, so its own bounds admit every value and it is never touched.
        //
        // A slot with no declaration behind it, or one naming something this
        // table does not know (an ALIAS DUT, "REFERENCE TO UDINT", a STRUCT),
        // is left exactly as the box-only rules produced it: a guessed width
        // would corrupt values that were already right.
        //
        // Long-boxed types only, on purpose. SINT/USINT/BYTE/INT/UINT/WORD/DINT
        // share the int box, and whether those narrow on assignment is a
        // separate question this rule must not answer by accident.
        private static object WrapToDeclaredRange(string declaredTypeName, long value)
        {
            if (declaredTypeName == null ||
                !IecNumericType.TryGetBounds(declaredTypeName, out var bounds) ||
                !(bounds.Min is long min) || !(bounds.Max is long max) ||
                (value >= min && value <= max))
                return value;

            // Modular over the declared range, so an overflow rolls to the
            // bottom and an underflow to the top, matching the target's
            // two's-complement rollover. The width cannot overflow its own
            // arithmetic here: a range as wide as long's has already returned
            // above, because no value falls outside it.
            var size = max - min + 1;
            var offset = (value - min) % size;
            return min + (offset < 0 ? offset + size : offset);
        }
    }
}
