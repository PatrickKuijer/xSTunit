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
                    host.AssertEqualsInt((int)named["Expected"], (int)named["Actual"], (string)named["Message"]);
                    return null;
                case "AssertTrue":
                    host.AssertTrueCall((bool)named["Condition"], (string)named["Message"]);
                    return null;
                case "AssertFalse":
                    host.AssertFalseCall((bool)named["Condition"], (string)named["Message"]);
                    return null;
                case "AssertEquals_BOOL":
                    host.AssertEqualsBool((bool)named["Expected"], (bool)named["Actual"], (string)named["Message"]);
                    return null;
                case "AssertEquals_STRING":
                    host.AssertEqualsString((string)named["Expected"], (string)named["Actual"], (string)named["Message"]);
                    return null;
                case "AssertEquals_REAL":
                    host.AssertEqualsReal(
                        Convert.ToDouble(named["Expected"]),
                        Convert.ToDouble(named["Actual"]),
                        Convert.ToDouble(named["Delta"]),
                        (string)named["Message"]);
                    return null;
                default:
                    throw new NotSupportedException(
                        $"TcUnit native call '{methodName}' isn't supported yet (grow-on-demand, TcXunit-w5x.12).");
            }
        }

        // TEST_ORDERED/TEST_FINISHED_NAMED/IS_TEST_FINISHED take a single
        // TestName input in upstream - accept it either positionally or by
        // that name (TcXunit-k28.7).
        private static string ResolveTestName(IReadOnlyList<object> positional, IReadOnlyDictionary<string, object> named)
        {
            if (named.TryGetValue("TestName", out var byName))
                return (string)byName;
            return (string)positional[0];
        }
    }
}
