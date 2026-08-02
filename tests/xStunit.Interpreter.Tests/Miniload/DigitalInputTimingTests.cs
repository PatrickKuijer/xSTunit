using Xunit;

namespace xStunit.Interpreter.Tests
{
    // The half of FB_DigitalInput's contract a TcUnit suite cannot reach: what
    // the debounce filter and the two elapsed meters do as simulated time
    // passes. xStunit's Clock is driven from C# and has no ST-visible advance,
    // so the suite in FB_DigitalInputTests.TcPOU can only pin the two ends of
    // the filter (T#0ms and "no window can close"). Everything between them is
    // pinned here, against the same fixture files.
    //
    // Arrangement writes the backing VARs rather than the properties because
    // there is no public entry point for a property Set; the property surface
    // itself is what the ST suite exercises.
    public class DigitalInputTimingTests
    {
        private static readonly string FixtureDir = TestFixtures.MiniloadSensorFixtureDir();

        private static (Engine Engine, FbInstance Sensor) NewSensor(uint debounceMs)
        {
            var engine = MiniloadSensorFixtureEngine.Create(FixtureDir);
            var sensor = engine.NewInstance("FB_DigitalInput");
            sensor.Fields["debounce"].Value = debounceMs;
            return (engine, sensor);
        }

        [Fact]
        public void RisingEdge_ReachesActive_OnlyOnceTheLevelHasHeldForDebounceTime()
        {
            var (engine, sensor) = NewSensor(20);
            sensor.Fields["HardwareInput"].Value = true;

            // Baseline call: a timer counts from its own last-observed clock
            // total, so nothing has elapsed for it until the call after an
            // advance.
            MiniloadSensorFixtureEngine.Step(engine, sensor);

            engine.Clock.AdvanceMs(19);
            MiniloadSensorFixtureEngine.Step(engine, sensor);
            Assert.False(MiniloadSensorFixtureEngine.Active(sensor));

            engine.Clock.AdvanceMs(1);
            MiniloadSensorFixtureEngine.Step(engine, sensor);
            Assert.True(MiniloadSensorFixtureEngine.Active(sensor));
        }

        [Fact]
        public void Glitch_ShorterThanDebounceTime_NeverReachesActive_AndRestartsTheWindow()
        {
            var (engine, sensor) = NewSensor(20);
            sensor.Fields["HardwareInput"].Value = true;
            MiniloadSensorFixtureEngine.Step(engine, sensor);

            engine.Clock.AdvanceMs(10);
            MiniloadSensorFixtureEngine.Step(engine, sensor);
            Assert.False(MiniloadSensorFixtureEngine.Active(sensor));

            sensor.Fields["HardwareInput"].Value = false;
            engine.Clock.AdvanceMs(5);
            MiniloadSensorFixtureEngine.Step(engine, sensor);

            // The window restarts from zero rather than resuming: 10 ms banked
            // before the dropout plus 15 ms after it must NOT add up to a
            // settled transition, or a fast enough chatter would eventually
            // fake one.
            sensor.Fields["HardwareInput"].Value = true;
            engine.Clock.AdvanceMs(15);
            MiniloadSensorFixtureEngine.Step(engine, sensor);
            Assert.False(MiniloadSensorFixtureEngine.Active(sensor));

            engine.Clock.AdvanceMs(5);
            MiniloadSensorFixtureEngine.Step(engine, sensor);
            Assert.True(MiniloadSensorFixtureEngine.Active(sensor));
        }

        [Fact]
        public void FallingEdge_ReleasesActive_OnlyOnceTheLevelHasHeldLowForDebounceTime()
        {
            var (engine, sensor) = NewSensor(20);
            sensor.Fields["HardwareInput"].Value = true;
            MiniloadSensorFixtureEngine.Step(engine, sensor);
            engine.Clock.AdvanceMs(20);
            MiniloadSensorFixtureEngine.Step(engine, sensor);
            Assert.True(MiniloadSensorFixtureEngine.Active(sensor));

            // Symmetric with the rising side, down to the same boundary: the
            // release is delayed by the same DebounceTime that delayed the
            // engage.
            sensor.Fields["HardwareInput"].Value = false;
            engine.Clock.AdvanceMs(19);
            MiniloadSensorFixtureEngine.Step(engine, sensor);
            Assert.True(MiniloadSensorFixtureEngine.Active(sensor));

            engine.Clock.AdvanceMs(1);
            MiniloadSensorFixtureEngine.Step(engine, sensor);
            Assert.False(MiniloadSensorFixtureEngine.Active(sensor));
        }

        [Fact]
        public void TimeActive_MetersTheCurrentStretch_AndBothMetersSwapOnATransition()
        {
            var (engine, sensor) = NewSensor(0);
            sensor.Fields["HardwareInput"].Value = true;
            MiniloadSensorFixtureEngine.Step(engine, sensor);

            engine.Clock.AdvanceMs(100);
            MiniloadSensorFixtureEngine.Step(engine, sensor);
            Assert.Equal(100u, MiniloadSensorFixtureEngine.TimeActiveMs(sensor));
            Assert.Equal(0u, MiniloadSensorFixtureEngine.TimeInactiveMs(sensor));

            engine.Clock.AdvanceMs(250);
            MiniloadSensorFixtureEngine.Step(engine, sensor);
            Assert.Equal(350u, MiniloadSensorFixtureEngine.TimeActiveMs(sensor));

            // The stretch is the CURRENT one, not a running total: releasing
            // must zero TimeActive outright, so "blocked for 350 ms" cannot be
            // read off a sensor that is no longer blocked.
            sensor.Fields["HardwareInput"].Value = false;
            MiniloadSensorFixtureEngine.Step(engine, sensor);
            engine.Clock.AdvanceMs(40);
            MiniloadSensorFixtureEngine.Step(engine, sensor);
            Assert.Equal(0u, MiniloadSensorFixtureEngine.TimeActiveMs(sensor));
            Assert.Equal(40u, MiniloadSensorFixtureEngine.TimeInactiveMs(sensor));
        }

        [Fact]
        public void ElapsedMeters_FollowTheSettledState_NotTheUnfilteredLevel()
        {
            var (engine, sensor) = NewSensor(20);
            MiniloadSensorFixtureEngine.Step(engine, sensor);

            engine.Clock.AdvanceMs(100);
            MiniloadSensorFixtureEngine.Step(engine, sensor);
            Assert.Equal(100u, MiniloadSensorFixtureEngine.TimeInactiveMs(sensor));

            // A bounce that the filter swallowed must not restart the meter
            // either, or "the lane has been clear for N ms" would be reset by
            // noise that never counted as a container.
            sensor.Fields["HardwareInput"].Value = true;
            engine.Clock.AdvanceMs(5);
            MiniloadSensorFixtureEngine.Step(engine, sensor);
            sensor.Fields["HardwareInput"].Value = false;
            engine.Clock.AdvanceMs(5);
            MiniloadSensorFixtureEngine.Step(engine, sensor);

            Assert.False(MiniloadSensorFixtureEngine.Active(sensor));
            Assert.Equal(110u, MiniloadSensorFixtureEngine.TimeInactiveMs(sensor));
        }
    }
}
