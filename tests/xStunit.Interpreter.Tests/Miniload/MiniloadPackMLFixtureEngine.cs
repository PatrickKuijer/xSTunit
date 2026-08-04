namespace xStunit.Interpreter.Tests
{
    // The PackML vocabulary the C# timing tests drive PML_StateMachine in:
    // commands and completion flags are levels on published structs, and a
    // state is left on a one-scan pulse of one of them. Engine construction is
    // MiniloadFixtureEngine's job; what is here is only what "pulse Reset" and
    // "read the current state" mean for this slice.
    internal static class MiniloadPackMLFixtureEngine
    {
        public static void Command(FbInstance unit, string command, bool level) =>
            MiniloadFixtureEngine.Member(unit, "Commands", command).Value = level;

        public static void ReportComplete(FbInstance unit, string actingState, bool level) =>
            MiniloadFixtureEngine.Member(unit, "StateComplete", actingState).Value = level;

        // Raise a level, run the scan it is meant to be seen on, drop it again -
        // the same one-scan pulse the ST suite spells out inline.
        public static void Pulse(Engine engine, FbInstance unit, string command)
        {
            Command(unit, command, true);
            MiniloadFixtureEngine.Step(engine, unit);
            Command(unit, command, false);
        }

        public static void PulseComplete(Engine engine, FbInstance unit, string actingState)
        {
            ReportComplete(unit, actingState, true);
            MiniloadFixtureEngine.Step(engine, unit);
            ReportComplete(unit, actingState, false);
        }

        public static int State(FbInstance unit) => (int)unit.Fields["CurrentState"].Value;

        public static uint StateElapsedMs(FbInstance unit) => DurationMs(unit, "StateElapsedTime");

        public static uint TotalRunMs(FbInstance unit) => DurationMs(unit, "TotalRunTime");

        private static uint DurationMs(FbInstance unit, string field) => (uint)unit.Fields[field].Value;
    }
}
