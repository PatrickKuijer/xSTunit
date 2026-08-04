using System.Linq;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // End-to-end guard for the miniload PackML slice: the real .TcPOU/.TcDUT
    // fixture files parsed, registered and interpreted, with the suite's own ST
    // deciding pass/fail. If PML_StateMachine stops loading - an unsupported
    // construct, an unresolved enum member, a renamed command field - this is
    // what goes red, not a C# stand-in for it.
    public class PackMLStateMachineSuiteTests
    {
        private static readonly string FixtureDir = TestFixtures.MiniloadPackMLFixtureDir();

        [Fact]
        public void RunSuite_PmlStateMachineTests_EveryCasePasses()
        {
            var engine = MiniloadFixtureEngine.Create(FixtureDir);

            var results = engine.RunSuite("PML_StateMachineTests");

            Assert.All(results, r => Assert.True(r.Passed, r.ToString()));
            Assert.Equal(
                new[]
                {
                    "UnitPowersUpStoppedAndUnfaulted",
                    "EveryPackMLStateHasItsPublishedOrdinal",
                    "ResetAndStartWalkTheProductionPath",
                    "HoldAndUnholdReturnTheUnitToExecute",
                    "SuspendAndUnsuspendReturnTheUnitToExecute",
                    "StopFromExecuteEndsAtStopped",
                    "AbortLatchesTheFaultBeforeTheUnitReachesAborted",
                    "ClearFromAbortedReleasesTheLatchAndReturnsToStopped",
                    "ClearIsIgnoredUnlessTheUnitIsAborted",
                    "AbortOutranksEveryOtherCommand",
                    "StopOutranksThePerStateCommands",
                    "AStaleAbortLevelCannotHoldTheUnitFaulted",
                    "ACompletionFlagOnlyCompletesItsOwnState",
                    "CountsAreLifetimeTotals",
                },
                results.Select(r => r.Name));
        }
    }
}
