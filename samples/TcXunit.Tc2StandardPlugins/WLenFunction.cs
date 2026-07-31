using TcXunit.Interpreter.Extensibility;

namespace TcXunit.Tc2StandardPlugins
{
    // Tc2_Standard WLEN (TcXunit-93l9): character count of STR, the WSTRING
    // counterpart of LEN.
    //
    // "Character" here means one UTF-16 code unit, which is what TwinCAT's
    // WSTRING stores and counts, and what .NET's string.Length reports - so
    // the narrow implementation's arithmetic carries over unchanged. A
    // non-BMP character (surrogate pair) therefore counts as 2 in both
    // TwinCAT and here, deliberately: the alternative (counting Unicode
    // scalar values) would disagree with the PLC.
    public sealed class WLenFunction : ITcXunitNativeFunction
    {
        public string Name => "WLEN";

        public object Invoke(NativeCallContext context)
        {
            var str = context.RequireString("STR", 0);
            return str.Length;
        }
    }
}
