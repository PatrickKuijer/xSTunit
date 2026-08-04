using System.Linq;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // End-to-end guard for the miniload storage slice: the real .TcPOU/.TcDUT
    // fixture files parsed, registered and interpreted, with the suite's own ST
    // deciding pass/fail. The map is the slice's only large array of structs -
    // 1800 four-dimensional elements walked by nested FOR loops and reached by
    // index arithmetic - so an interpreter regression in any of that surfaces
    // here rather than in a C# stand-in.
    public class StorageLocationMapSuiteTests
    {
        private static readonly string FixtureDir = TestFixtures.MiniloadStorageFixtureDir();

        [Fact]
        public void RunSuite_FbStorageLocationMapTests_EveryCasePasses()
        {
            var engine = MiniloadFixtureEngine.Create(FixtureDir);

            var results = engine.RunSuite("FB_StorageLocationMapTests");

            Assert.All(results, r => Assert.True(r.Passed, r.ToString()));
            Assert.Equal(
                new[]
                {
                    "AFreshMapIsEntirelyFree",
                    "EveryLocationCarriesTheAddressItsIndexResolvesTo",
                    "TheFlatIndexWalksOnePositionAtATime",
                    "AnAddressOutsideTheRackingHasNoIndex",
                    "MesAssignsTheLocationAndTheContainerIsStoredThere",
                    "ALocationThatIsNotFreeCannotBeReservedAgain",
                    "StoringNeedsAReservationFirst",
                    "ReleaseReturnsTheLocationAndTheStoredTotalSurvivesIt",
                    "ReleasingAnEmptyLocationIsRefused",
                    "TheAllocatorIsUnreachableWhileMesIsOnline",
                    "TheFallbackAllocatorHandsOutTheLowestFreeLocation",
                    "TheFallbackAllocatorSkipsEverythingThatIsNotFree",
                    "AFullWarehouseRefusesTheFallbackAllocation",
                },
                results.Select(r => r.Name));
        }
    }
}
