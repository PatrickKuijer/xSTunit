using xStunit.Interpreter;
using xStunit.Interpreter.Extensibility;

namespace xStunit.SystemLibraryPlugins
{
    // Tc2_System's MEMCMP:
    //   FUNCTION MEMCMP : DINT
    //   VAR_INPUT pBuf1 : PVOID; pBuf2 : PVOID; n : UDINT; END_VAR
    //
    // The one member of the memory family that is not already an interpreter
    // intrinsic - MEMCPY, MEMSET and MEMMOVE are, and a plugin of those would
    // be unreachable code, native functions being consulted only after
    // intrinsics.
    public sealed class MemCmpFunction : IXstunitNativeFunction
    {
        // What the vendor returns for a call it cannot perform: a null pointer
        // or a zero length. Deliberately NOT an exception - real source calls
        // MEMCMP with a length computed at run time, and a 0 there is a
        // condition the caller is expected to see in the return value.
        private const int InvalidParameters = 0xFF;

        public string Name => "MEMCMP";

        public object Invoke(NativeCallContext context)
        {
            // Read before RequireBytes rather than through it: an unset POINTER
            // is a plain zero in the value model, not a Pointer, and
            // RequireBytes would raise on it where the vendor answers 16#FF.
            var first = context.RequireArg("pBuf1", 0);
            var second = context.RequireArg("pBuf2", 1);
            var count = context.RequireInt64("n", 2);

            if (count <= 0 || !(first is Pointer) || !(second is Pointer))
                return InvalidParameters;

            var left = context.RequireBytes("pBuf1", 0, (int)count);
            var right = context.RequireBytes("pBuf2", 1, (int)count);

            for (var i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return left[i] < right[i] ? -1 : 1;
            }

            return 0;
        }
    }
}
