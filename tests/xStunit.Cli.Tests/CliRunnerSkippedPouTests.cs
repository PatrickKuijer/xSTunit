using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using xStunit.Cli;
using Xunit;

namespace xStunit.Cli.Tests
{
    // A POU outside the parse subset, or a structurally unexpected file, is
    // skipped and reported individually instead of aborting the run: one bad
    // file must never cost a project the results of every good one. Isolated
    // temp-directory fixture so these deliberately-unsupported POUs don't
    // pollute the shared FB_Counter fixture's all-green state.
    public class CliRunnerSkippedPouTests : IDisposable
    {
        private readonly string _tempDir;

        public CliRunnerSkippedPouTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "xStunitCliSkipFixture_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
            File.WriteAllText(Path.Combine(_tempDir, "FB_UsesTc2System.TcPOU"), UnsupportedPouXml);
            File.WriteAllText(Path.Combine(_tempDir, "FB_PassingTests.TcPOU"), PassingSuiteXml);
        }

        public void Dispose() => Directory.Delete(_tempDir, recursive: true);

        [Fact]
        public void Run_UnsupportedPouAlongsideSuite_StillRunsSuiteAndReportsSkip()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir }, output);

            var text = output.ToString();
            // Exit 0 (a completed run), not 2 - the unsupported POU is a skip,
            // not a usage/discovery error.
            Assert.Equal(0, exitCode);
            Assert.Contains("PASS", text);
            Assert.Contains("1 passed, 0 failed, 1 skipped", text);
            Assert.Contains("FB_UsesTc2System.TcPOU", text);
            Assert.Contains("outside the v1 parse subset", text);
        }

        [Fact]
        public void Run_MalformedPouAlongsideSuite_StillRunsSuiteAndReportsSkip()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_Malformed.TcPOU"), "<TcPlcObject><POU Name=");
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir }, output);

            var text = output.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("1 passed, 0 failed, 2 skipped", text);
            Assert.Contains("FB_Malformed.TcPOU", text);
        }

        // A file that can't even be READ - as opposed to the malformed XML
        // above - throws IOException/UnauthorizedAccessException rather than a
        // parse error. If the scan's guard doesn't catch those too, the whole
        // enumeration aborts and every file it hadn't reached yet, sibling
        // suites included, disappears from the run without a word.
        [Fact]
        public void Run_UnreadablePouAlongsideSuite_StillRunsSuiteAndReportsSkip()
        {
            var unreadablePath = Path.Combine(_tempDir, "FB_Unreadable.TcPOU");
            File.WriteAllText(unreadablePath, PassingSuiteXml.Replace("FB_PassingTests", "FB_Unrelated"));

            if (OperatingSystem.IsWindows())
            {
                return;
            }

            File.SetUnixFileMode(unreadablePath, UnixFileMode.None);
            try
            {
                if (CanStillRead(unreadablePath))
                {
                    // Running as root, or anywhere else permissions aren't
                    // enforced: the premise "this file cannot be read" cannot be
                    // constructed, so there is nothing left to check.
                    return;
                }

                var output = new StringWriter();

                var exitCode = CliRunner.Run(new[] { _tempDir }, output);

                var text = output.ToString();
                Assert.Equal(0, exitCode);
                Assert.Contains("1 passed, 0 failed, 2 skipped", text);
                Assert.Contains("FB_Unreadable.TcPOU", text);
            }
            finally
            {
                // Dispose cannot delete the temp directory otherwise.
                File.SetUnixFileMode(unreadablePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        private static bool CanStillRead(string path)
        {
            try
            {
                File.ReadAllText(path);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }

        [Fact]
        public void Run_UnsupportedPou_JsonFormat_ListsSkippedFileAndReason()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            Assert.Equal(0, exitCode);
            using var doc = JsonDocument.Parse(output.ToString());
            var root = doc.RootElement;
            Assert.Equal(1, root.GetProperty("passed").GetInt32());
            var skipped = root.GetProperty("skipped");
            Assert.Equal(1, skipped.GetArrayLength());
            Assert.EndsWith("FB_UsesTc2System.TcPOU", skipped[0].GetProperty("filePath").GetString());
            Assert.Contains("outside the v1 parse subset", skipped[0].GetProperty("reason").GetString());
        }

        [Fact]
        public void Run_NoSkips_JsonFormat_EmitsEmptySkippedArray()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { TestFixtures.FbCounterFixtureDir(), "--format", "json" }, output);

            Assert.Equal(0, exitCode);
            using var doc = JsonDocument.Parse(output.ToString());
            Assert.Equal(0, doc.RootElement.GetProperty("skipped").GetArrayLength());
        }

        // With every candidate POU unsupported the run is a genuine discovery
        // error, but "no suites found" on its own hides the reason - each
        // dropped file still has to be named.
        [Fact]
        public void Run_OnlyUnsupportedPous_ReturnsTwoAndReportsEachSkip()
        {
            var onlyUnsupportedDir = Path.Combine(_tempDir, "onlyUnsupported");
            Directory.CreateDirectory(onlyUnsupportedDir);
            File.WriteAllText(Path.Combine(onlyUnsupportedDir, "FB_UsesTc2System.TcPOU"), UnsupportedPouXml);
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { onlyUnsupportedDir }, output);

            var text = output.ToString();
            Assert.Equal(2, exitCode);
            Assert.Contains("no TcUnit suites found", text);
            Assert.Contains("FB_UsesTc2System.TcPOU", text);
        }

        [Fact]
        public void Run_OnlyUnsupportedPous_JsonFormat_ReportsErrorWithSkips()
        {
            var onlyUnsupportedDir = Path.Combine(_tempDir, "onlyUnsupportedJson");
            Directory.CreateDirectory(onlyUnsupportedDir);
            File.WriteAllText(Path.Combine(onlyUnsupportedDir, "FB_UsesTc2System.TcPOU"), UnsupportedPouXml);
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { onlyUnsupportedDir, "--format=json" }, output);

            Assert.Equal(2, exitCode);
            using var doc = JsonDocument.Parse(output.ToString());
            var root = doc.RootElement;
            Assert.Contains("no TcUnit suites found", root.GetProperty("error").GetString());
            Assert.Equal(1, root.GetProperty("skipped").GetArrayLength());
        }

        // A POU drawn in a graphical language is understood and deliberately
        // not run, not broken: its skip line has to read like the Tc2_System
        // one - naming the POU and the language - with no "Failed to parse"
        // wrapper and nothing about a null reference.
        [Fact]
        public void Run_GraphicalPouAlongsideSuite_SkipReadsAsUnsupportedConstructNotParseFailure()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_Graphical.TcPOU"), GraphicalPouXml);
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir }, output);

            var text = output.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("1 passed, 0 failed, 2 skipped", text);
            Assert.Contains(
                "'FB_Graphical' has no ST body (implementation language 'NWL'), " +
                "which is outside the v1 parse subset (not yet implemented).",
                text);
            Assert.DoesNotContain("Failed to parse 'FB_Graphical.TcPOU'", text);
            Assert.DoesNotContain("Object reference not set", text);
        }

        // A struct DUT with EXTENDS is a skip, never an exit-2 discovery
        // error, and the suite next to it still runs.
        [Fact]
        public void Run_ExtendsStructDutAlongsideSuite_StillRunsSuiteAndReportsSkip()
        {
            const string declaration = "TYPE ST_Child EXTENDS ST_Base :\nSTRUCT\n\tx : REAL;\nEND_STRUCT\nEND_TYPE";
            File.WriteAllText(
                Path.Combine(_tempDir, "ST_Child.TcDUT"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<TcPlcObject Version=\"1.1.0.1\">\n" +
                "  <DUT Name=\"ST_Child\" Id=\"{00000000-0000-0000-0000-0000000000d0}\">\n" +
                "    <Declaration><![CDATA[" + declaration + "]]></Declaration>\n  </DUT>\n</TcPlcObject>");
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir }, output);

            var text = output.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("1 passed, 0 failed, 2 skipped", text);
            Assert.Contains("ST_Child.TcDUT", text);
            Assert.Contains("inheritance is not supported", text);
        }

        // The POU-body case is the one real trees hit today; a graphical METHOD
        // on an otherwise ST-bodied FB is the same defect one level down, and
        // reaches the user through the same skip line.
        [Fact]
        public void Run_GraphicalMethodAlongsideSuite_SkipNamesTheMethodAndTheLanguage()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_MixedLanguages.TcPOU"), GraphicalMethodPouXml);
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir }, output);

            var text = output.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("FB_MixedLanguages.TcPOU", text);
            Assert.Contains("'DrawnInLadder' has no ST body (implementation language 'NWL')", text);
            Assert.DoesNotContain("Object reference not set", text);
        }

        // Production-shaped POU the parser rejects outright, on account of the
        // library call in its body.
        private const string UnsupportedPouXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_UsesTc2System"" Id=""{00000000-0000-0000-0000-0000000000b0}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_UsesTc2System
