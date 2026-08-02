using System.Collections.Generic;
using System.IO;
using System.Linq;
using xStunit.Parser;

namespace xStunit.Interpreter.Tests
{
    // Builds the engine both miniload sensor test classes run against, from the
    // fixture files themselves rather than from a hand-built AST: FB_DigitalInput
    // publishes an ST_SensorData, so the .TcDUT has to be loaded alongside the
    // .TcPOU files or the struct field never resolves.
    internal static class MiniloadSensorFixtureEngine
    {
        public static Engine Create(string fixtureDir)
        {
            var pous = Directory
                .GetFiles(fixtureDir, "*.TcPOU")
                .OrderBy(f => f)
                .Select(f => TcPouParser.Parse(File.ReadAllText(f)))
                .ToList();

            var structs = DutStructLoader.Load(new[] { fixtureDir }, out var skipped);
            if (skipped.Count > 0)
                throw new IOException($"unreadable .TcDUT in {fixtureDir}: {skipped[0].Message}");

            return new Engine(new TypeRegistry(pous, structs));
        }

        // The sensor's own top-level body, run once - the equivalent of one PLC
        // scan. Timing tests call this between clock advances, because a timer's
        // elapsed time is only observed on the call that follows the advance.
        public static void Step(Engine engine, FbInstance sensor) =>
            engine.CallMethod(sensor, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new List<NamedArg>(), null, null);

        public static bool Active(FbInstance sensor) => (bool)Snapshot(sensor)["Active"].Value;

        public static uint TimeActiveMs(FbInstance sensor) => (uint)Snapshot(sensor)["TimeActive"].Value;

        public static uint TimeInactiveMs(FbInstance sensor) => (uint)Snapshot(sensor)["TimeInactive"].Value;

        private static Dictionary<string, Cell> Snapshot(FbInstance sensor) =>
            ((StructInstance)sensor.Fields["Data"].Value).Fields;
    }
}
