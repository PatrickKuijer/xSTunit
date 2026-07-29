using TcXunit.Interpreter.Extensibility;

namespace TcXunit.SamplePlugins
{
    // Worked example of the TcXunit-6k2 native-function extension point,
    // standing in for Tc2_Utilities' F_CheckSum16.
    //
    // ============================ READ THIS ============================
    // The algorithm below is NOT Beckhoff's. It is a plain 16-bit additive
    // sum, chosen because it is trivially verifiable by hand in a fixture
    // assertion - nothing more. Beckhoff ships Tc2_Utilities compiled-only and
    // does not document F_CheckSum16's internals, so no faithful reproduction
    // is possible from outside; inventing one and calling it F_CheckSum16
    // would make suites pass against numbers a real PLC never produces, which
    // is worse than not supporting the function at all.
    //
    // A real deployment must replace this with the actual algorithm - obtained
    // by measuring the real function's output on a real PLC, or from a vendor
    // spec - in a plugin of its own. That plugin belongs in the codebase that
    // owns the requirement, NOT in this repo: keeping it out is a large part
    // of why this extension point exists (a company's proprietary library
    // behavior stays in the company's own private plugin assembly).
    // ===================================================================
    //
    // Signature mirrored from the real one, as called from ST:
    //   F_CheckSum16(pData : POINTER TO BYTE, nSize : UDINT, nSeed : WORD) : WORD
    public sealed class CheckSum16Function : ITcXunitNativeFunction
    {
        public string Name => "F_CheckSum16";

        public object Invoke(NativeCallContext context)
        {
            // Size first: it determines how many bytes the pointer read is
            // allowed to touch, and RequireBytes needs it up front.
            //
            // Real ST passes this as a UDINT, which the interpreter represents
            // as long - RequireInt32 accepts every integer representation and
            // range-checks, rather than assuming one CLR type.
            var size = context.RequireInt32("nSize", 1);
            var seed = context.RequireInt32("nSeed", 2);
            var data = context.RequireBytes("pData", 0, size);

            var sum = seed;
            foreach (var b in data)
                sum += b;

            // WORD is represented as int in the interpreter's value model (see
            // ITcXunitNativeFunction.Invoke's type table), masked to 16 bits
            // the way the declared return type would be on a real PLC.
            return sum & 0xFFFF;
        }
    }
}
