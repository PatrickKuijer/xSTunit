using xStunit.Interpreter;
using xStunit.Interpreter.Extensibility;

namespace xStunit.SystemLibraryPlugins
{
    // Tc2_System's F_ToASC:
    //   FUNCTION F_ToASC : BYTE
    //   VAR_INPUT str : STRING; END_VAR
    // the code of the string's FIRST character, with an empty STRING
    // documented to deliver zero.
    //
    // "First character, rest ignored" rather than "requires a single
    // character": the declared parameter is a plain STRING, and real source
    // leans on that - F_ToASC(sLine) to classify a record by its leading byte
    // is the common shape. Rejecting a longer argument would break those calls
    // for a strictness the vendor doesn't have.
    public sealed class ToAscFunction : IXstunitNativeFunction
    {
        public string Name => "F_ToASC";

        public object Invoke(NativeCallContext context)
        {
            var text = context.RequireString("str", 0);

            // Latin-1 through NarrowStringByte rather than a cast, so this
            // agrees with s[0] and with the MEMCPY byte image on what the first
            // byte of a narrow STRING is - and raises on text that has no
            // one-byte encoding instead of silently returning its low half.
            //
            // Widened to int on the way out because that is how the value model
            // boxes BYTE, along with every other narrow integer type. Returning
            // the byte NarrowStringByte hands back would differ from the
            // empty-string branch's 0 and read as the wrong type to
            // AssertEquals(ANY), which resolves on the box.
            return text.Length == 0 ? 0 : (int)NarrowStringByte.FromChar(text[0]);
        }
    }

    // F_ToASC's inverse:
    //   FUNCTION F_ToCHR : STRING
    //   VAR_INPUT c : BYTE; END_VAR
    public sealed class ToChrFunction : IXstunitNativeFunction
    {
        public string Name => "F_ToCHR";

        public object Invoke(NativeCallContext context)
        {
            var code = context.RequireInt32("c", 0) & 0xFF;

            // A STRING is NUL-terminated, so code 0 is not a one-character
            // string holding NUL - it is the empty string, and a round trip
            // F_ToASC(F_ToCHR(0)) lands back on 0 through the empty-string rule
            // rather than through a NUL character that could not be stored.
            return code == 0 ? string.Empty : NarrowStringByte.ToChar(code).ToString();
        }
    }
}
