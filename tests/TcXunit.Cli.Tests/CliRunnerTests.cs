using System;
using System.IO;
using TcXunit.Cli;
using Xunit;

namespace TcXunit.Cli.Tests
{
    public class CliRunnerTests
    {
        private static readonly string FixturePouDir = TestFixtures.FbCounterFixtureDir();

        [Fact]
        public void Run_FixtureProject_PrintsFourPassesAndReturnsZero()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { FixturePouDir }, output);

            Assert.Equal(0, exitCode);
            var text = output.ToString();
            Assert.Equal(4, CountOccurrences(text, "PASS"));
            Assert.DoesNotContain("FAIL", text);
        }

        [Fact]
        public void Run_MissingPath_ReturnsTwoAndPrintsError()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { @"C:\this\path\does\not\exist" }, output);

            Assert.Equal(2, exitCode);
            Assert.Contains("does not exist", output.ToString());
        }

        [Fact]
        public void Run_NoArgs_ReturnsTwoAndPrintsUsage()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new string[0], output);

            Assert.Equal(2, exitCode);
            Assert.Contains("usage", output.ToString().ToLowerInvariant());
        }

        [Fact]
        public void Run_SourceAndTestsInSeparateDirectories_UnionsAndPassesAllFour()
        {
            var sourceDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-cli-source-" + Guid.NewGuid()));
            var testsDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-cli-tests-" + Guid.NewGuid()));
            try
            {
                CopyFixtureFile(sourceDir.FullName, "FB_Counter.TcPOU");
                CopyFixtureFile(sourceDir.FullName, "FB_ClampedCounter.TcPOU");
                CopyFixtureFile(testsDir.FullName, "FB_CounterTests.TcPOU");

                var output = new StringWriter();

                var exitCode = CliRunner.Run(new[] { sourceDir.FullName, testsDir.FullName }, output);

                Assert.Equal(0, exitCode);
                var text = output.ToString();
                Assert.Equal(4, CountOccurrences(text, "PASS"));
                Assert.DoesNotContain("FAIL", text);
            }
            finally
            {
                Directory.Delete(sourceDir.FullName, recursive: true);
                Directory.Delete(testsDir.FullName, recursive: true);
            }
        }

        [Fact]
        public void Run_DuplicateTypeNameAcrossDirectories_ReturnsTwoAndPrintsClearError()
        {
            var dirA = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-cli-dupA-" + Guid.NewGuid()));
            var dirB = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-cli-dupB-" + Guid.NewGuid()));
            try
            {
                CopyFixtureFile(dirA.FullName, "FB_Counter.TcPOU");
                CopyFixtureFile(dirB.FullName, "FB_Counter.TcPOU");

                var output = new StringWriter();

                var exitCode = CliRunner.Run(new[] { dirA.FullName, dirB.FullName }, output);

                Assert.Equal(2, exitCode);
                Assert.Contains("FB_Counter", output.ToString());
            }
            finally
            {
                Directory.Delete(dirA.FullName, recursive: true);
                Directory.Delete(dirB.FullName, recursive: true);
            }
        }

        [Fact]
        public void Run_LaterArgMissingPath_ReturnsTwoAndNamesThatPath()
        {
            // The path-existence loop in CliRunner.Run walks all of args, not
            // just args[0] (b22ecd27). A single-missing-path test can't tell
            // a "check args[0] only" regression from a "check every arg"
            // implementation, since both behave identically when args has
            // one element. This exercises a valid first directory alongside
            // an invalid second one, and asserts the second (missing) path
            // is the one named in the error.
            var missingPath = Path.Combine(Path.GetTempPath(), "tcxunit-cli-missing-" + Guid.NewGuid());

            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { FixturePouDir, missingPath }, output);

            Assert.Equal(2, exitCode);
            var text = output.ToString();
            Assert.Contains("does not exist", text);
            Assert.Contains(missingPath, text);
        }

        [Fact]
        public void Run_SameDirectoryPassedTwice_DeduplicatesAndPassesAllFour()
        {
            // Locks in current behavior (coverage-only, TcXunit-drc): passing
            // the same directory twice does NOT double-parse its files and
            // does NOT trip DuplicatePouTypeException. MultiDirectoryPouLoader
            // normalizes/de-duplicates the resolved file paths
            // (DeduplicatePaths, 70f3196) before parsing, so repeating an
            // input directory is a no-op union with itself, identical to
            // passing it once. If a future change reintroduces per-file
            // duplication for repeated input directories, this test should
            // start failing (or start reporting duplicate-type errors)
            // instead of silently passing.
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { FixturePouDir, FixturePouDir }, output);

            Assert.Equal(0, exitCode);
            var text = output.ToString();
            Assert.Equal(4, CountOccurrences(text, "PASS"));
            Assert.DoesNotContain("FAIL", text);
        }

        [Fact]
        public void Run_SuiteDependsOnStructTypeFromTcDutFile_ResolvesAndPasses()
        {
            // TcXunit-9li: `tcxunit run` previously built its TypeRegistry with
            // no struct types at all (unlike SuiteCaseRunner.BuildRegistry),
            // so any suite/FB depending on a STRUCT declared in a .TcDUT file
            // failed to resolve via the CLI even though Test Explorer
            // discovery worked. A suite referencing ST_Msg here must run and
            // pass through the CLI entry point.
            var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-cli-struct-" + Guid.NewGuid()));
            try
            {
                File.WriteAllText(Path.Combine(dir.FullName, "ST_Msg.TcDUT"), StructDutXml);
                File.WriteAllText(Path.Combine(dir.FullName, "FB_StructTests.TcPOU"), StructSuitePouXml);

                var output = new StringWriter();

                var exitCode = CliRunner.Run(new[] { dir.FullName }, output);

                var text = output.ToString();
                Assert.Equal(0, exitCode);
                Assert.Equal(1, CountOccurrences(text, "PASS"));
                Assert.DoesNotContain("FAIL", text);
            }
            finally
            {
                Directory.Delete(dir.FullName, recursive: true);
            }
        }

        [Fact]
        public void Run_PouOutsideParseSubset_ReturnsTwoAndPrintsClearError()
        {
            var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-cli-rejected-" + Guid.NewGuid()));
            try
            {
                File.WriteAllText(Path.Combine(dir.FullName, "FB_UsesTc2System.TcPOU"), RejectedPouXml);

                var output = new StringWriter();

                var exitCode = CliRunner.Run(new[] { dir.FullName }, output);

                Assert.Equal(2, exitCode);
                Assert.Contains("Tc2_System", output.ToString());
            }
            finally
            {
                Directory.Delete(dir.FullName, recursive: true);
            }
        }

        private const string RejectedPouXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_UsesTc2System"" Id=""{00000000-0000-0000-0000-0000000000ba}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_UsesTc2System]]></Declaration>
    <Implementation>
      <ST><![CDATA[Tc2_System.SOME_FUNCTION();]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

        private const string StructDutXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <DUT Name=""ST_Msg"" Id=""{00000000-0000-0000-0000-0000000000db}"">
    <Declaration><![CDATA[TYPE ST_Msg :
STRUCT
	count : INT;
END_STRUCT
END_TYPE]]></Declaration>
  </DUT>
</TcPlcObject>";

        private const string StructSuitePouXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <POU Name=""FB_StructTests"" Id=""{00000000-0000-0000-0000-0000000000dc}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_StructTests EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[StructFieldDefaultsToZero();]]></ST>
    </Implementation>
    <Method Name=""StructFieldDefaultsToZero"" Id=""{00000000-0000-0000-0000-0000000000dd}"">
      <Declaration><![CDATA[METHOD PRIVATE StructFieldDefaultsToZero
VAR
	msg : ST_Msg;
END_VAR
]]></Declaration>
      <Implementation>
        <ST><![CDATA[TEST('StructFieldDefaultsToZero');

msg.count := 5;

AssertEquals_INT(Expected := 5,
                  Actual := msg.count,
                  Message := 'ST_Msg field roundtrip');

TEST_FINISHED();]]></ST>
      </Implementation>
    </Method>
  </POU>
</TcPlcObject>";

        private static void CopyFixtureFile(string destDir, string fileName) =>
            File.Copy(Path.Combine(FixturePouDir, fileName), Path.Combine(destDir, fileName));

        private static int CountOccurrences(string haystack, string needle)
        {
            var count = 0;
            var index = 0;
            while ((index = haystack.IndexOf(needle, index, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }
            return count;
        }
    }
}
