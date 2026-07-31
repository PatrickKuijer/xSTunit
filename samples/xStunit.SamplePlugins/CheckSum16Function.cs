using xStunit.Interpreter.Extensibility;

namespace xStunit.SamplePlugins
{
    // Worked example of the native-function extension point, standing in for
    // Tc2_Utilities' F_CheckSum16.
    //
    // The algorithm below is NOT Beckhoff's - it is a plain 16-bit additive
    // sum, picked only because a fixture assertion can verify it by hand.
    // Beckhoff ships Tc2_Utilities compiled-only and does not document
    // F_CheckSum16's internals, so no faithful reproduction is possible from
    // outside, and a plausible invention under the real name would make suites
    // pass against numbers a real PLC never produces. A deployment that needs
    // the real values must supply the measured algorithm in a plugin of its
    // own, in the codebase that owns the requirement.
    //
    // Signature mirrored from the real one, as called from ST:
    //   F_CheckSum16(pData : POINTER TO BYTE, nSize : UDINT, nSeed : WORD) : WORD
    public sealed class CheckSum16Function : IXstunitNativeFunction
    {
        public string Name => "F_CheckSum16";

        public object Invoke(NativeCallContext context)
        {
            // Size is read before the buffer because it bounds how many bytes
            // the pointer read may touch. RequireInt32 rather than a cast: ST
            // passes nSize as a UDINT, which the interpreter represents as
            // long, and the accessor accepts every integer representation and
            // range-checks it.
            var size = context.RequireInt32("nSize", 1);
            var seed = context.RequireInt32("nSeed", 2);
            var data = context.RequireBytes("pData", 0, size);

            var sum = seed;
            foreach (var b in data)
                sum += b;

            // WORD is int in the interpreter's value model (see
            // IXstunitNativeFunction.Invoke's type table), so the declared
            // return type does not narrow the sum - mask it the way a real PLC
            // would.
            return sum & 0xFFFF;
        }
    }
}
