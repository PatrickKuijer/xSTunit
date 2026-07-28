using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using TcXunit.Cli;
using Xunit;

namespace TcXunit.Cli.Tests
{
    // TcXunit-p3t.4: the capstone acceptance check. p3t.1 got POU + method into
    // the failure log, p3t.2 put a line on every AST node (in-body), p3t.3
    // recorded where each body starts in the .TcPOU file; this asserts the
    // three compose into a line the user can actually open their editor at.
    //
    // The expected line is looked up in the fixture file on disk rather than
    // hard-coded, so editing the fixture can't silently turn this into a test
    // of a stale constant (same rationale as FixtureParsingTests) - but the
    // faulting statement sits on body line 5 of a method whose ST element
    // starts at file line 21, so dropping BodyStartLine, or adding it without
    // the -1, still fails loudly.
    public class CliRunnerFailureLineTests
    {
        private static readonly string FixtureDir = TestFixtures.FailingLineFixtureDir();

        [Fact]
        public void Run_SuiteFaults_TextLogNamesTheRealTcPouFileLine()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { FixtureDir }, output);
            var text = output.ToString();

            Assert.Equal(1, exitCode);
            Assert.Contains(
                $"FB_FailingLineTests: FAIL (in FB_FailingLineHelper.ThrowsFromNestedCall({FaultFileLine()}): ",
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
        public void Run_SuiteFaults_JsonErrorCarriesTheLine_AndWireShapeUnchanged()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { FixtureDir, "--format=json" }, output);

            Assert.Equal(1, exitCode);
            using var doc = JsonDocument.Parse(output.ToString());
            var suite = doc.RootElement.GetProperty("suites")[0];
            Assert.Equal("FB_FailingLineTests", suite.GetProperty("name").GetString());

            // error stays a plain string on the same property (TcXunit-p3t.4
            // adds no JSON structure), it just carries the line now.
            var error = suite.GetProperty("error");
            Assert.Equal(JsonValueKind.String, error.ValueKind);
            Assert.StartsWith(
                $"FB_FailingLineHelper.ThrowsFromNestedCall({FaultFileLine()}): ",
                error.GetString());
        }

        private static int FaultFileLine() =>
            LineOf("FB_FailingLineHelper.TcPOU", "ThisMethodDoesNotExist();");

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
    }
}
