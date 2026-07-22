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
                case "TEST_FINISHED":
                    host.TestFinished();
                    return null;
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
    }
}
