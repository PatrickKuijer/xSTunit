using System;
using TcXunit.Interpreter.Extensibility;

namespace TcXunit.Tc2StandardPlugins
{
    // Tc2_Standard WFIND (TcXunit-93l9): 1-based position of the first
    // occurrence of STR2 within STR1, or 0 if STR2 is empty or not found.
    // WSTRING counterpart of FIND (TcXunit-8po.3).
    //
    // Ordinal comparison, like FIND: WSTRING matching in the PLC is
    // code-unit-for-code-unit, so culture-aware collation (which can equate
    // sequences of different lengths, making a 1-based "position" ambiguous)
    // would be wrong here.
    public sealed class WFindFunction : ITcXunitNativeFunction
    {
        public string Name => "WFIND";

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
