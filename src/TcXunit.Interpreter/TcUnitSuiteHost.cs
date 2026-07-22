using System.Collections.Generic;
using TcXunit.Runner.TcUnitStub;

namespace TcXunit.Interpreter
{
    // Native-stub boundary (TcXunit-w5x.7): backs an interpreted FB that
    // EXTENDS TcUnit.FB_TestSuite. The interpreter drives TEST()/
    // AssertEquals_INT()/etc itself as it executes the suite's interpreted
    // statements, instead of Body() being a compiled override - Body() is a
    // no-op here on purpose.
    public sealed class TcUnitSuiteHost : FB_TestSuite
    {
        protected override void Body()
        {
        }

        public void Test(string name) => TEST(name);

        public void TestFinished() => TEST_FINISHED();

        public void AssertEqualsInt(int expected, int actual, string message) =>
            AssertEquals_INT(expected, actual, message);

        public void AssertTrueCall(bool condition, string message) =>
            AssertTrue(condition, message);

        public void AssertFalseCall(bool condition, string message) =>
            AssertFalse(condition, message);

        public void AssertEqualsBool(bool expected, bool actual, string message) =>
            AssertEquals_BOOL(expected, actual, message);

        public void AssertEqualsString(string expected, string actual, string message) =>
            AssertEquals_STRING(expected, actual, message);

        public void AssertEqualsReal(double expected, double actual, double delta, string message) =>
            AssertEquals_REAL(expected, actual, delta, message);

        public IReadOnlyList<TestCaseResult> Collect() => Run();
    }
}
