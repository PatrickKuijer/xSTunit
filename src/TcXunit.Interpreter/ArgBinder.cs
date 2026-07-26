using System.Collections.Generic;

namespace TcXunit.Interpreter
{
    // Shared named-arg-first/positional-fallback resolution (TcXunit-6af.3).
    // BindParams (Engine.Invocation.cs), ResolveIntrinsicArgs
    // (Engine.Expressions.cs), and NativeMethodBridge.ResolveArgs each
    // reimplemented the same algorithm independently and had already drifted
    // once. All three walk their declared params left-to-right, take an
    // explicit named arg if present, otherwise consume the next unconsumed
    // positional arg (posIndex is shared/running across params, not tied to
    // declared position) - this is that one primitive, parameterized over the
    // arg/value shape (Expr for unevaluated call args, object for already-
    // evaluated native-call args).
    internal static class ArgBinder
    {
        // tryGetNamed returns null when paramName has no explicit named arg.
        // T is always a reference type here (Expr, object), so null is a safe
        // "not found" sentinel.
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
    }
}
