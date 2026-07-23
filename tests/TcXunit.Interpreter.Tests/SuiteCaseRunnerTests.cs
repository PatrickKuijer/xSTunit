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

        private static string StructDutXml(string typeName, string fieldDecl) => $@"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <DUT Name=""{typeName}"" Id=""{{a1b2c3d4-0004-4a1a-8b1b-0000000000ff}}"">
    <Declaration><![CDATA[TYPE {typeName} :
STRUCT
	{fieldDecl} : INT;
END_STRUCT
END_TYPE]]></Declaration>
  </DUT>
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

        [Fact]
        public void DiscoverCases_DuplicateStructTypeNameAcrossDirectories_ThrowsDuplicateStructTypeException()
        {
            var dirA = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-dutdupA-" + Guid.NewGuid()));
            var dirB = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-dutdupB-" + Guid.NewGuid()));
            try
            {
                var pathA = Path.Combine(dirA.FullName, "ST_Shared.TcDUT");
                var pathB = Path.Combine(dirB.FullName, "ST_Shared.TcDUT");
                File.WriteAllText(pathA, StructDutXml("ST_Shared", "fieldA"));
                File.WriteAllText(pathB, StructDutXml("ST_Shared", "fieldB"));

                var ex = Assert.Throws<DuplicateStructTypeException>(
                    () => SuiteCaseRunner.DiscoverCases(new[] { dirA.FullName, dirB.FullName }));

                Assert.Equal("ST_Shared", ex.TypeName);
                Assert.Contains(pathA, ex.FilePaths);
                Assert.Contains(pathB, ex.FilePaths);
                Assert.Contains("ST_Shared", ex.Message);
                Assert.Contains(pathA, ex.Message);
                Assert.Contains(pathB, ex.Message);
            }
            finally
            {
                Directory.Delete(dirA.FullName, recursive: true);
                Directory.Delete(dirB.FullName, recursive: true);
            }
        }

        [Fact]
        public void DiscoverCases_SingleStructTypeNameAcrossDirectories_DoesNotThrow()
        {
            var dirA = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-dutokA-" + Guid.NewGuid()));
            var dirB = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-dutokB-" + Guid.NewGuid()));
            try
            {
                File.WriteAllText(Path.Combine(dirA.FullName, "ST_Shared.TcDUT"), StructDutXml("ST_Shared", "fieldA"));

                var cases = SuiteCaseRunner.DiscoverCases(new[] { dirA.FullName, dirB.FullName });

                // No suites declared, but the DUT must have parsed without
                // triggering the duplicate-struct check.
                Assert.Empty(cases);
            }
            finally
            {
                Directory.Delete(dirA.FullName, recursive: true);
                Directory.Delete(dirB.FullName, recursive: true);
            }
        }

        private const string MalformedXmlDut = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <DUT Name=""ST_Broken"" Id=""{a1b2c3d4-0005-4a1a-8b1b-0000000000ff}"">
    <Declaration><![CDATA[TYPE ST_Broken :
STRUCT
	value : INT;
END_STRUCT
END_TYPE]]></Declaration>
</TcPlcObject>";

        [Fact]
        public void DiscoverCases_MalformedTcDutFile_DoesNotAbortRegistryBuild()
        {
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-" + Guid.NewGuid()));
            try
            {
                foreach (var file in Directory.GetFiles(FixturePouDir, "*.TcPOU"))
                    File.Copy(file, Path.Combine(tempDir.FullName, Path.GetFileName(file)));
                File.WriteAllText(Path.Combine(tempDir.FullName, "ST_Broken.TcDUT"), MalformedXmlDut);

                var cases = SuiteCaseRunner.DiscoverCases(tempDir.FullName);

                // The malformed .TcDUT must not throw out of registry build; the
                // rest of the directory's suites still run normally.
                Assert.Contains(
                    cases,
                    c => c.SuiteName == "FB_CounterTests" && c.CaseName == "IncrementAddsDelta");
            }
            finally
            {
                Directory.Delete(tempDir.FullName, recursive: true);
            }
        }

        private static void CopyFixtureFile(string destDir, string fileName) =>
            File.Copy(Path.Combine(FixturePouDir, fileName), Path.Combine(destDir, fileName));

        [Fact]
        public void DiscoverCases_SameNamedRejectedPouInTwoDirectories_SurfacesBothWithoutCollision()
        {
            var dirA = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-rejA-" + Guid.NewGuid()));
            var dirB = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-rejB-" + Guid.NewGuid()));
            try
            {
                CopyFixtureFile(dirA.FullName, "FB_CounterTests.TcPOU");
                CopyFixtureFile(dirA.FullName, "FB_Counter.TcPOU");
                CopyFixtureFile(dirA.FullName, "FB_ClampedCounter.TcPOU");
                File.WriteAllText(Path.Combine(dirA.FullName, "FB_Unsupported.TcPOU"), RejectedPou);
                File.WriteAllText(Path.Combine(dirB.FullName, "FB_Unsupported.TcPOU"), RejectedPou);

                var cases = SuiteCaseRunner.DiscoverCases(new[] { dirA.FullName, dirB.FullName });

                var skippedCases = cases.Where(c => c.CaseName == "(parse error)").ToList();

                // Two same-named rejected POUs across the two directories must
                // surface as two distinct SuiteCase entries (unique full-path
                // keys), not collapse into one ambiguous duplicate.
                Assert.Equal(2, skippedCases.Count);
                Assert.Equal(skippedCases.Select(c => c.SuiteName).Distinct().Count(), skippedCases.Count);

                var expectedPathA = Path.Combine(dirA.FullName, "FB_Unsupported.TcPOU");
                var expectedPathB = Path.Combine(dirB.FullName, "FB_Unsupported.TcPOU");
                Assert.Contains(skippedCases, c => c.SuiteName == expectedPathA);
                Assert.Contains(skippedCases, c => c.SuiteName == expectedPathB);

                var resultA = SuiteCaseRunner.RunCase(
                    new[] { dirA.FullName, dirB.FullName }, expectedPathA, "(parse error)");
                var resultB = SuiteCaseRunner.RunCase(
                    new[] { dirA.FullName, dirB.FullName }, expectedPathB, "(parse error)");

                Assert.False(resultA.Passed);
                Assert.False(resultB.Passed);
                Assert.Contains("Tc2_System", resultA.ToString());
                Assert.Contains("Tc2_System", resultB.ToString());
            }
            finally
            {
                Directory.Delete(dirA.FullName, recursive: true);
                Directory.Delete(dirB.FullName, recursive: true);
            }
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

                var skippedCase = Assert.Single(
                    cases, c => c.CaseName == "(parse error)" && c.SuiteName.Contains("FB_Unsupported"));
                Assert.Equal(Path.Combine(tempDir.FullName, "FB_Unsupported.TcPOU"), skippedCase.SuiteName);

                var result = SuiteCaseRunner.RunCase(tempDir.FullName, skippedCase.SuiteName, "(parse error)");

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

                Assert.Contains(
                    cases, c => c.SuiteName.Contains("FB_Broken") && c.CaseName == "(parse error)");
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

                Assert.Contains(
                    cases, c => c.SuiteName.Contains("FB_NoBody") && c.CaseName == "(parse error)");
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
