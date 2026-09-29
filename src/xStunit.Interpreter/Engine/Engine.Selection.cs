using System;
using System.Collections.Generic;
using System.Linq;

namespace xStunit.Interpreter
{
    public sealed partial class Engine
    {
        private static readonly string[] SelectionFunctionNames = { "MIN", "MAX", "LIMIT" };

        private static readonly string[] LimitParamNames = { "MN", "IN", "MX" };

        private bool TryEvaluateSelection(
            string methodName,
            IReadOnlyList<Expr> positionalArgs,
            IReadOnlyList<NamedArg> namedArgs,
            Frame frame,
            out object result)
        {
            result = null;
            var function = IecIdentifier.Canonical(SelectionFunctionNames, methodName);
            if (function == null)
                return false;

            if (function == "LIMIT")
            {
                var args = BindLimitArgs(positionalArgs, namedArgs);
                var lower = Evaluate(args["MN"], frame);
                var input = Evaluate(args["IN"], frame);
                var upper = Evaluate(args["MX"], frame);
                result = Min(function, Max(function, input, lower, frame), upper, frame);
                return true;
            }

            if (namedArgs.Count > 0)
                throw new InvalidOperationException($"{function} takes positional arguments only");
            if (positionalArgs.Count < 2)
                throw new InvalidOperationException(
                    $"{function} requires at least 2 arguments, got {positionalArgs.Count}");

            var values = positionalArgs.Select(a => Evaluate(a, frame)).ToArray();
            var pick = function == "MIN" ? (Func<object, object, object>)((a, b) => Min(function, a, b, frame))
                                         : (a, b) => Max(function, a, b, frame);
            result = values.Skip(1).Aggregate(RequireOrdered(function, values[0]), pick);
            return true;
        }

        private static IReadOnlyDictionary<string, Expr> BindLimitArgs(
            IReadOnlyList<Expr> positionalArgs, IReadOnlyList<NamedArg> namedArgs)
        {
            var expected = string.Join(", ", LimitParamNames);
            var bound = new Dictionary<string, Expr>(IecIdentifier.Comparer);

            foreach (var named in namedArgs)
            {
                var param = IecIdentifier.Canonical(LimitParamNames, named.Name);
                if (param == null)
                    throw new InvalidOperationException(
                        $"LIMIT has no parameter '{named.Name}' (parameters: {expected})");
                if (bound.ContainsKey(param))
                    throw new InvalidOperationException($"LIMIT parameter '{param}' given more than once");
                bound[param] = named.Value;
            }

            var free = LimitParamNames.Where(p => !bound.ContainsKey(p)).ToList();
            if (positionalArgs.Count > free.Count)
                throw new InvalidOperationException(
                    $"LIMIT takes exactly 3 arguments ({expected}), got {positionalArgs.Count + namedArgs.Count}");

            for (var i = 0; i < positionalArgs.Count; i++)
                bound[free[i]] = positionalArgs[i];

            var missing = LimitParamNames.FirstOrDefault(p => !bound.ContainsKey(p));
            if (missing != null)
                throw new InvalidOperationException($"LIMIT missing argument '{missing}' (parameters: {expected})");

            return bound;
        }

        private object Min(string function, object left, object right, Frame frame) =>
            Pick(function, "<", left, right, frame);

        private object Max(string function, object left, object right, Frame frame) =>
            Pick(function, ">", left, right, frame);

        private object Pick(string function, string preferOp, object current, object candidate, Frame frame)
        {
            RequireOrdered(function, current);
            RequireOrdered(function, candidate);
            if ((current is string) != (candidate is string))
                throw new NotSupportedException(
                    $"{function} cannot order {current.GetType().Name} against {candidate.GetType().Name}");

            if (current.GetType() != candidate.GetType())
            {
                try
                {
                    (current, candidate) = NumericCoercion.Promote(current, candidate);
                }
                catch (NotSupportedException ex)
                {
                    throw new NotSupportedException($"{function} cannot order mixed operand types: {ex.Message}", ex);
                }
            }

            return (bool)EvaluateBinaryValues(preferOp, candidate, current, frame) ? candidate : current;
        }

        private static object RequireOrdered(string function, object value)
        {
            if (value is int || value is uint || value is long || value is ulong ||
                value is float || value is double || value is string)
                return value;

            throw new NotSupportedException(
                $"{function} requires ordered arguments (numeric, TIME/DATE family, STRING or enum), got {value?.GetType().Name ?? "null"}");
        }
    }
}
