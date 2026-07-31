using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using xStunit.Cli;
using Xunit;

namespace xStunit.Cli.Tests
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

            // TcXunit-3tx.3: the fault happens inside an open TEST() bracket,
            // so it is charged to that test rather than discarding the suite -
            // the FAIL line is the test's, and the located message it carries
            // is unchanged.
            Assert.Equal(1, exitCode);
            Assert.Contains(
                $"FaultsDeepInAHelper: FAIL (FB_FailingLineHelper.ThrowsFromNestedCall({FaultBodyLine()}): ",
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

        // TcXunit-7s6: the fixture's call chain is two interpreted frames deep
        // (FB_FailingLineTests.FaultsDeepInAHelper calls
        // helper.ThrowsFromNestedCall(), which is where the fault actually
        // happens) - a real deep-call-chain fixture, not a hand-built AST, so
        // this exercises the same file-line/body-line arithmetic as the rest
        // of this test class end-to-end through both frames.
        [Fact]
        public void Run_SuiteFaults_TextLogPrintsTheFullCallChainBeneathTheFailLine()
        {
            var output = new StringWriter();

            CliRunner.Run(new[] { FixtureDir }, output);
            var text = output.ToString();

            var callerBodyLine = BodyLineOf("FB_FailingLineTests.TcPOU", "helper.ThrowsFromNestedCall();");

            Assert.Contains($"    at FB_FailingLineHelper.ThrowsFromNestedCall({FaultBodyLine()})", text);
            Assert.Contains($"    at FB_FailingLineTests.FaultsDeepInAHelper({callerBodyLine})", text);

            // Innermost frame's "at" line must appear before the outer one -
            // "innermost first" is a hard contract of CallStack itself
            // (TcXunit-1am), and the console rendering must not reorder it.
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
            // TcXunit-3tx.3: contained into the test that was open, so the
            // chain rides on that test's failure - same array, same contract,
            // same three frames as when it was a suite-level error.
            var callStack = FirstFailure(doc).GetProperty("callStack");

            // Three interpreted frames: the helper method that actually
            // throws, the suite method that called it, and the suite's own
            // top-level body that invoked that method - the outermost frame
            // has no method name (TcXunit-1am's "suite entry point last").
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

            // TcXunit-3tx.3: the fault is contained into the open test, so its
            // message and file line ride on that test's failure rather than on
            // suites[].error/fileLine. Both keep the shape gfs gave them: the
            // message carries the body-relative line, and the raw .TcPOU file
            // line stays a separate structured field for a non-interactive
            // consumer opening the file directly rather than through XAE.
            var failure = FirstFailure(doc);
            Assert.StartsWith(
                $"FB_FailingLineHelper.ThrowsFromNestedCall({FaultBodyLine()}): ",
                failure.GetProperty("message").GetString());
            Assert.Equal(FaultFileLine(), failure.GetProperty("line").GetInt32());
        }

        // The single failure of the single (faulted) test in this fixture.
        private static JsonElement FirstFailure(JsonDocument doc) =>
            doc.RootElement
                .GetProperty("suites")[0]
                .GetProperty("tests")[0]
                .GetProperty("failures")[0];

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
