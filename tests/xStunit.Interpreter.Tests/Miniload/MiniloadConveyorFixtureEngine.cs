using System.IO;
using System.Linq;
using xStunit.Parser;

namespace xStunit.Interpreter.Tests
{
    // Builds the engine the miniload conveyor tests run against, from the
    // fixture files themselves rather than a hand-built AST.
    //
    // TWO directories, not one: FB_Conveyor drives its detection point through
    // I_DigitalInput and the implementation of that contract is FB_DigitalInput
    // in the sensor fixture, so the conveyor fixture is not loadable on its own.
    // That is the dependency the module really has - an equipment module that
    // only worked against a stand-in written for its own tests would be no
    // evidence about the machine - and the CLI is pointed at both directories
    // for the same reason.
    internal static class MiniloadConveyorFixtureEngine
    {
        public static Engine Create(string conveyorFixtureDir, string sensorFixtureDir)
        {
            var directories = new[] { conveyorFixtureDir, sensorFixtureDir };

            var pous = directories
                .SelectMany(d => Directory.GetFiles(d, "*.TcPOU"))
                .OrderBy(f => f)
                .Select(f => TcPouParser.Parse(File.ReadAllText(f)))
                .ToList();

            var structs = DutStructLoader.Load(directories, out var structsSkipped);
            if (structsSkipped.Count > 0)
                throw new IOException($"unreadable .TcDUT in {string.Join(", ", directories)}: {structsSkipped[0].Message}");

            var aliases = DutEnumLoader
                .Load(directories, out var enumsSkipped, out var enumMembers)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            if (enumsSkipped.Count > 0)
                throw new IOException($"unreadable .TcDUT in {string.Join(", ", directories)}: {enumsSkipped[0].Message}");

            return new Engine(new TypeRegistry(pous, structs, null, aliases, enumMembers));
        }
    }
}
