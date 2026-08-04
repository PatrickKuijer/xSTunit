using System.Linq;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // The half of FB_ContainerMover's contract that needs simulated time to
    // move mid-case: the move allowance, the only fault the shuttle raises
    // because nothing happened rather than because a guard said no. Its suite
    // lives in a POU of its own because it calls AdvanceClock, which has no
    // TwinCAT counterpart - keeping those cases out of FB_ContainerMoverTests
    // is what lets that file stay compilable on the target. If this goes red
    // the timing contract has moved, not the split.
    public class ContainerMoverTimingSuiteTests
    {
        private static readonly string FixtureDir = TestFixtures.MiniloadMoverFixtureDir();
        private static readonly string StorageFixtureDir = TestFixtures.MiniloadStorageFixtureDir();

        [Fact]
        public void RunSuite_FbContainerMoverTimingTests_EveryCasePasses()
        {
            var engine = MiniloadFixtureEngine.Create(FixtureDir, StorageFixtureDir);

            var results = engine.RunSuite("FB_ContainerMoverTimingTests");

            Assert.All(results, r => Assert.True(r.Passed, r.ToString()));
            Assert.Equal(
                new[]
                {
                    "AShuttleThatNeverArrivesFaultsExactlyAtTheMoveAllowance",
                    "AMoveThatFinishesWithAMillisecondToSpareNeverTripsTheLatch",
                    "TheAllowanceCoversTheHookSequenceAndNotOnlyTheTravel",
                    "EachOrderGetsAWholeAllowanceRatherThanWhatTheLastOneLeft",
                },
                results.Select(r => r.Name));
        }
    }
}
