using System;
using System.Collections.Generic;
using TcXunit.Runner.TcUnitStub;

namespace TcXunit.Interpreter
{
    // Maps calls that fall through to the TcUnit.FB_TestSuite native-stub
    // boundary onto TcUnitSuiteHost. Grow-on-demand: only the surface the
    // FB_CounterTests fixture exercises (TcXunit-w5x.7/.8).
    public static class NativeMethodBridge
    {
        public static object Invoke(
            TcUnitSuiteHost host,
            string methodName,
            IReadOnlyList<object> positional,
            IReadOnlyDictionary<string, object> named,
            IReadOnlyDictionary<string, string> anyTypeNames = null)
        {
            switch (methodName)
            {
                case "TEST":
                    host.Test((string)positional[0]);
                    return null;
                case "TEST_ORDERED":
                    return host.TestOrdered(ResolveTestName(positional, named));
                case "TEST_FINISHED":
                    host.TestFinished();
                    return null;
                case "TEST_FINISHED_NAMED":
                    host.TestFinishedNamed(ResolveTestName(positional, named));
                    return null;
                case "IS_TEST_FINISHED":
                    return host.IsTestFinished(ResolveTestName(positional, named));
                case "AssertTrue":
                    {
                        var args = ResolveArgs(ConditionAssertParamNames, positional, named);
                        host.AssertTrueCall((bool)args["Condition"], (string)args["Message"]);
                        return null;
                    }
                case "AssertFalse":
                    {
                        var args = ResolveArgs(ConditionAssertParamNames, positional, named);
                        host.AssertFalseCall((bool)args["Condition"], (string)args["Message"]);
                        return null;
                    }
                case "AssertEquals":
                    {
                        // TcXunit-gd2.5: type-erased AssertEquals(Expected:
                        // ANY, Actual: ANY, Message) dispatcher. Expected/
                        // Actual's declared IEC type names are resolved by
                        // Engine.Invocation.cs (from the argument
                        // expression, before evaluation) and passed in via
                        // anyTypeNames, since NativeMethodBridge only ever
                        // sees the already-evaluated CLR values here -
                        // which, for several IEC scalar types, are
                        // ambiguous/shared CLR representations (see
                        // ScalarAssertType.cs) and can't be told apart on
                        // their own.
                        var args = ResolveArgs(ScalarAssertParamNames, positional, named);
                        string expectedTypeName = null;
                        string actualTypeName = null;
                        anyTypeNames?.TryGetValue("Expected", out expectedTypeName);
                        anyTypeNames?.TryGetValue("Actual", out actualTypeName);
                        host.AssertEqualsAnyCall(expectedTypeName, args["Expected"], actualTypeName, args["Actual"], (string)args["Message"]);
                        return null;
                    }
                default:
                    // TcXunit-gd2.11: table-driven scalar dispatch. Parses
                    // the AssertEquals_<TYPE> suffix from the native call
                    // name, looks up the ScalarAssertType registry to know
                    // whether a Delta arg is expected, and calls the one
                    // generic host method instead of switching per type -
                    // adding a new scalar type means adding a registry entry,
                    // not a new case here.
                    if (methodName.StartsWith("AssertEquals_", StringComparison.Ordinal))
                    {
                        var typeName = methodName.Substring("AssertEquals_".Length);
                        if (ScalarAssertType.Registry.TryGetValue(typeName, out var scalarType))
                        {
                            var paramNames = scalarType.HasDelta ? ScalarAssertWithDeltaParamNames : ScalarAssertParamNames;
                            var args = ResolveArgs(paramNames, positional, named);
                            var delta = scalarType.HasDelta ? args["Delta"] : null;
                            host.AssertEqualsScalar(typeName, args["Expected"], args["Actual"], delta, (string)args["Message"]);
                            return null;
                        }
                    }
                    throw new NotSupportedException(
                        $"TcUnit native call '{methodName}' isn't supported yet (grow-on-demand, TcXunit-w5x.12).");
            }
        }

        private static readonly string[] ConditionAssertParamNames = { "Condition", "Message" };
        private static readonly string[] ScalarAssertParamNames = { "Expected", "Actual", "Message" };
        private static readonly string[] ScalarAssertWithDeltaParamNames = { "Expected", "Actual", "Delta", "Message" };

        // TEST_ORDERED/TEST_FINISHED_NAMED/IS_TEST_FINISHED take a single
        // TestName input in upstream - accept it either positionally or by
        // that name (TcXunit-k28.7).
        private static string ResolveTestName(IReadOnlyList<object> positional, IReadOnlyDictionary<string, object> named)
        {
            if (named.TryGetValue("TestName", out var byName))
                return (string)byName;
            return (string)positional[0];
        }

        // TcXunit-bda: Assert*/AssertEquals_* calls are native intrinsics
        // like MEMCPY/MEMSET (no VarBlockParser decls for BindParams to
        // reconcile named args against - see Engine.ResolveIntrinsicArgs),
        // so real ST callers that pass args positionally (e.g.
        // AssertTrue(cond, 'msg')) hit named[...] directly and throw
        // KeyNotFoundException. Resolve each declared param by name first,
        // falling back to positional args in left-to-right order for
        // params not given by name (mirrors BindParams' shared posIndex).
        private static IReadOnlyDictionary<string, object> ResolveArgs(
            IReadOnlyList<string> paramNamesInDeclOrder,
            IReadOnlyList<object> positional,
            IReadOnlyDictionary<string, object> named)
        {
            var resolved = new Dictionary<string, object>();
            var posIndex = 0;
            foreach (var paramName in paramNamesInDeclOrder)
            {
                if (ArgBinder.TryResolveArg(
                    paramName,
                    name => named.TryGetValue(name, out var v) ? v : null,
                    positional,
                    ref posIndex,
                    out var value))
                    resolved[paramName] = value;
            }
            return resolved;
        }
    }
}
