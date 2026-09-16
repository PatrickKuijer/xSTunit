using System;
using System.Collections.Generic;
using System.Text;
using xStunit.Interpreter.Extensibility;

namespace xStunit.SystemLibraryPlugins
{
    // Tc2_System's F_CreateAmsNetId. Documented, so this is the real behavior
    // rather than a stand-in:
    //   FUNCTION F_CreateAmsNetId : T_AmsNetID
    //   VAR_INPUT nIds : T_AmsNetIdArr; END_VAR   (* ARRAY[0..5] OF BYTE *)
    // building the dotted decimal form '127.16.17.3.1.1'.
    //
    // A STRING, not a packed six-byte value: T_AmsNetID is STRING(23), and
    // everything downstream in real source (an ADS FB's sNetId input, a
    // comparison against '' for "local") expects that text. Returning the
    // octets would type-check and then mismatch every one of those.
    public sealed class CreateAmsNetIdFunction : IXstunitNativeFunction
    {
        // The vendor input type is fixed at six octets. A different length is a
        // call that could not have compiled in TwinCAT, so it is rejected here
        // rather than silently producing a net ID of the wrong shape.
        private const int OctetCount = 6;

        public string Name => "F_CreateAmsNetId";

        public object Invoke(NativeCallContext context)
        {
            var octets = context.RequireByteArray("nIds", 0);
            if (octets.Length != OctetCount)
            {
                throw new InvalidOperationException(
                    $"F_CreateAmsNetId expects an ARRAY[0..5] OF BYTE ({OctetCount} octets), " +
                    $"got {octets.Length}");
            }

            var text = new StringBuilder(23);
            for (var i = 0; i < octets.Length; i++)
            {
                if (i > 0)
                    text.Append('.');
                text.Append(octets[i]);
            }

            return text.ToString();
        }
    }
}
