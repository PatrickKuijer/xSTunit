using System;
using xStunit.Runner.Tests.Fakes;
using Xunit;

namespace xStunit.Runner.Tests
{
    public class OrderedAndNamedFinishTests
    {
        [Fact]
        public void TestOrdered_RunsBothTestsInDeclaredOrder()
        {
            var results = SuiteRunner.RunAll(new OrderedTestSuite());

            Assert.Equal(2, results.Count);
            Assert.Equal("Test_1", results[0].Name);
            Assert.True(results[0].Passed, results[0].ToString());
            Assert.Equal("Test_2", results[1].Name);
            Assert.True(results[1].Passed, results[1].ToString());
        }

        [Fact]
        public void TestOrdered_NotYetItsTurn_SkipsAssertsAndNeverFinishes()
        {
            var results = SuiteRunner.RunAll(new OutOfTurnOrderedTestSuite());

            var result = Assert.Single(results);
            Assert.Equal("Test_1", result.Name);
            Assert.True(result.Passed, result.ToString());
        }

        [Fact]
        public void TestFinishedNamed_ClosesNamedTestEvenWhenCurrent()
        {
            var results = SuiteRunner.RunAll(new NamedFinishTestSuite());

            var result = Assert.Single(results);
            Assert.Equal("A", result.Name);
            Assert.True(result.Passed, result.ToString());
        }

        [Fact]
        public void TestFinishedNamed_UnknownName_ThrowsImmediately()
        {
            Assert.Throws<InvalidOperationException>(
                () => SuiteRunner.RunAll(new UnknownNamedFinishTestSuite()));
        }

        [Fact]
        public void IsTestFinished_UnknownName_ThrowsImmediately()
        {
            Assert.Throws<InvalidOperationException>(
                () => SuiteRunner.RunAll(new UnknownIsFinishedTestSuite()));
        }
    }
}
