using System;
using TcXunit.Interpreter.Extensibility;

namespace TcXunit.Tc2StandardPlugins
{
    // Tc2_Standard LEFT (TcXunit-8po.5): leftmost SIZE characters of STR,
    // clamped to [0, STR's length].
    public sealed class LeftFunction : ITcXunitNativeFunction
    {
        public string Name => "LEFT";

        public object Invoke(NativeCallContext context)
        {
            var str = context.RequireString("STR", 0);
            var size = context.RequireInt32("SIZE", 1);

            var take = Math.Min(Math.Max(size, 0), str.Length);
            return str.Substring(0, take);
        }
    }
}
