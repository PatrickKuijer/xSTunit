using System.Text;
using xStunit.Interpreter.Extensibility;

namespace TcXunit.Tc2StandardPlugins
{
    // Tc2_Standard WCONCAT (TcXunit-93l9): concatenates STR1..STR10, with
    // STR1/STR2 required and STR3..STR10 optional trailing arguments - the
    // same variadic shape the interpreter's CONCAT intrinsic uses
    // (Engine.Expressions.cs's ConcatParamNames, TcXunit-3lt).
    //
    // Unlike its narrow sibling, this one is NOT dead code. CONCAT was
    // deliberately left out of this plugin (TcXunit-8po.1) because the
    // interpreter intercepts it as an intrinsic before native functions are
    // ever consulted. That interception is an exact ordinal name match on
    // "CONCAT", which "WCONCAT" does not hit, and no other intrinsic mentions
    // WCONCAT - so a WCONCAT call falls all the way through to the
    // native-function registry and lands here.
    public sealed class WConcatFunction : ITcXunitNativeFunction
    {
        // Declared parameter names in IEC order, so a caller may supply them
        // positionally or as `STRn := ...` and still get them appended in
        // declared order.
        private static readonly string[] ParamNames =
            { "STR1", "STR2", "STR3", "STR4", "STR5", "STR6", "STR7", "STR8", "STR9", "STR10" };

        public string Name => "WCONCAT";

        public object Invoke(NativeCallContext context)
        {
            var builder = new StringBuilder();

            // STR1/STR2 are required: RequireString faults the call site the
            // same way the CONCAT intrinsic's RequireIntrinsicArg does.
            builder.Append(context.RequireString(ParamNames[0], 0));
            builder.Append(context.RequireString(ParamNames[1], 1));

            for (var position = 2; position < ParamNames.Length; position++)
            {
                if (!context.TryGetArg(ParamNames[position], position, out var value))
                    continue;

                if (!(value is string text))
                {
                    // Mirrors the intrinsic's RequireStringArg: a STRn
                    // argument is ANY_STRING, so a non-string is a type error
                    // rather than something to coerce.
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
