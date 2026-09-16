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
    internal static class BitOperand
    {
        // DWORD is long in the interpreter's value model; the mask keeps a
        // result inside 32 bits, since nothing else would stop a shift from
        // leaking into the upper half of the long that carries it.
        private const long DWordMask = 0xFFFFFFFFL;

        public static long Value(NativeCallContext context) =>
            context.RequireInt64("inVal32", 0) & DWordMask;

        // The vendor's modulo, corrected for a negative bitNo: bitNo is a SINT,
        // so it can legitimately arrive negative, and C#'s % would then produce
        // a negative shift distance rather than wrapping the way TwinCAT does.
        public static int Bit(NativeCallContext context) =>
            (int)(((context.RequireInt64("bitNo", 1) % 32) + 32) % 32);

        public static long Mask(NativeCallContext context) => 1L << Bit(context);

        public static long Clamp(long value) => value & DWordMask;
    }

    public sealed class SetBit32Function : IXstunitNativeFunction
    {
        public string Name => "SETBIT32";

        public object Invoke(NativeCallContext context) =>
            BitOperand.Clamp(BitOperand.Value(context) | BitOperand.Mask(context));
    }

    public sealed class ClearBit32Function : IXstunitNativeFunction
    {
        public string Name => "CLEARBIT32";

        public object Invoke(NativeCallContext context) =>
            BitOperand.Clamp(BitOperand.Value(context) & ~BitOperand.Mask(context));
    }

    public sealed class GetBit32Function : IXstunitNativeFunction
    {
        public string Name => "GETBIT32";

        public object Invoke(NativeCallContext context) =>
            (BitOperand.Value(context) & BitOperand.Mask(context)) != 0;
    }

    public sealed class CSetBit32Function : IXstunitNativeFunction
    {
        public string Name => "CSETBIT32";

        public object Invoke(NativeCallContext context)
        {
            var value = BitOperand.Value(context);
            var mask = BitOperand.Mask(context);

            return BitOperand.Clamp(context.RequireBool("bitVal", 2) ? value | mask : value & ~mask);
        }
    }
}
