using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // TcXunit-k28.7: TEST_ORDERED/TEST_FINISHED_NAMED/IS_TEST_FINISHED had no
    // NativeMethodBridge case or SuiteHost wrapper - wires them up and
    // proves they're reachable through the interpreter, including as boolean
    // expressions inside IF (TEST_ORDERED/IS_TEST_FINISHED return BOOL).
    public class NativeMethodBridgeOrderedTests
    {
        private static Engine NewSuiteEngine(string implementationText)
        {
            var suite = new PouAst("FB_MySuite", "TcUnit.FB_TestSuite", "", implementationText, new List<MethodAst>());
            return new Engine(new TypeRegistry(new[] { suite }));
        }

        [Fact]
        public void RunSuite_TestOrdered_GatesOnItsTurnAndReportsBothTests()
        {
            var engine = NewSuiteEngine(
                "IF TEST_ORDERED('Test_1') THEN\n" +
                "  AssertEquals_INT(Expected := 1, Actual := 1, Message := 'ok');\n" +
                "  TEST_FINISHED();\n" +
                "END_IF\n" +
                "IF TEST_ORDERED('Test_2') THEN\n" +
                "  AssertEquals_INT(Expected := 2, Actual := 2, Message := 'ok');\n" +
                "  TEST_FINISHED();\n" +
                "END_IF");

            var results = engine.RunSuite("FB_MySuite");

            Assert.Equal(2, results.Count);
            Assert.True(results[0].Passed, results[0].ToString());
            Assert.True(results[1].Passed, results[1].ToString());
        }

        [Fact]
        public void RunSuite_TestFinishedNamed_ClosesNamedTest()
        {
            var engine = NewSuiteEngine(
                "TEST('A');\n" +
                "TEST_FINISHED_NAMED('A');");

            var result = Assert.Single(engine.RunSuite("FB_MySuite"));

            Assert.True(result.Passed, result.ToString());
        }

        [Fact]
        public void RunSuite_IsTestFinished_ReflectsFinishTransition()
        {
            var engine = NewSuiteEngine(
                "TEST('A');\n" +
                "AssertFalse(Condition := IS_TEST_FINISHED('A'), Message := 'not finished yet');\n" +
                "TEST_FINISHED();\n" +
                "AssertTrue(Condition := IS_TEST_FINISHED('A'), Message := 'finished now');");

            // The trailing AssertTrue runs outside any TEST()/TEST_FINISHED()
            // bracket, so it only proves IS_TEST_FINISHED's return value via
            // the suite completing without an assertion-bracket error.
            var result = Assert.Single(engine.RunSuite("FB_MySuite"));
            Assert.True(result.Passed, result.ToString());
        }
    }
}
