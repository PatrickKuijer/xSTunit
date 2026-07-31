using System;
using TcXunit.Interpreter.Extensibility;

namespace TcXunit.Tc2StandardPlugins
{
    // Tc2_Standard FIND (TcXunit-8po.3): 1-based position of the first
    // occurrence of STR2 within STR1, or 0 if STR2 is empty or not found.
    public sealed class FindFunction : ITcXunitNativeFunction
    {
        public string Name => "FIND";

        public object Invoke(NativeCallContext context)
        {
            var str1 = context.RequireString("STR1", 0);
            var str2 = context.RequireString("STR2", 1);

            if (string.IsNullOrEmpty(str2))
                return 0;

            var index = str1.IndexOf(str2, StringComparison.Ordinal);
            return index < 0 ? 0 : index + 1;
        }
    }
}
