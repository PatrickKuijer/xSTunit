using System;
using TcXunit.Interpreter.Extensibility;

namespace TcXunit.Tc2StandardPlugins
{
    // Tc2_Standard WRIGHT (TcXunit-93l9): rightmost SIZE characters of STR,
    // clamped to [0, STR's length]. WSTRING counterpart of RIGHT
    // (TcXunit-8po.9).
    public sealed class WRightFunction : ITcXunitNativeFunction
    {
        public string Name => "WRIGHT";

        public object Invoke(NativeCallContext context)
        {
            var str = context.RequireString("STR", 0);
            var size = context.RequireInt32("SIZE", 1);

            var take = Math.Min(Math.Max(size, 0), str.Length);
            return str.Substring(str.Length - take);
        }
    }
}
