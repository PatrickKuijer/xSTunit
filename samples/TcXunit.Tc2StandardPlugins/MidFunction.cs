using System;
using TcXunit.Interpreter.Extensibility;

namespace TcXunit.Tc2StandardPlugins
{
    // Tc2_Standard MID (TcXunit-8po.7): LEN characters of STR starting at the
    // 1-based POS, clamped when POS+LEN exceeds STR's length. POS outside
    // [1, STR's length], or a non-positive LEN, yields an empty string.
    public sealed class MidFunction : ITcXunitNativeFunction
    {
        public string Name => "MID";

        public object Invoke(NativeCallContext context)
        {
            var str = context.RequireString("STR", 0);
            var len = context.RequireInt32("LEN", 1);
            var pos = context.RequireInt32("POS", 2);

            if (pos < 1 || pos > str.Length || len <= 0)
                return string.Empty;

            var start = pos - 1;
            var available = str.Length - start;
            var take = Math.Min(len, available);
            return str.Substring(start, take);
        }
    }
}
