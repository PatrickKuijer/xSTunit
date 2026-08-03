using System.Collections.Generic;
using System.IO;
using System.Linq;
using xStunit.Parser;

namespace xStunit.Interpreter.Tests
{
    // Builds the engine the miniload PackML tests run against, from the fixture
    // files themselves rather than a hand-built AST. PML_StateMachine needs all
    // three kinds of DUT in the directory: the command and completion structs,
    // and E_PackMLState - whose members have to reach the registry as enum
    // members AND whose name has to reach it as an alias for the underlying
    // integer type, exactly as the CLI's own loader merges them.
    internal static class MiniloadPackMLFixtureEngine
    {
        public static Engine Create(string fixtureDir)
        {
            var directories = new[] { fixtureDir };

            var pous = Directory
                .GetFiles(fixtureDir, "*.TcPOU")
                .OrderBy(f => f)
                .Select(f => TcPouParser.Parse(File.ReadAllText(f)))
                .ToList();

            var structs = DutStructLoader.Load(directories, out var structsSkipped);
            if (structsSkipped.Count > 0)
                throw new IOException($"unreadable .TcDUT in {fixtureDir}: {structsSkipped[0].Message}");

            var aliases = DutEnumLoader
                .Load(directories, out var enumsSkipped, out var enumMembers)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            if (enumsSkipped.Count > 0)
                throw new IOException($"unreadable .TcDUT in {fixtureDir}: {enumsSkipped[0].Message}");

            return new Engine(new TypeRegistry(pous, structs, null, aliases, enumMembers));
        }

        // One PLC scan of the state manager's own top-level body. Timing tests
        // call this between clock advances, because a timer's elapsed time is
        // only observed on the call that follows the advance.
        public static void Step(Engine engine, FbInstance unit) =>
            engine.CallMethod(unit, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new List<NamedArg>(), null, null);

        public static void Command(FbInstance unit, string command, bool level) =>
            ((StructInstance)unit.Fields["Commands"].Value).Fields[command].Value = level;

        public static void ReportComplete(FbInstance unit, string actingState, bool level) =>
            ((StructInstance)unit.Fields["StateComplete"].Value).Fields[actingState].Value = level;

        // Raise a level, run the scan it is meant to be seen on, drop it again -
        // the same one-scan pulse the ST suite spells out inline.
        public static void Pulse(Engine engine, FbInstance unit, string command)
        {
            Command(unit, command, true);
            Step(engine, unit);
            Command(unit, command, false);
        }

        public static void PulseComplete(Engine engine, FbInstance unit, string actingState)
        {
            ReportComplete(unit, actingState, true);
            Step(engine, unit);
            ReportComplete(unit, actingState, false);
        }

        public static int State(FbInstance unit) => (int)unit.Fields["CurrentState"].Value;

        public static uint StateElapsedMs(FbInstance unit) => DurationMs(unit, "StateElapsedTime");

        public static uint TotalRunMs(FbInstance unit) => DurationMs(unit, "TotalRunTime");

        private static uint DurationMs(FbInstance unit, string field) =>
            (uint)unit.Fields[field].Value;
    }
}
