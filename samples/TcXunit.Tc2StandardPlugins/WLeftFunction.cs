using System;
using TcXunit.Interpreter.Extensibility;

namespace TcXunit.Tc2StandardPlugins
{
    // Tc2_Standard WLEFT (TcXunit-93l9): leftmost SIZE characters of STR,
    // clamped to [0, STR's length]. WSTRING counterpart of LEFT, with the
    // same clamp-rather-than-throw convention (TcXunit-8po.5).
    public sealed class WLeftFunction : ITcXunitNativeFunction
    {
        public string Name => "WLEFT";

        public object Invoke(NativeCallContext context)
        {
            var str = context.RequireString("STR", 0);
            var size = context.RequireInt32("SIZE", 1);

            var take = Math.Min(Math.Max(size, 0), str.Length);
            return str.Substring(0, take);
        }
    }
}
