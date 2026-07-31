using System.Collections.Generic;
using System.Linq;
using xStunit.Runner.TcUnitStub;

namespace xStunit.Interpreter
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

        // Type-erased AssertEquals(ANY) dispatcher (TcXunit-gd2.5): thin
        // forward, same shape as AssertEqualsScalar above - NativeMethodBridge
        // has already resolved Expected/Actual's declared IEC type names
        // (via Engine.Invocation.cs, before they were evaluated away to bare
        // CLR values) by the time this is called.
        public void AssertEqualsAnyCall(
            string expectedTypeName, object expectedValue,
            string actualTypeName, object actualValue,
            string message) =>
            AssertEqualsAny(expectedTypeName, expectedValue, actualTypeName, actualValue, message);

        // ARRAY[*] equality dispatch (TcXunit-gd2.6): flattens each
        // ArrayValue's per-dimension (Lo, Hi) bounds into the plain
        // size/lower-bound primitive lists AssertArrayEquals (Runner
        // project, no ArrayValue reference) expects, then forwards its own
        // already-flattened Elements storage straight through - no copy
        // needed since AssertArrayEquals only reads. delta is null for the
        // 12 non-float types; REAL/LREAL (TcXunit-gd2.7) pass their boxed
        // Delta VAR_INPUT through here.
        public void AssertArrayEqualsCall(string typeName, ArrayValue expected, ArrayValue actual, object delta, string message) =>
            AssertArrayEquals(
                typeName,
                DimensionSizes(expected), DimensionLowerBounds(expected), expected.Elements,
                DimensionSizes(actual), DimensionLowerBounds(actual), actual.Elements,
                message, delta);

        private static List<int> DimensionSizes(ArrayValue array) =>
            array.Dimensions.Select(d => d.Hi - d.Lo + 1).ToList();

        private static List<int> DimensionLowerBounds(ArrayValue array) =>
            array.Dimensions.Select(d => d.Lo).ToList();

        public IReadOnlyList<TestCaseResult> Collect() => Run();

        // TcXunit-3tx.3: the two halves of "charge this fault to the open test
        // instead of killing the suite". Thin forwards, same shape as every
        // other member here - Engine (not this type) decides when a fault is
        // one test's problem rather than the suite's.
        public bool HasOpenTestCase => HasOpenTest;

        public void AbortCurrentTestCase(AssertionFailure failure) => AbortCurrentTest(failure);

        // TcXunit-3tx.2: announces which native call is about to run and where
        // it is written, so a failure it records can name both. Called for
        // every TcUnit native call, not just asserts - TEST()/TEST_FINISHED()
        // simply never reach Fail(), and gating on "is this an assert name"
        // here would duplicate NativeMethodBridge's dispatch table.
        public void EnterNativeCall(string methodName, AssertSite site)
        {
            CurrentAssert = methodName;
            CurrentSite = site;
        }
    }
}
