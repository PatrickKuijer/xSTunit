using System.Collections.Generic;

namespace xStunit.Interpreter
{
    // Named-arg-first, positional-fallback binding, shared by BindParams
    // (Engine.Invocation.cs), ResolveIntrinsicArgs (Engine.Expressions.cs)
    // and NativeMethodBridge.ResolveArgs.
    //
    // posIndex runs across the whole declared param list rather than tracking
    // a param's declared position: a positional arg's slot depends on how many
    // preceding params were supplied by name. T is the arg shape - Expr for
    // unevaluated call args, object for already-evaluated native-call args.
    internal static class ArgBinder
    {
        // tryGetNamed returns null when paramName has no explicit named arg;
        // T is a reference type here, so null is a safe "not found" sentinel.
        public static bool TryResolveArg<T>(
            string paramName,
            System.Func<string, T> tryGetNamed,
            IReadOnlyList<T> positionalArgs,
            ref int posIndex,
            out T value)
            where T : class
        {
            var named = tryGetNamed(paramName);
            if (named != null)
            {
                value = named;
                return true;
            }

            if (posIndex < positionalArgs.Count)
            {
                value = positionalArgs[posIndex++];
                return true;
            }

            value = null;
            return false;
        }

        // The tryGetNamed half for unevaluated call args. Matched
        // case-insensitively, as IEC identifiers are: a miss here does not
        // fail, it silently falls back to positional binding.
        public static Expr FindNamed(IReadOnlyList<NamedArg> namedArgs, string paramName)
        {
            foreach (var arg in namedArgs)
                if (string.Equals(arg.Name, paramName, System.StringComparison.OrdinalIgnoreCase))
                    return arg.Value;

            return null;
        }
    }
}
