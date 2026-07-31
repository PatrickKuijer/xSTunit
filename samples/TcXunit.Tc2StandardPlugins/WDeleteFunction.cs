using System;
using TcXunit.Interpreter.Extensibility;

namespace TcXunit.Tc2StandardPlugins
{
    // Tc2_Standard WDELETE (TcXunit-93l9): removes LEN characters from STR
    // starting at the 1-based POS, clamped when POS+LEN exceeds STR's length.
    // POS outside [1, STR's length], or a non-positive LEN, leaves STR
    // unchanged. WSTRING counterpart of DELETE (TcXunit-8po.2).
    public sealed class WDeleteFunction : ITcXunitNativeFunction
    {
        public string Name => "WDELETE";

        public object Invoke(NativeCallContext context)
        {
            var str = context.RequireString("STR", 0);
            var len = context.RequireInt32("LEN", 1);
            var pos = context.RequireInt32("POS", 2);

            if (pos < 1 || pos > str.Length || len <= 0)
                return str;

            var start = pos - 1;
            var available = str.Length - start;
            var remove = Math.Min(len, available);
            return str.Remove(start, remove);
        }
    }
}
