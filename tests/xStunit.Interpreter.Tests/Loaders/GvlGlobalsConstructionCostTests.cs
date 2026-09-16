using System;
using System.Collections.Generic;
using System.Diagnostics;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;
using Xunit.Abstractions;

namespace xStunit.Interpreter.Tests
{
    // Engine's constructor seeds every VAR_GLOBAL's default value, re-running
    // decls whose initializer could not be settled yet. What this file pins is
    // that the re-running is bounded by how deep the dependency chains
    // actually go, not by how many globals the workspace happens to declare.
    // Left unbounded that way, seeding a workspace's globals costs more than
    // parsing every POU in it and running its suites put together.
    public class GvlGlobalsConstructionCostTests
    {
        private readonly ITestOutputHelper _output;

        public GvlGlobalsConstructionCostTests(ITestOutputHelper output) => _output = output;

        private static readonly StructAst SettingsStruct = new StructAst("uSettings", new[]
        {
            new VarDecl("mode", "INT", null, VarSection.Local),
            new VarDecl("limit", "DINT", null, VarSection.Local),
            new VarDecl("label", "STRING(80)", null, VarSection.Local),
        });

        // Struct- and array-typed globals on purpose: those rebuild an
        // aggregate on every evaluation, which is what made the quadratic pass
        // count expensive rather than merely wasteful.
        private static GvlAst BuildGvl(int index)
        {
            var text =
                "VAR_GLOBAL\n" +
                $"\tstSettings{index} : uSettings;\n" +
                $"\taBuffer{index} : ARRAY[1..32] OF INT;\n" +
                $"\tnCount{index} : UINT := 7;\n" +
                $"\tsName{index} : STRING(80) := 'global';\n" +
                "END_VAR";
            return new GvlAst($"gScratch{index}", text);
        }

        private const int GvlCount = 300;
        private const int DeclsPerGvl = 4;

        [Fact]
        public void Construction_WithManyIndependentGlobals_CostsFarLessThanAPassPerDecl()
        {
            var gvls = new List<GvlAst>();
            for (var i = 0; i < GvlCount; i++)
                gvls.Add(BuildGvl(i));

            var declCount = GvlCount * DeclsPerGvl;
            var registry = new TypeRegistry(
                Array.Empty<PouAst>(), new[] { SettingsStruct }, gvls);

            var stopwatch = Stopwatch.StartNew();
            new Engine(registry);
            stopwatch.Stop();

            _output.WriteLine($"{declCount} globals, construction took {stopwatch.ElapsedMilliseconds} ms");

            // A wall-clock bound, no pass count being observable from outside
            // the constructor. The margin is what makes it signal rather than
            // flake: none of these decls reads another global, so settling
            // takes a single pass and lands near 30ms, where a pass per decl
            // takes about 6s - two orders of magnitude, with the threshold
            // sitting between them.
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(2),
                $"Engine construction over {declCount} globals took {stopwatch.ElapsedMilliseconds} ms, " +
                "which means default-value resolution is re-running every decl once per decl.");
        }
    }
}
