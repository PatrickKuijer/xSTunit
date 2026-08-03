using Xunit;

namespace xStunit.Interpreter.Tests
{
    // The half of PML_StateMachine's contract a TcUnit suite cannot reach: what
    // StateElapsedTime and TotalRunTime do as simulated time passes. xStunit's
    // Clock is driven from C# and has no ST-visible advance, so the suite in
    // PML_StateMachineTests.TcPOU sees both meters frozen at zero. The state
    // model itself is entirely reachable from ST - a PackML state is left on a
    // command or on a completion flag, never on a timeout - so only the two
    // meters are pinned here, against the same fixture files.
    public class PackMLStateMachineTimingTests
    {
        private const int Stopped = 2;
        private const int Execute = 6;
        private const int Holding = 10;
        private const int Resetting = 15;

        private static readonly string FixtureDir = TestFixtures.MiniloadPackMLFixtureDir();

        private static (Engine Engine, FbInstance Unit) NewUnit()
        {
            var engine = MiniloadPackMLFixtureEngine.Create(FixtureDir);
            return (engine, engine.NewInstance("PML_StateMachine"));
        }

        private static void DriveToExecute(Engine engine, FbInstance unit)
        {
            MiniloadPackMLFixtureEngine.Pulse(engine, unit, "Reset");
            MiniloadPackMLFixtureEngine.PulseComplete(engine, unit, "Resetting");
            MiniloadPackMLFixtureEngine.Pulse(engine, unit, "Start");
            MiniloadPackMLFixtureEngine.PulseComplete(engine, unit, "Starting");
        }

        [Fact]
        public void StateElapsedTime_MetersTheCurrentState_AndRestartsOnEveryTransition()
        {
            var (engine, unit) = NewUnit();

            // Baseline scan: a timer counts from its own last-observed clock
            // total, so nothing has elapsed for it until the call after an
            // advance.
            MiniloadPackMLFixtureEngine.Step(engine, unit);
            engine.Clock.AdvanceMs(100);
            MiniloadPackMLFixtureEngine.Step(engine, unit);
            Assert.Equal(Stopped, MiniloadPackMLFixtureEngine.State(unit));
            Assert.Equal(100u, MiniloadPackMLFixtureEngine.StateElapsedMs(unit));

            // A transition has to zero the meter on the scan it lands, not on
            // the scan after: "how long have we been resetting" read one scan
            // into Resetting must not answer with the time spent Stopped.
            engine.Clock.AdvanceMs(30);
            MiniloadPackMLFixtureEngine.Pulse(engine, unit, "Reset");
            Assert.Equal(Resetting, MiniloadPackMLFixtureEngine.State(unit));
            Assert.Equal(0u, MiniloadPackMLFixtureEngine.StateElapsedMs(unit));

            engine.Clock.AdvanceMs(50);
            MiniloadPackMLFixtureEngine.Step(engine, unit);
            Assert.Equal(50u, MiniloadPackMLFixtureEngine.StateElapsedMs(unit));
        }

        [Fact]
        public void TotalRunTime_AccumulatesEveryExecuteStretch_AndStandsStillOutsideThem()
        {
            var (engine, unit) = NewUnit();
            DriveToExecute(engine, unit);
            Assert.Equal(Execute, MiniloadPackMLFixtureEngine.State(unit));
            Assert.Equal(0u, MiniloadPackMLFixtureEngine.TotalRunMs(unit));

            engine.Clock.AdvanceMs(200);
            MiniloadPackMLFixtureEngine.Step(engine, unit);
            Assert.Equal(200u, MiniloadPackMLFixtureEngine.TotalRunMs(unit));

            // Leaving Execute must bank the stretch rather than discard it. The
            // meter behind it is a TON, which zeroes its elapsed time on the
            // same call that drops IN, so an unbanked stretch is not merely
            // paused - it is gone, and a shift's run time with it.
            MiniloadPackMLFixtureEngine.Pulse(engine, unit, "Hold");
            Assert.Equal(Holding, MiniloadPackMLFixtureEngine.State(unit));
            Assert.Equal(200u, MiniloadPackMLFixtureEngine.TotalRunMs(unit));

            engine.Clock.AdvanceMs(500);
            MiniloadPackMLFixtureEngine.Step(engine, unit);
            Assert.Equal(200u, MiniloadPackMLFixtureEngine.TotalRunMs(unit));

            MiniloadPackMLFixtureEngine.PulseComplete(engine, unit, "Holding");
            MiniloadPackMLFixtureEngine.Pulse(engine, unit, "Unhold");
            MiniloadPackMLFixtureEngine.PulseComplete(engine, unit, "Unholding");
            Assert.Equal(Execute, MiniloadPackMLFixtureEngine.State(unit));

            engine.Clock.AdvanceMs(300);
            MiniloadPackMLFixtureEngine.Step(engine, unit);
            Assert.Equal(500u, MiniloadPackMLFixtureEngine.TotalRunMs(unit));
        }
    }
}
