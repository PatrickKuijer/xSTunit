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

        private const string MalformedXmlPou = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <POU Name=""FB_Broken"" Id=""{a1b2c3d4-0002-4a1a-8b1b-0000000000ff}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_Broken
VAR
	value : INT;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[value := 1;]]></ST>
    </Implementation>
</TcPlcObject>";

        private const string MissingStBodyPou = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <POU Name=""FB_NoBody"" Id=""{a1b2c3d4-0003-4a1a-8b1b-0000000000ff}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_NoBody
VAR
	value : INT;
END_VAR]]></Declaration>
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
        public void DiscoverCases_SuiteAndFbInSeparateDirectories_DiscoversAcrossBoth()
        {
            var sourceDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-source-" + Guid.NewGuid()));
            var testsDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-tests-" + Guid.NewGuid()));
            try
            {
                CopyFixtureFile(sourceDir.FullName, "FB_Counter.TcPOU");
                CopyFixtureFile(sourceDir.FullName, "FB_ClampedCounter.TcPOU");
                CopyFixtureFile(testsDir.FullName, "FB_CounterTests.TcPOU");

                var cases = SuiteCaseRunner.DiscoverCases(new[] { sourceDir.FullName, testsDir.FullName });

                Assert.Equal(
                    new[]
                    {
                        ("FB_CounterTests", "CounterStartsAtZero"),
                        ("FB_CounterTests", "IncrementAddsDelta"),
                        ("FB_CounterTests", "DecrementClampsAtZero"),
                        ("FB_CounterTests", "ClampedCounterIncrementRespectsCeiling"),
                    },
                    cases.Select(c => (c.SuiteName, c.CaseName)));

                var result = SuiteCaseRunner.RunCase(
                    new[] { sourceDir.FullName, testsDir.FullName }, "FB_CounterTests", "IncrementAddsDelta");
                Assert.True(result.Passed, result.ToString());
            }
            finally
            {
                Directory.Delete(sourceDir.FullName, recursive: true);
                Directory.Delete(testsDir.FullName, recursive: true);
            }
        }

        [Fact]
        public void DiscoverCases_DuplicateTypeNameAcrossDirectories_ThrowsDuplicatePouTypeException()
        {
            var dirA = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-dupA-" + Guid.NewGuid()));
            var dirB = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-dupB-" + Guid.NewGuid()));
            try
            {
                CopyFixtureFile(dirA.FullName, "FB_Counter.TcPOU");
                CopyFixtureFile(dirB.FullName, "FB_Counter.TcPOU");

                Assert.Throws<TcXunit.Parser.DuplicatePouTypeException>(
                    () => SuiteCaseRunner.DiscoverCases(new[] { dirA.FullName, dirB.FullName }));
            }
            finally
            {
                Directory.Delete(dirA.FullName, recursive: true);
                Directory.Delete(dirB.FullName, recursive: true);
            }
        }

        private static void CopyFixtureFile(string destDir, string fileName) =>
            File.Copy(Path.Combine(FixturePouDir, fileName), Path.Combine(destDir, fileName));

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

        [Fact]
        public void RunCase_ParseErrorCaseWithNoMatchingSkipEntry_ThrowsInvalidOperationExceptionNotNullReference()
        {
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-" + Guid.NewGuid()));
            try
            {
                foreach (var file in Directory.GetFiles(FixturePouDir, "*.TcPOU"))
                    File.Copy(file, Path.Combine(tempDir.FullName, Path.GetFileName(file)));
                File.WriteAllText(Path.Combine(tempDir.FullName, "FB_Unsupported.TcPOU"), RejectedPou);

                // Stale/mismatched pair: caseName is the parse-error sentinel but
                // suiteName does not match any entry in the current skip set
                // (e.g. the skip set changed between DiscoverCases and RunCase).
                // suiteName was never added to the TypeRegistry, so falling
                // through to engine.RunSuite would previously throw a
                // NullReferenceException instead of a clear diagnostic.
                var ex = Assert.Throws<InvalidOperationException>(
                    () => SuiteCaseRunner.RunCase(tempDir.FullName, "FB_DoesNotExist", "(parse error)"));

                Assert.Contains("FB_DoesNotExist", ex.Message);
            }
            finally
            {
                Directory.Delete(tempDir.FullName, recursive: true);
            }
        }

        [Fact]
        public void DiscoverCases_MalformedXmlPou_SurfacesAsFailingCaseInsteadOfAbortingScan()
        {
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-" + Guid.NewGuid()));
            try
            {
                foreach (var file in Directory.GetFiles(FixturePouDir, "*.TcPOU"))
                    File.Copy(file, Path.Combine(tempDir.FullName, Path.GetFileName(file)));
                File.WriteAllText(Path.Combine(tempDir.FullName, "FB_Broken.TcPOU"), MalformedXmlPou);

                var cases = SuiteCaseRunner.DiscoverCases(tempDir.FullName);

                Assert.Contains(cases, c => c.SuiteName == "FB_Broken" && c.CaseName == "(parse error)");
                Assert.Contains(
                    cases,
                    c => c.SuiteName == "FB_CounterTests" && c.CaseName == "IncrementAddsDelta");
            }
            finally
            {
                Directory.Delete(tempDir.FullName, recursive: true);
            }
        }

        [Fact]
        public void DiscoverCases_PouMissingStBody_SurfacesAsFailingCaseInsteadOfAbortingScan()
        {
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-" + Guid.NewGuid()));
            try
            {
                foreach (var file in Directory.GetFiles(FixturePouDir, "*.TcPOU"))
                    File.Copy(file, Path.Combine(tempDir.FullName, Path.GetFileName(file)));
                File.WriteAllText(Path.Combine(tempDir.FullName, "FB_NoBody.TcPOU"), MissingStBodyPou);

                var cases = SuiteCaseRunner.DiscoverCases(tempDir.FullName);

                Assert.Contains(cases, c => c.SuiteName == "FB_NoBody" && c.CaseName == "(parse error)");
                Assert.Contains(
                    cases,
                    c => c.SuiteName == "FB_CounterTests" && c.CaseName == "IncrementAddsDelta");
            }
            finally
            {
                Directory.Delete(tempDir.FullName, recursive: true);
            }
        }
    }
}
