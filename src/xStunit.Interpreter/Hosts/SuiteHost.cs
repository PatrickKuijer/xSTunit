using System.Collections.Generic;
using System.Linq;
using xStunit.Runner.TcUnitStub;

namespace xStunit.Interpreter
{
    // Backs an interpreted FB that EXTENDS TcUnit.FB_TestSuite, exposing the
    // stub's protected assert/test surface to NativeMethodBridge.
    //
    // Body() is a deliberate no-op: upstream expects a compiled override to
    // run the tests, but here the interpreter executes the suite's ST
    // statements itself and calls TEST()/AssertEquals_INT()/etc as it goes.
    public sealed class SuiteHost : FB_TestSuite
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

        // typeName carries the IEC type the ST caller wrote, since one generic
        // forward replaces what would otherwise be a wrapper per scalar type.
        // Only NativeMethodBridge calls this, keyed off the AssertEquals_<TYPE>
        // suffix it parsed from the native call name.
        public new void AssertEqualsScalar(string typeName, object expected, object actual, object delta, string message) =>
            base.AssertEqualsScalar(typeName, expected, actual, delta, message);

        // The type names must be passed in because the values arrive here
        // already evaluated to bare CLR values, which for several IEC scalar
        // types are indistinguishable. Engine.Invocation.cs resolves them from
        // the argument expressions before evaluation.
        public void AssertEqualsAnyCall(
            string expectedTypeName, object expectedValue,
            string actualTypeName, object actualValue,
            string message) =>
            AssertEqualsAny(expectedTypeName, expectedValue, actualTypeName, actualValue, message);

        // Flattens each ArrayValue's per-dimension (Lo, Hi) bounds into the
        // primitive size/lower-bound lists AssertArrayEquals expects, because
        // the Runner project cannot reference ArrayValue. Elements is already
        // flat and is passed through uncopied - AssertArrayEquals only reads
        // it. delta is null for every non-float type.
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

        // The two halves of "charge this fault to the open test instead of
        // killing the suite". Engine, not this type, decides when a fault is
        // one test's problem rather than the whole suite's.
        public bool HasOpenTestCase => HasOpenTest;

        public void AbortCurrentTestCase(AssertionFailure failure) => AbortCurrentTest(failure);

        // Announces which native call is about to run and where it is written,
        // so a failure recorded during it can name both. Called for every
        // TcUnit native call rather than only the asserts: TEST()/
        // TEST_FINISHED() simply never reach Fail(), and filtering on "is this
        // an assert name" here would duplicate NativeMethodBridge's dispatch
        // table.
        public void EnterNativeCall(string methodName, AssertSite site)
        {
            CurrentAssert = methodName;
            CurrentSite = site;
        }
    }
}
