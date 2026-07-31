using TcXunit.Interpreter.Extensibility;

namespace TcXunit.Tc2StandardPlugins
{
    // Tc2_Standard LEN (TcXunit-8po.6): character count of STR.
    public sealed class LenFunction : ITcXunitNativeFunction
    {
        public string Name => "LEN";

        public object Invoke(NativeCallContext context)
        {
            var str = context.RequireString("STR", 0);
            return str.Length;
        }
    }
}
