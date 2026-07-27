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

        public bool TestOrdered(string name) => TEST_ORDERED(name);

        public void TestFinished() => TEST_FINISHED();

        public void TestFinishedNamed(string name) => TEST_FINISHED_NAMED(name);

        public bool IsTestFinished(string name) => IS_TEST_FINISHED(name);

        public void AssertTrueCall(bool condition, string message) =>
            AssertTrue(condition, message);

        public void AssertFalseCall(bool condition, string message) =>
            AssertFalse(condition, message);

        // Table-driven scalar dispatch (TcXunit-gd2.11): collapses the 4
        // named AssertEquals<Type> wrappers this used to have (Int/Bool/
        // String/Real) into one generic forward. No compile-time-name
        // constraint is needed on this side - only NativeMethodBridge calls
        // it, keyed off the AssertEquals_<TYPE> suffix it parsed from the
        // native call name.
        public new void AssertEqualsScalar(string typeName, object expected, object actual, object delta, string message) =>
            base.AssertEqualsScalar(typeName, expected, actual, delta, message);

        public IReadOnlyList<TestCaseResult> Collect() => Run();
    }
}
