namespace xStunit.Interpreter.Tests
{
    // A conveyor wired to the drive and the detection point it is commanded
    // through, plus the one scan order that keeps the three consistent.
    internal sealed class ConveyorRig
    {
        // The ordinals in E_ConveyorState, so a timing test can name a state
        // instead of asserting against a bare integer.
        public const int Init = 0;
        public const int Starting = 1;
        public const int WaitContainer = 2;
        public const int WaitDownstreamReady = 3;
        public const int ProcesContainer = 4;
        public const int Stopping = 5;

        private readonly Engine _engine;
        private readonly FbInstance _conveyor;
        private readonly FbInstance _sensor;
        private readonly FbInstance _axis;

        public ConveyorRig(string conveyorFixtureDir, string sensorFixtureDir)
        {
            _engine = MiniloadConveyorFixtureEngine.Create(conveyorFixtureDir, sensorFixtureDir);
            _conveyor = _engine.NewInstance("FB_Conveyor");
            _sensor = _engine.NewInstance("FB_DigitalInput");
            _axis = _engine.NewInstance("FB_AxisBase");

            // The detection point runs unfiltered and in simulation: this rig
            // exists to move the CONVEYOR's clock, and a debounce window of its
            // own on the sensor would put a second timer between the level a
            // test sets and the transition it is measuring.
            _sensor.Fields["simulate"].Value = true;
            _sensor.Fields["debounce"].Value = 0u;

            _conveyor.Fields["caseDetectionSensor"].Value = _sensor;
            _conveyor.Fields["conveyorAxis"].Value = _axis;
        }

        public int State => (int)_conveyor.Fields["CurrentState"].Value;

        public bool MotorRunning => (bool)_conveyor.Fields["MotorRunning"].Value;

        // Converted rather than cast: a CTU's CV arrives boxed as the counter
        // host's own integer type, not as the UINT the output is declared as.
        public int ContainersPassed => System.Convert.ToInt32(_conveyor.Fields["ContainersPassed"].Value);

        // One of the recipe's four TIME fields, in milliseconds.
        public void SetRecipe(string field, uint milliseconds) =>
            ((StructInstance)_conveyor.Fields["Control"].Value).Fields[field].Value = milliseconds;

        public void Command(string input, bool level) => _conveyor.Fields[input].Value = level;

        // TRUE means a container is breaking the eye.
        public void ContainerOnEye(bool present) => _sensor.Fields["simulatedLevel"].Value = present;

        // One scan of the whole run, in the order a real task would: the world
        // the module reads is updated before the module reads it, so a level
        // set between two scans is seen on the next one rather than the one
        // after.
        public void Scan()
        {
            MiniloadConveyorFixtureEngine.Step(_engine, _axis);
            MiniloadConveyorFixtureEngine.Step(_engine, _sensor);
            MiniloadConveyorFixtureEngine.Step(_engine, _conveyor);
        }

        public void AdvanceAndScan(uint milliseconds)
        {
            _engine.Clock.AdvanceMs(milliseconds);
            Scan();
        }

        // Walks the run from INIT to WAIT_CONTAINER with the ramp switched off,
        // for the tests that are measuring something further along. A test that
        // is measuring the ramp itself sets AccelTime and drives Start by hand.
        public void StartWithoutARamp()
        {
            SetRecipe("AccelTime", 0);
            Command("Start", true);
            Scan();
            Scan();
        }
    }
}
