using Xunit;

namespace xStunit.Interpreter.Tests
{
    // The half of FB_Conveyor's contract a TcUnit suite cannot reach: the four
    // arcs gated on a timer, on the scan their window actually closes.
    //
    // xStunit's Clock is driven from C# and has no ST-visible advance, so the
    // suite in FB_ConveyorTests.TcPOU pins both ENDS of each gate exactly - at
    // zero the arc is taken at once, at any non-zero setting it can never be
    // taken with the clock stopped - and everything strictly between them is
    // pinned here, against the same fixture files.
    //
    // Arrangement writes the backing VARs rather than the ST property surface
    // because there is no public entry point for a property Set; the property
    // surface itself is what the ST suite exercises.
    public class ConveyorTimingTests
    {
        private static readonly string FixtureDir = TestFixtures.MiniloadConveyorFixtureDir();
        private static readonly string SensorFixtureDir = TestFixtures.MiniloadSensorFixtureDir();

        private static ConveyorRig NewConveyor() => new ConveyorRig(FixtureDir, SensorFixtureDir);

        [Fact]
        public void Starting_ReachesWaitContainer_OnlyOnceAccelTimeHasElapsed()
        {
            var rig = NewConveyor();
            rig.SetRecipe("AccelTime", 500);

            rig.Command("Start", true);
            rig.Scan();
            Assert.Equal(ConveyorRig.Starting, rig.State);

            rig.AdvanceAndScan(499);
            Assert.Equal(ConveyorRig.Starting, rig.State);

            rig.AdvanceAndScan(1);
            Assert.Equal(ConveyorRig.WaitContainer, rig.State);
        }

        [Fact]
        public void Stopping_ReachesInit_OnlyOnceDecelTimeHasElapsed()
        {
            var rig = NewConveyor();
            rig.SetRecipe("DecelTime", 800);
            rig.StartWithoutARamp();
            Assert.Equal(ConveyorRig.WaitContainer, rig.State);

            rig.Command("Start", false);
            rig.Command("Stop", true);
            rig.Scan();
            Assert.Equal(ConveyorRig.Stopping, rig.State);

            // The run is not finished until the belt has actually come to rest.
            // Reaching INIT early would let the next Start command a drive that
            // was still coasting.
            rig.AdvanceAndScan(799);
            Assert.Equal(ConveyorRig.Stopping, rig.State);

            rig.AdvanceAndScan(1);
            Assert.Equal(ConveyorRig.Init, rig.State);
        }

        [Fact]
        public void Handover_Completes_OnlyOnceTheEyeHasBeenClearForTheWholeDelay()
        {
            var rig = NewConveyor();
            rig.SetRecipe("HandoverDelay", 200);
            rig.StartWithoutARamp();

            rig.ContainerOnEye(true);
            rig.Scan();
            rig.Command("DownstreamReady", true);
            rig.Scan();
            Assert.Equal(ConveyorRig.ProcesContainer, rig.State);

            rig.ContainerOnEye(false);
            rig.AdvanceAndScan(199);
            Assert.Equal(ConveyorRig.ProcesContainer, rig.State);
            Assert.Equal(0, rig.ContainersPassed);

            rig.AdvanceAndScan(1);
            Assert.Equal(ConveyorRig.WaitContainer, rig.State);
            Assert.Equal(1, rig.ContainersPassed);
        }

        [Fact]
        public void AContainerBackOnTheEye_RestartsTheHandoverDelay_RatherThanResumingIt()
        {
            var rig = NewConveyor();
            rig.SetRecipe("HandoverDelay", 200);
            rig.StartWithoutARamp();

            rig.ContainerOnEye(true);
            rig.Scan();
            rig.Command("DownstreamReady", true);
            rig.Scan();

            rig.ContainerOnEye(false);
            rig.AdvanceAndScan(150);
            Assert.Equal(ConveyorRig.ProcesContainer, rig.State);

            // A second container arriving 150 ms into the window must not be
            // able to finish the first one's hand-over 50 ms later: banked time
            // plus fresh time adding up would count two closely spaced
            // containers as one that had cleared.
            rig.ContainerOnEye(true);
            rig.AdvanceAndScan(10);
            rig.ContainerOnEye(false);
            rig.AdvanceAndScan(60);
            Assert.Equal(ConveyorRig.ProcesContainer, rig.State);
            Assert.Equal(0, rig.ContainersPassed);

            rig.AdvanceAndScan(140);
            Assert.Equal(ConveyorRig.WaitContainer, rig.State);
            Assert.Equal(1, rig.ContainersPassed);
        }

        [Fact]
        public void MotorRunOn_ReleasesTheBelt_OnlyOnceItsWindowHasElapsed()
        {
            var rig = NewConveyor();
            rig.SetRecipe("MotorRunOn", 300);
            rig.StartWithoutARamp();
            Assert.True(rig.MotorRunning);

            // Detection drops the demand - the container is held for the next
            // station - and the run-on is what carries it the rest of the way
            // onto the transfer instead of stopping it half on.
            rig.ContainerOnEye(true);
            rig.Scan();
            Assert.Equal(ConveyorRig.WaitDownstreamReady, rig.State);

            rig.AdvanceAndScan(299);
            Assert.True(rig.MotorRunning);

            rig.AdvanceAndScan(1);
            Assert.False(rig.MotorRunning);
        }
    }
}
