using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using xStunit.Cli;
using Xunit;

namespace xStunit.Cli.Tests
{
    // A located failure carries two different line numbers: the human-facing
    // one is body-relative, exactly as the XAE editor shows it, while the raw
    // .TcPOU file line stays a separate JSON field for consumers opening the
    // file directly. The fault here is charged to the open TEST() bracket, so
    // both ride on that test's failure rather than on the suite.
    //
    // Both numbers are looked up in the fixture on disk rather than hard-coded,
    // so editing the fixture cannot quietly turn these into tests of a stale
    // constant - and the lookups are computed independently of the
    // body-start arithmetic under test, so dropping that arithmetic (or
    // applying it off by one) still fails loudly.
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
                $"FaultsDeepInAHelper: FAIL (FB_FailingLineHelper.ThrowsFromNestedCall({FaultBodyLine()}): ",
                text);
            Assert.Contains("'ThisMethodDoesNotExist' not found", text);
        }

        [Fact]
        public void Run_SuiteFaults_ReportsInnermostFrameLineNotTheCallersLine()
        {
            var output = new StringWriter();

            CliRunner.Run(new[] { FixtureDir }, output);
            var text = output.ToString();

            // The call site is in a different file, and on a different line,
            // than the statement that threw - so a report that names the caller
            // is not merely imprecise, it points at the wrong source file.
            var callerLine = LineOf("FB_FailingLineTests.TcPOU", "helper.ThrowsFromNestedCall();");
            Assert.NotEqual(callerLine, FaultFileLine());
            Assert.DoesNotContain($"ThrowsFromNestedCall({callerLine})", text);
        }

        [Fact]
        public void Run_SuiteFaults_TextLogPrintsTheFullCallChainBeneathTheFailLine()
        {
            var output = new StringWriter();

            CliRunner.Run(new[] { FixtureDir }, output);
            var text = output.ToString();

            var callerBodyLine = BodyLineOf("FB_FailingLineTests.TcPOU", "helper.ThrowsFromNestedCall();");

            Assert.Contains($"    at FB_FailingLineHelper.ThrowsFromNestedCall({FaultBodyLine()})", text);
            Assert.Contains($"    at FB_FailingLineTests.FaultsDeepInAHelper({callerBodyLine})", text);

            // "Innermost first" is a contract of the call stack itself, and the
            // console rendering must not reorder it.
            var innerIndex = text.IndexOf($"at FB_FailingLineHelper.ThrowsFromNestedCall({FaultBodyLine()})", StringComparison.Ordinal);
            var outerIndex = text.IndexOf($"at FB_FailingLineTests.FaultsDeepInAHelper({callerBodyLine})", StringComparison.Ordinal);
            Assert.True(innerIndex >= 0 && outerIndex >= 0 && innerIndex < outerIndex);
        }

        [Fact]
        public void Run_SuiteFaults_JsonCallStackCapturesAllThreeFramesInnermostFirst()
        {
            var output = new StringWriter();

            CliRunner.Run(new[] { FixtureDir, "--format=json" }, output);

            using var doc = JsonDocument.Parse(output.ToString());
            var callStack = FirstFailure(doc).GetProperty("callStack");

            // Three interpreted frames: the helper method that throws, the
            // suite method that called it, and the suite's own top-level body -
            // that outermost frame is the entry point, so it has no method name.
            Assert.Equal(3, callStack.GetArrayLength());

            var inner = callStack[0];
            Assert.Equal("FB_FailingLineHelper", inner.GetProperty("pouTypeName").GetString());
            Assert.Equal("ThrowsFromNestedCall", inner.GetProperty("methodName").GetString());
            Assert.Equal(FaultFileLine(), inner.GetProperty("line").GetInt32());
            Assert.Equal(FaultBodyLine(), inner.GetProperty("bodyLine").GetInt32());

            var middle = callStack[1];
            Assert.Equal("FB_FailingLineTests", middle.GetProperty("pouTypeName").GetString());
            Assert.Equal("FaultsDeepInAHelper", middle.GetProperty("methodName").GetString());

            var callerFileLine = LineOf("FB_FailingLineTests.TcPOU", "helper.ThrowsFromNestedCall();");
            var callerBodyLine = BodyLineOf("FB_FailingLineTests.TcPOU", "helper.ThrowsFromNestedCall();");
            Assert.Equal(callerFileLine, middle.GetProperty("line").GetInt32());
            Assert.Equal(callerBodyLine, middle.GetProperty("bodyLine").GetInt32());

            var outer = callStack[2];
            Assert.Equal("FB_FailingLineTests", outer.GetProperty("pouTypeName").GetString());
            Assert.Equal(JsonValueKind.Null, outer.GetProperty("methodName").ValueKind);
        }

        [Fact]
        public void Run_SuiteFaults_JsonErrorCarriesTheBodyLine_AndFileLineMovesToItsOwnField()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { FixtureDir, "--format=json" }, output);

            Assert.Equal(1, exitCode);
            using var doc = JsonDocument.Parse(output.ToString());
            Assert.Equal("FB_FailingLineTests", doc.RootElement.GetProperty("suites")[0].GetProperty("name").GetString());

            var failure = FirstFailure(doc);
            Assert.StartsWith(
                $"FB_FailingLineHelper.ThrowsFromNestedCall({FaultBodyLine()}): ",
                failure.GetProperty("message").GetString());
            Assert.Equal(FaultFileLine(), failure.GetProperty("line").GetInt32());
        }

        private static JsonElement FirstFailure(JsonDocument doc) =>
            doc.RootElement
                .GetProperty("suites")[0]
                .GetProperty("tests")[0]
                .GetProperty("failures")[0];

        private static int FaultFileLine() =>
            LineOf("FB_FailingLineHelper.TcPOU", "ThisMethodDoesNotExist();");

        private static int FaultBodyLine() =>
            BodyLineOf("FB_FailingLineHelper.TcPOU", "ThisMethodDoesNotExist();");

        // 1-based line of the fixture line whose trimmed text is exactly
        // `statement`; asserts it is unique, so an edit that duplicates the
        // statement fails here rather than silently picking one.
        private static int LineOf(string fileName, string statement)
        {
            var lines = File.ReadAllLines(Path.Combine(FixtureDir, fileName));
            var matches = Enumerable.Range(0, lines.Length)
                .Where(i => lines[i].Trim() == statement)
                .ToList();
            return Assert.Single(matches) + 1;
        }

        // 1-based line counted from the body's own `<ST><![CDATA[` line rather
        // than the top of the file - derived straight from the fixture text, so
        // it stays independent of the arithmetic under test.
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
