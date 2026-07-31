using System;
using TcXunit.Interpreter.Extensibility;

namespace TcXunit.Tc2StandardPlugins
{
    // Tc2_Standard WINSERT (TcXunit-93l9): inserts STR2 into STR1 immediately
    // after the 1-based POS. POS is clamped to [0, STR1's length]; POS = 0
    // inserts before the first character. WSTRING counterpart of INSERT
    // (TcXunit-8po.4).
    public sealed class WInsertFunction : ITcXunitNativeFunction
    {
        public string Name => "WINSERT";

        public object Invoke(NativeCallContext context)
        {
            var str1 = context.RequireString("STR1", 0);
            var str2 = context.RequireString("STR2", 1);
            var pos = context.RequireInt32("POS", 2);

            var at = Math.Min(Math.Max(pos, 0), str1.Length);
            return str1.Substring(0, at) + str2 + str1.Substring(at);
        }
    }
}
