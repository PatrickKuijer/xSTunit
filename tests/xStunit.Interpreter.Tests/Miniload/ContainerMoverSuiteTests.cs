using System.Linq;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // End-to-end guard for the miniload container-mover slice: the real
    // .TcPOU/.TcDUT/.TcIO fixture files parsed, registered and interpreted,
    // with the suite's own ST deciding pass/fail. It is loaded together with
    // the storage fixture because the mover genuinely holds an
    // FB_StorageLocationMap - the two compose into one run, which is what makes
    // this an integration fixture rather than two isolated ones.
    //
    // It is also the slice's only real LREAL arithmetic: surveyed position
    // tables, an in-position window and a coordinated move's remaining travel,
    // so a regression in floating-point handling surfaces here.
    public class ContainerMoverSuiteTests
    {
        private static readonly string FixtureDir = TestFixtures.MiniloadMoverFixtureDir();
        private static readonly string StorageFixtureDir = TestFixtures.MiniloadStorageFixtureDir();

        [Fact]
        public void RunSuite_FbContainerMoverTests_EveryCasePasses()
        {
            var engine = MiniloadFixtureEngine.Create(FixtureDir, StorageFixtureDir);

            var results = engine.RunSuite("FB_ContainerMoverTests");

            Assert.All(results, r => Assert.True(r.Passed, r.ToString()));
            Assert.Equal(
                new[]
                {
                    "AFreshMoverIsIdleAndHoldsNoOrder",
                    "AnOrderHomesTheShuttleBeforeItTravels",
                    "BothAxesAreCommandedTogetherRatherThanOneAfterTheOther",
                    "TheAxisTargetsAreReadFromTheSurveyedPositionTable",
                    "TheHookReachTakesItsSignFromTheRackFace",
                    "AStoreOrderRunsTheHookSequenceAndTellsTheMapOnlyOnceItIsDone",
                    "ARetrieveOrderReturnsTheLocationToTheFreePool",
                    "TheHooksMayNotReachIntoTheRackUntilTheyAreHome",
                    "TheHooksMayNotReachIntoTheRackUntilTheCylinderIsSet",
                    "TheOrderIsNotCompleteUntilTheHooksAreBackFromTheRack",
                    "AnOrderForALocationTheRackingDoesNotHaveIsFaulted",
                    "AnOrderForAnotherAislesRackIsFaulted",
                    "StoringIntoALocationNobodyReservedIsFaulted",
                    "RetrievingFromAnEmptyLocationIsFaulted",
                    "ATargetOutsideTheAxisEnvelopeIsFaulted",
                    "AnOrderArrivingWhileTheShuttleIsBusyIsRefusedWithoutFaulting",
                    "ADriveFaultLatchesUntilItIsAcknowledged",
                    "TheCoordinatedMoveReportsTheLongerOfItsTwoLegsAsRemaining",
                },
                results.Select(r => r.Name));
        }
    }
}
