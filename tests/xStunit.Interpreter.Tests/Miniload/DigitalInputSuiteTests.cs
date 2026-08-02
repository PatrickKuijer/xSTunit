using System.Linq;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // End-to-end guard for the miniload sensor slice: the real .TcPOU/.TcDUT
    // fixture files parsed, registered and interpreted, with the suite's own ST
    // deciding pass/fail. If FB_DigitalInput stops loading - an unsupported
    // construct, an unresolved struct, a renamed property - this is what goes
    // red, not a C# stand-in for it.
    public class DigitalInputSuiteTests
    {
        private static readonly string FixtureDir = TestFixtures.MiniloadSensorFixtureDir();

        [Fact]
        public void RunSuite_FbDigitalInputTests_EveryCasePasses()
        {
            var engine = MiniloadSensorFixtureEngine.Create(FixtureDir);

            var results = engine.RunSuite("FB_DigitalInputTests");

            Assert.All(results, r => Assert.True(r.Passed, r.ToString()));
            Assert.Equal(
                new[]
                {
                    "SensorStartsInactiveWithBothMetersAtZero",
                    "UnfilteredSensorFollowsTheHardwareLevel",
                    "InvertedSensorReadsTheHardwareLevelBackwards",
                    "SimulationReplacesTheHardwareLevel",
                    "ClearingSimulationReturnsToTheHardwareLevel",
                    "SimulationIsFilteredAndInvertedLikeTheFieldBit",
                    "DebounceHoldsARisingEdgeUntilItsWindowCloses",
                    "DebounceHoldsAFallingEdgeUntilItsWindowCloses",
                    "SnapshotMirrorsThePublishedState",
                },
                results.Select(r => r.Name));
        }
    }
}
