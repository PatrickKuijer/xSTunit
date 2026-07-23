using System;
using System.Collections.Generic;

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
            IReadOnlyDictionary<string, object> named)
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
                case "AssertEquals_INT":
                    {
                        var args = ResolveArgs(IntAssertParamNames, positional, named);
                        host.AssertEqualsInt((int)args["Expected"], (int)args["Actual"], (string)args["Message"]);
                        return null;
                    }
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
                case "AssertEquals_BOOL":
                    {
                        var args = ResolveArgs(BoolAssertParamNames, positional, named);
                        host.AssertEqualsBool((bool)args["Expected"], (bool)args["Actual"], (string)args["Message"]);
                        return null;
                    }
                case "AssertEquals_STRING":
                    {
                        var args = ResolveArgs(StringAssertParamNames, positional, named);
                        host.AssertEqualsString((string)args["Expected"], (string)args["Actual"], (string)args["Message"]);
                        return null;
                    }
                case "AssertEquals_REAL":
                    {
                        var args = ResolveArgs(RealAssertParamNames, positional, named);
                        host.AssertEqualsReal(
                            Convert.ToDouble(args["Expected"]),
                            Convert.ToDouble(args["Actual"]),
                            Convert.ToDouble(args["Delta"]),
                            (string)args["Message"]);
                        return null;
                    }
                default:
                    throw new NotSupportedException(
                        $"TcUnit native call '{methodName}' isn't supported yet (grow-on-demand, TcXunit-w5x.12).");
            }
        }

        private static readonly string[] ConditionAssertParamNames = { "Condition", "Message" };
        private static readonly string[] IntAssertParamNames = { "Expected", "Actual", "Message" };
        private static readonly string[] BoolAssertParamNames = { "Expected", "Actual", "Message" };
        private static readonly string[] StringAssertParamNames = { "Expected", "Actual", "Message" };
        private static readonly string[] RealAssertParamNames = { "Expected", "Actual", "Delta", "Message" };

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
                if (named.TryGetValue(paramName, out var value))
                    resolved[paramName] = value;
                else if (posIndex < positional.Count)
                    resolved[paramName] = positional[posIndex++];
            }
            return resolved;
        }
    }
}
