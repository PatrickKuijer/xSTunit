using System.Linq;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // End-to-end guard for the miniload conveyor slice: the real
    // .TcPOU/.TcDUT fixture files parsed, registered and interpreted, with the
    // suite's own ST deciding pass/fail. If FB_Conveyor stops loading - an
    // unsupported construct, an unresolved enum member, a dependency that no
    // longer resolves through its interface - this is what goes red, not a C#
    // stand-in for it.
    public class ConveyorSuiteTests
    {
        private static readonly string FixtureDir = TestFixtures.MiniloadConveyorFixtureDir();
        private static readonly string SensorFixtureDir = TestFixtures.MiniloadSensorFixtureDir();

        [Fact]
        public void RunSuite_FbConveyorTests_EveryCasePasses()
        {
            var engine = MiniloadConveyorFixtureEngine.Create(FixtureDir, SensorFixtureDir);

            var results = engine.RunSuite("FB_ConveyorTests");

            Assert.All(results, r => Assert.True(r.Passed, r.ToString()));
            Assert.Equal(
                new[]
                {
                    "ConveyorPowersUpStoppedWithNothingCounted",
                    "StartRampsTheConveyorUpToWaitingForAContainer",
                    "ANonZeroAccelTimeHoldsTheConveyorInStarting",
                    "DetectionHoldsTheContainerUntilDownstreamIsReady",
                    "DownstreamReadyReleasesTheContainerIntoTheHandover",
                    "AWithdrawnDownstreamPutsTheContainerBackOnHold",
                    "AContainerLiftedOffTheEyeReleasesTheHold",
                    "AClearedEyeCompletesTheHandoverAndCountsTheContainer",
                    "AContainerStillOnTheEyeCannotCompleteTheHandover",
                    "ANonZeroHandoverDelayHoldsTheHandoverOpen",
                    "StopEndsTheRunFromWhereverItIs",
                    "ANonZeroDecelTimeHoldsTheConveyorInStopping",
                    "ADriveTripStopsTheBeltAtOnceAndLatchesTheFault",
                    "AFaultedConveyorCannotBeStarted",
                    "ResetReleasesTheLatchAndMakesTheRunStartableAgain",
                    "AStandingDriveFaultCannotBeAcknowledgedAway",
                    "SpeedPercentageScalesTheCommandedVelocity",
                    "MotorRunOnKeepsTheBeltTurningAfterTheDemandDrops",
                    "ContainersPassedIsALifetimeTotal",
                    "SensorSnapshotPublishesTheDetectionPoint",
                    "TheInfeedInstanceRunsTheSharedMachineOnItsOwnRecipe",
                },
                results.Select(r => r.Name));
        }
    }
}
