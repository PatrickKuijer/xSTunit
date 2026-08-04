using System.Linq;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // The half of FB_Conveyor's contract that needs simulated time to move
    // mid-case: the scan each of the four timed gates closes on, and the
    // hand-over window restarting rather than resuming. Its suite lives in a
    // POU of its own because it calls AdvanceClock, which has no TwinCAT
    // counterpart - keeping those cases out of FB_ConveyorTests is what lets
    // that file stay compilable on the target. If this goes red the timing
    // contract has moved, not the split.
    public class ConveyorTimingSuiteTests
    {
        private static readonly string FixtureDir = TestFixtures.MiniloadConveyorFixtureDir();
        private static readonly string SensorFixtureDir = TestFixtures.MiniloadSensorFixtureDir();

        [Fact]
        public void RunSuite_FbConveyorTimingTests_EveryCasePasses()
        {
            var engine = MiniloadConveyorFixtureEngine.Create(FixtureDir, SensorFixtureDir);

            var results = engine.RunSuite("FB_ConveyorTimingTests");

            Assert.All(results, r => Assert.True(r.Passed, r.ToString()));
            Assert.Equal(
                new[]
                {
                    "StartingReachesWaitContainerOnlyOnceAccelTimeHasElapsed",
                    "StoppingReachesInitOnlyOnceDecelTimeHasElapsed",
                    "HandoverCompletesOnlyOnceTheEyeHasBeenClearForTheWholeDelay",
                    "AContainerBackOnTheEyeRestartsTheHandoverDelayRatherThanResumingIt",
                    "MotorRunOnReleasesTheBeltOnlyOnceItsWindowHasElapsed",
                },
                results.Select(r => r.Name));
        }
    }
}
