using xStunit.Interpreter.Extensibility;

namespace xStunit.SystemLibraryPlugins
{
    // Tc2_System's four DWORD bit helpers:
    //   FUNCTION SETBIT32   : DWORD  (inVal32 : DWORD; bitNo : SINT)
    //   FUNCTION CLEARBIT32 : DWORD  (inVal32 : DWORD; bitNo : SINT)
    //   FUNCTION GETBIT32   : BOOL   (inVal32 : DWORD; bitNo : SINT)
    //   FUNCTION CSETBIT32  : DWORD  (inVal32 : DWORD; bitNo : SINT; bitVal : BOOL)
    //
    // bitNo is documented as 0..31 and "internally converted to a modulo 32
    // value prior to execution", so an out-of-range number wraps rather than
    // faulting - reproduced here because real source relies on it to index a
    // bit by a computed offset.
    // The two arguments all four functions share, read once in declared order.
    //
    // Read once matters: NativeCallContext resolves a parameter by name and
    // then by position, and the positional slot it lands on depends on which
    // earlier parameters were passed by name - a correction it accumulates as
    // the parameters are asked for, in ascending declared order. Asking for the
    // same parameter twice at different points is not wrong today, but it makes
    // that ordering rule something each function has to keep in its head.
    internal readonly struct BitOperand
    {
        // DWORD is long in the interpreter's value model; the mask keeps a
        // result inside 32 bits, since nothing else would stop a shift from
        // leaking into the upper half of the long that carries it.
        private const long DWordMask = 0xFFFFFFFFL;

        private BitOperand(long value, int bit)
        {
            Value = value;
            Bit = bit;
        }

        public long Value { get; }

        public int Bit { get; }

        public long Mask => 1L << Bit;

        public static BitOperand Read(NativeCallContext context)
        {
            var value = context.RequireInt64("inVal32", 0) & DWordMask;

            // The vendor's modulo, corrected for a negative bitNo: bitNo is a
            // SINT, so it can legitimately arrive negative, and C#'s % would
            // then produce a negative shift distance rather than wrapping the
            // way TwinCAT does.
            var bit = (int)(((context.RequireInt64("bitNo", 1) % 32) + 32) % 32);

            return new BitOperand(value, bit);
        }

        public long Clamp(long result) => result & DWordMask;
    }

    public sealed class SetBit32Function : IXstunitNativeFunction
    {
        public string Name => "SETBIT32";

        public object Invoke(NativeCallContext context)
        {
            var operand = BitOperand.Read(context);
            return operand.Clamp(operand.Value | operand.Mask);
        }
    }

    public sealed class ClearBit32Function : IXstunitNativeFunction
    {
        public string Name => "CLEARBIT32";

        public object Invoke(NativeCallContext context)
        {
            var operand = BitOperand.Read(context);
            return operand.Clamp(operand.Value & ~operand.Mask);
        }
    }

    public sealed class GetBit32Function : IXstunitNativeFunction
    {
        public string Name => "GETBIT32";

        public object Invoke(NativeCallContext context)
        {
            var operand = BitOperand.Read(context);
            return (operand.Value & operand.Mask) != 0;
        }
    }

    public sealed class CSetBit32Function : IXstunitNativeFunction
    {
        public string Name => "CSETBIT32";

        public object Invoke(NativeCallContext context)
        {
            var operand = BitOperand.Read(context);

            return operand.Clamp(context.RequireBool("bitVal", 2)
                ? operand.Value | operand.Mask
                : operand.Value & ~operand.Mask);
        }
    }
}
