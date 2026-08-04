using System.Linq;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // The half of FB_DigitalInput's contract that needs simulated time to move
    // mid-case: the exact debounce boundaries, a glitch inside the window, and
    // the elapsed meters. Its suite lives in a POU of its own because it calls
    // AdvanceClock, which has no TwinCAT counterpart - keeping those cases out
    // of FB_DigitalInputTests is what lets that file stay compilable on the
    // target. If this goes red the timing contract has moved, not the split.
    public class DigitalInputTimingSuiteTests
    {
        private static readonly string FixtureDir = TestFixtures.MiniloadSensorFixtureDir();

        [Fact]
        public void RunSuite_FbDigitalInputTimingTests_EveryCasePasses()
        {
            var engine = MiniloadFixtureEngine.Create(FixtureDir);

            var results = engine.RunSuite("FB_DigitalInputTimingTests");

            Assert.All(results, r => Assert.True(r.Passed, r.ToString()));
            Assert.Equal(
                new[]
                {
                    "RisingEdgeSettlesExactlyAtDebounceTime",
                    "FallingEdgeReleasesExactlyAtDebounceTime",
                    "GlitchShorterThanDebounceTimeNeverSettlesAndRestartsTheWindow",
                    "TimeActiveMetersTheCurrentStretchAndBothMetersSwapOnATransition",
                    "ElapsedMetersFollowTheSettledStateNotTheUnfilteredLevel",
                },
                results.Select(r => r.Name));
        }
    }
}
