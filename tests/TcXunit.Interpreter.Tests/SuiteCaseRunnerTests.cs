using System;
using System.IO;
using System.Linq;
using TcXunit.Interpreter;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    public class SuiteCaseRunnerTests
    {
        private static readonly string FixturePouDir = TestFixtures.FbCounterFixtureDir();

        private const string RejectedPou = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <POU Name=""FB_Unsupported"" Id=""{a1b2c3d4-0001-4a1a-8b1b-0000000000ff}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_Unsupported
VAR
	value : INT;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[Tc2_System.SOME_CALL();]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

        [Fact]
        public void DiscoverCases_FixtureProject_ListsAllFourCasesUnderFbCounterTests()
        {
            var cases = SuiteCaseRunner.DiscoverCases(FixturePouDir);

            Assert.Equal(
                new[]
                {
                    ("FB_CounterTests", "CounterStartsAtZero"),
                    ("FB_CounterTests", "IncrementAddsDelta"),
                    ("FB_CounterTests", "DecrementClampsAtZero"),
                    ("FB_CounterTests", "ClampedCounterIncrementRespectsCeiling"),
                },
                cases.Select(c => (c.SuiteName, c.CaseName)));
        }

        [Fact]
        public void RunCase_SpecificCase_ReturnsOnlyThatCasesResult()
        {
            var result = SuiteCaseRunner.RunCase(FixturePouDir, "FB_CounterTests", "IncrementAddsDelta");

            Assert.Equal("IncrementAddsDelta", result.Name);
            Assert.True(result.Passed, result.ToString());
        }

        [Fact]
        public void DiscoverCases_UnparseablePou_SurfacesAsFailingCaseInsteadOfVanishing()
        {
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-" + Guid.NewGuid()));
            try
            {
                foreach (var file in Directory.GetFiles(FixturePouDir, "*.TcPOU"))
                    File.Copy(file, Path.Combine(tempDir.FullName, Path.GetFileName(file)));
                File.WriteAllText(Path.Combine(tempDir.FullName, "FB_Unsupported.TcPOU"), RejectedPou);

                var cases = SuiteCaseRunner.DiscoverCases(tempDir.FullName);

                Assert.Contains(cases, c => c.SuiteName == "FB_Unsupported" && c.CaseName == "(parse error)");

                var result = SuiteCaseRunner.RunCase(tempDir.FullName, "FB_Unsupported", "(parse error)");

                Assert.False(result.Passed);
                Assert.Contains("Tc2_System", result.ToString());
            }
            finally
            {
                Directory.Delete(tempDir.FullName, recursive: true);
            }
        }
    }
}