VAR
    fbTimer : Tc2_System.GETCURTASKINDEX;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[Tc2_System.GETCURTASKINDEX();]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

        // How TwinCAT writes an LD/FBD body: a network list where the <ST>
        // element would be.
        private const string GraphicalPouXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_Graphical"" Id=""{00000000-0000-0000-0000-0000000000c0}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_Graphical
VAR
	bFlag : BOOL;
END_VAR]]></Declaration>
    <Implementation>
      <NWL>
        <XmlArchive>
          <Data>
            <o xml:space=""preserve"" t=""NWLImplementationObject"">
              <v n=""NetworkListComment"">""""</v>
            </o>
          </Data>
        </XmlArchive>
      </NWL>
    </Implementation>
  </POU>
</TcPlcObject>";

        private const string GraphicalMethodPouXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_MixedLanguages"" Id=""{00000000-0000-0000-0000-0000000000c1}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_MixedLanguages
VAR
	bFlag : BOOL;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[bFlag := TRUE;]]></ST>
    </Implementation>
    <Method Name=""DrawnInLadder"" Id=""{00000000-0000-0000-0000-0000000000c2}"">
      <Declaration><![CDATA[METHOD PUBLIC DrawnInLadder : BOOL]]></Declaration>
      <Implementation>
        <NWL>
          <XmlArchive>
            <Data>
              <o xml:space=""preserve"" t=""NWLImplementationObject"">
                <v n=""NetworkListComment"">""""</v>
              </o>
            </Data>
          </XmlArchive>
        </NWL>
      </Implementation>
    </Method>
  </POU>
</TcPlcObject>";

        private const string PassingSuiteXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_PassingTests"" Id=""{00000000-0000-0000-0000-0000000000b1}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_PassingTests EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[ThisAlwaysPasses();]]></ST>
    </Implementation>
    <Method Name=""ThisAlwaysPasses"" Id=""{00000000-0000-0000-0000-0000000000b2}"">
      <Declaration><![CDATA[METHOD PRIVATE ThisAlwaysPasses
]]></Declaration>
      <Implementation>
        <ST><![CDATA[TEST('ThisAlwaysPasses');

AssertTrue(Condition := (1 = 1),
           Message := 'one is always one');

TEST_FINISHED();]]></ST>
      </Implementation>
    </Method>
  </POU>
</TcPlcObject>";
    }
}
