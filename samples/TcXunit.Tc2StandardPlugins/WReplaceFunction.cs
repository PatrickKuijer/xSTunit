using System;
using TcXunit.Interpreter.Extensibility;

namespace TcXunit.Tc2StandardPlugins
{
    // Tc2_Standard WREPLACE (TcXunit-93l9): replaces L characters in STR1
    // starting at the 1-based P with STR2, clamped when P+L exceeds STR1's
    // length. P outside [1, STR1's length] leaves STR1 unchanged apart from
    // whatever a non-positive L would already make a no-op. WSTRING
    // counterpart of REPLACE (TcXunit-8po.8).
    public sealed class WReplaceFunction : ITcXunitNativeFunction
    {
        public string Name => "WREPLACE";

        public object Invoke(NativeCallContext context)
        {
            var str1 = context.RequireString("STR1", 0);
            var str2 = context.RequireString("STR2", 1);
            var l = context.RequireInt32("L", 2);
            var p = context.RequireInt32("P", 3);

            if (p < 1 || p > str1.Length)
                return str1;

            var start = p - 1;
            var available = str1.Length - start;
            var remove = Math.Min(Math.Max(l, 0), available);
            return str1.Substring(0, start) + str2 + str1.Substring(start + remove);
        }
    }
}
