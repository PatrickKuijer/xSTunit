using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using TcXunit.Cli;
using Xunit;

namespace TcXunit.Cli.Tests
{
    // TcXunit-p3t.4/gfs: the capstone acceptance check. p3t.1 got POU + method
    // into the failure log, p3t.2 put a line on every AST node (in-body), p3t.3
    // recorded where each body starts in the .TcPOU file; gfs swapped the
    // human-facing number from the raw file line to the XAE-body-relative one
    // (still keeping the raw file line as a separate JSON field for non-XAE
    // consumers).
    //
    // Both expected numbers are looked up in the fixture file on disk rather
    // than hard-coded, so editing the fixture can't silently turn this into a
    // test of a stale constant (same rationale as FixtureParsingTests) - the
    // faulting statement sits on body line 5 of a method whose ST element
    // starts at file line 21 (file line 25), so dropping BodyStartLine, or
    // adding it without the -1, still fails loudly.
    public class CliRunnerFailureLineTests
    {
        private static readonly string FixtureDir = TestFixtures.FailingLineFixtureDir();

        [Fact]
        public void Run_SuiteFaults_TextLogNamesTheXaeBodyRelativeLine()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { FixtureDir }, output);
            var text = output.ToString();

            Assert.Equal(1, exitCode);
            Assert.Contains(
                $"FB_FailingLineTests: FAIL (in FB_FailingLineHelper.ThrowsFromNestedCall({FaultBodyLine()}): ",
                text);
            Assert.Contains("Method 'ThisMethodDoesNotExist' not found", text);
        }

        [Fact]
        public void Run_SuiteFaults_ReportsInnermostFrameLineNotTheCallersLine()
        {
            var output = new StringWriter();

            CliRunner.Run(new[] { FixtureDir }, output);
            var text = output.ToString();

            // The suite's own call site (`helper.ThrowsFromNestedCall();`) is on
            // a different file line - and in a different file - than the
            // statement that actually threw. Only the innermost one may be
            // reported, otherwise "innermost wins" degraded to "outermost wins"
            // the moment a line got attached to it.
            var callerLine = LineOf("FB_FailingLineTests.TcPOU", "helper.ThrowsFromNestedCall();");
            Assert.NotEqual(callerLine, FaultFileLine());
            Assert.DoesNotContain($"ThrowsFromNestedCall({callerLine})", text);
        }

        [Fact]
        public void Run_SuiteFaults_JsonErrorCarriesTheBodyLine_AndFileLineMovesToItsOwnField()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { FixtureDir, "--format=json" }, output);

            Assert.Equal(1, exitCode);
            using var doc = JsonDocument.Parse(output.ToString());
            var suite = doc.RootElement.GetProperty("suites")[0];
            Assert.Equal("FB_FailingLineTests", suite.GetProperty("name").GetString());

            // error stays a plain string on the same property (TcXunit-p3t.4/gfs
            // add no new JSON structure to it), it now carries the body-relative
            // line rather than the raw file line.
            var error = suite.GetProperty("error");
            Assert.Equal(JsonValueKind.String, error.ValueKind);
            Assert.StartsWith(
                $"FB_FailingLineHelper.ThrowsFromNestedCall({FaultBodyLine()}): ",
                error.GetString());

            // TcXunit-gfs: the raw .TcPOU file line - the number the text/JSON
            // error string used to carry - is not dropped, just demoted to a
            // dedicated structured field for a non-interactive consumer (e.g.
            // an AI agent) opening the .TcPOU file directly rather than
            // through XAE.
            Assert.Equal(FaultFileLine(), suite.GetProperty("fileLine").GetInt32());
        }

        // The raw .TcPOU XML line - counted from the top of the file.
        private static int FaultFileLine() =>
            LineOf("FB_FailingLineHelper.TcPOU", "ThisMethodDoesNotExist();");

        // The XAE-implementation-editor-relative line - counted from the
        // method's own <ST><![CDATA[ line, exactly as XAE's editor would show
        // it, independent of the BodyStartLine + line - 1 arithmetic that
        // produces the raw file line above.
        private static int FaultBodyLine() =>
            BodyLineOf("FB_FailingLineHelper.TcPOU", "ThisMethodDoesNotExist();");

        // 1-based line of the (unique) fixture line whose trimmed text is
        // exactly `statement`.
        private static int LineOf(string fileName, string statement)
        {
            var lines = File.ReadAllLines(Path.Combine(FixtureDir, fileName));
            var matches = Enumerable.Range(0, lines.Length)
                .Where(i => lines[i].Trim() == statement)
                .ToList();
            return Assert.Single(matches) + 1;
        }

        // 1-based line of `statement` counted from its own body's nearest
        // preceding `<ST><![CDATA[` line (line 1), rather than from the top of
        // the file - the same line TwinCAT XAE's editor would show, and
        // independent of the BodyStartLine + line - 1 arithmetic under test.
        private static int BodyLineOf(string fileName, string statement)
        {
            var lines = File.ReadAllLines(Path.Combine(FixtureDir, fileName));
            var statementLine = Enumerable.Range(0, lines.Length).Single(i => lines[i].Trim() == statement);
            var stLine = Enumerable.Range(0, statementLine + 1)
                .Last(i => lines[i].Contains("<ST><![CDATA["));
            return statementLine - stLine + 1;
        }
    }
}
