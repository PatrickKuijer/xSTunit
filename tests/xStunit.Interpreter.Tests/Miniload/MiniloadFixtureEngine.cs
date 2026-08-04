using System;
using System.Collections.Generic;
using System.IO;

namespace xStunit.Interpreter.Tests
{
    // Builds the engine every miniload slice runs against, from the fixture
    // files themselves rather than a hand-built AST, through the same
    // WorkspaceLoader a real run is pointed at. Going through the loader rather
    // than globbing .TcPOU here is what keeps the fixtures honest about the
    // file kinds a slice actually depends on: FB_Conveyor reaches its detection
    // point through an I_DigitalInput declared in a .TcIO, and a hand-rolled
    // load that only knew about .TcPOU and .TcDUT would leave that VAR reading
    // as the integer 0 while every suite still passed.
    internal static class MiniloadFixtureEngine
    {
        // Variadic because a slice is not always one directory: FB_Conveyor
        // depends on the I_DigitalInput implementation that lives in the sensor
        // fixture, so the conveyor is not loadable on its own - which is the
        // dependency the equipment module really has, and why the CLI is
        // pointed at both directories too.
        public static Engine Create(params string[] directories)
        {
            var workspace = WorkspaceLoader.Load(directories);

            if (workspace.Error != null)
                throw new IOException($"unloadable fixture in {Named(directories)}: {workspace.Error}");

            // A real tree is allowed to carry files the parser skips; a fixture
            // directory is not. Everything under one is part of the slice under
            // test, so a tolerated skip would silently drop the type a case
            // needs and resurface much later as an unrelated failure.
            if (workspace.Skipped.Count > 0)
            {
                var first = workspace.Skipped[0];
                throw new IOException(
                    $"unreadable fixture file in {DirectoryOf(first.FileKey, directories)}: {first.FileKey}: {first.Message}");
            }

            return new Engine(workspace.Registry);
        }

        // One PLC scan of an instance's own top-level body. Timing tests call
        // this between clock advances, because a timer's elapsed time is only
        // observed on the call that follows the advance. Which instances a
        // fixture steps, and in what order, stays with that fixture - a rig
        // driving a sensor into a conveyor has to scan them upstream first.
        public static void Step(Engine engine, FbInstance instance) =>
            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new List<NamedArg>(), null, null);

        // The one place the walk from an instance to a field of a published
        // struct lives, so no test spells out a Fields chain of its own.
        public static Cell Member(FbInstance instance, string structField, string member) =>
            ((StructInstance)instance.Fields[structField].Value).Fields[member];

        private static string Named(IEnumerable<string> directories) => string.Join(", ", directories);

        // A slice loaded from two directories must still say which one holds
        // the bad file, or the message sends the reader to the wrong fixture.
        // A skip is not always attributable to a path - a duplicate name is
        // keyed by the name - so the whole set is the honest fallback.
        private static string DirectoryOf(string fileKey, IReadOnlyList<string> directories)
        {
            foreach (var directory in directories)
            {
                if (fileKey != null && fileKey.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
                    return directory;
            }

            return Named(directories);
        }
    }
}
