using System.Text;
using xStunit.Interpreter.Extensibility;

namespace xStunit.StandardStringPlugins
{
    // Tc2_Standard WCONCAT: STR1/STR2 required, STR3..STR10 optional trailing
    // arguments - the same variadic shape the interpreter's CONCAT intrinsic
    // uses (Engine.Expressions.cs's ConcatParamNames).
    //
    // Narrow CONCAT is deliberately not in this plugin, because the
    // interpreter intercepts it as an intrinsic before native functions are
    // consulted at all. That interception is an exact ordinal match on
    // "CONCAT", which "WCONCAT" does not hit, and no other intrinsic claims
    // WCONCAT - so a WCONCAT call does reach the native-function registry and
    // land here.
    public sealed class WConcatFunction : IXstunitNativeFunction
    {
        // In declared IEC order: a caller may supply arguments positionally or
        // as `STRn := ...`, and the index into this array is what makes both
        // forms append in declared order.
        private static readonly string[] ParamNames =
            { "STR1", "STR2", "STR3", "STR4", "STR5", "STR6", "STR7", "STR8", "STR9", "STR10" };

        public string Name => "WCONCAT";

        public object Invoke(NativeCallContext context)
        {
            var builder = new StringBuilder();

            builder.Append(context.RequireString(ParamNames[0], 0));
            builder.Append(context.RequireString(ParamNames[1], 1));

            for (var position = 2; position < ParamNames.Length; position++)
            {
                if (!context.TryGetArg(ParamNames[position], position, out var value))
                    continue;

                if (!(value is string text))
                {
                    // A STRn argument is ANY_STRING, so a non-string is a type
                    // error rather than something to coerce - the same call the
                    // intrinsic's RequireStringArg makes.
                    throw new System.InvalidOperationException(
                        $"{context.FunctionName} argument '{ParamNames[position]}' must be a WSTRING, " +
                        $"got {(value == null ? "null" : value.GetType().Name)}");
                }

                builder.Append(text);
            }

            return builder.ToString();
        }
    }
}
