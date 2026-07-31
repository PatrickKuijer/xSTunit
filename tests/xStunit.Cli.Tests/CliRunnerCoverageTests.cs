using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using xStunit.Cli;
using Xunit;

namespace xStunit.Cli.Tests
{
    // TcXunit-3tx.4: the CLI surface for the coverage work list. The vendored
    // FbCounterFixture is the natural subject - FB_CounterTests exercises both
    // FB_Counter and FB_ClampedCounter, so an uncovered POU has to be added to
    // see the other half.
    public class CliRunnerCoverageTests : IDisposable
    {
        private readonly string _tempDir;

        public CliRunnerCoverageTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "TcXunitCliCoverageFixture_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
            foreach (var file in Directory.GetFiles(TestFixtures.FbCounterFixtureDir(), "*.TcPOU"))
                File.Copy(file, Path.Combine(_tempDir, Path.GetFileName(file)));
            File.WriteAllText(Path.Combine(_tempDir, "F_ComputeChecksum.TcPOU"), UncoveredFunctionXml);
        }

        public void Dispose() => Directory.Delete(_tempDir, recursive: true);

        [Fact]
        public void Run_WithCoverage_JsonListsEveryNonSuitePouAndTheSuitesCoveringIt()
        {
            using var document = JsonDocument.Parse(RunJson());
            var coverage = document.RootElement.GetProperty("coverage");

            var covered = coverage.EnumerateArray().Single(c => c.GetProperty("pou").GetString() == "FB_Counter");
            Assert.Equal(new[] { "FB_CounterTests" }, covered.GetProperty("suites").EnumerateArray().Select(s => s.GetString()));
        }

        // The line that is the point: directly usable as an agent prompt.
        [Fact]
        public void Run_WithCoverage_JsonReportsAPouNoSuiteMentionsAsUncovered()
        {
            using var document = JsonDocument.Parse(RunJson());
            var coverage = document.RootElement.GetProperty("coverage");

            var uncovered = coverage.EnumerateArray().Single(c => c.GetProperty("pou").GetString() == "F_ComputeChecksum");
            Assert.Empty(uncovered.GetProperty("suites").EnumerateArray());
        }

        // Suites are the test code, not the code under test.
        [Fact]
        public void Run_WithCoverage_DoesNotListTheSuitesThemselves()
        {
            using var document = JsonDocument.Parse(RunJson());

            Assert.DoesNotContain(
                document.RootElement.GetProperty("coverage").EnumerateArray(),
                c => c.GetProperty("pou").GetString() == "FB_CounterTests");
        }

        [Fact]
        public void Run_WithCoverage_TextOutputListsCoveredAndUncoveredPous()
        {
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir, "--coverage" }, output);
            var text = output.ToString();

            Assert.Contains("FB_Counter", text);
            Assert.Contains("suites: FB_CounterTests", text);
            Assert.Contains("F_ComputeChecksum", text);
            Assert.Contains("suites: (none)", text);
        }

        // Coverage is a report, not a gate: an all-passing run with an
        // uncovered POU still exits 0.
        [Fact]
        public void Run_WithCoverage_UncoveredPouDoesNotChangeTheExitCode()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir, "--coverage" }, output);

            Assert.Equal(0, exitCode);
        }

        // Opt-in: a run without the flag emits nothing extra, in either format.
        [Fact]
        public void Run_WithoutCoverage_EmitsNoCoverageSection()
        {
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            using var document = JsonDocument.Parse(output.ToString());
            Assert.False(document.RootElement.TryGetProperty("coverage", out _));
        }

        [Fact]
        public void Run_WithoutCoverage_TextOutputMentionsNoUncoveredPou()
        {
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir }, output);

            Assert.DoesNotContain("suites:", output.ToString());
        }

        // A tree with no suites is a usage error for a RUN, but for a work list
        // it is the most informative answer there is: every POU in it is
        // uncovered. Reporting nothing there would be exactly backwards.
        [Fact]
        public void Run_WithCoverage_TreeWithNoSuitesStillReportsEveryPouAsUncovered()
        {
            var bareDir = Path.Combine(_tempDir, "bare");
            Directory.CreateDirectory(bareDir);
            File.Copy(Path.Combine(_tempDir, "F_ComputeChecksum.TcPOU"), Path.Combine(bareDir, "F_ComputeChecksum.TcPOU"));
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { bareDir, "--format", "json", "--coverage" }, output);

            Assert.Equal(2, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            var uncovered = document.RootElement.GetProperty("coverage").EnumerateArray().Single();
            Assert.Equal("F_ComputeChecksum", uncovered.GetProperty("pou").GetString());
            Assert.Empty(uncovered.GetProperty("suites").EnumerateArray());
        }

        [Fact]
        public void Run_WithCoverage_TreeWithNoSuites_TextOutputListsThemToo()
        {
            var bareDir = Path.Combine(_tempDir, "baretext");
            Directory.CreateDirectory(bareDir);
            File.Copy(Path.Combine(_tempDir, "F_ComputeChecksum.TcPOU"), Path.Combine(bareDir, "F_ComputeChecksum.TcPOU"));
            var output = new StringWriter();

            CliRunner.Run(new[] { bareDir, "--coverage" }, output);

            Assert.Contains("F_ComputeChecksum  suites: (none)", output.ToString());
        }

        private string RunJson()
        {
            var output = new StringWriter();
            CliRunner.Run(new[] { _tempDir, "--format", "json", "--coverage" }, output);
            return output.ToString();
        }

        // A FUNCTION no suite anywhere references.
        private const string UncoveredFunctionXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""F_ComputeChecksum"" Id=""{00000000-0000-0000-0000-0000000000e0}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION F_ComputeChecksum : INT
VAR_INPUT
	nIn : INT;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[F_ComputeChecksum := nIn;]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";
    }
}
