using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using xStunit.Interpreter;
using xStunit.Parser;
using xStunit.Runner.TcUnitStub;

namespace xStunit.Cli
{
    // Everything a run has to say, as events: the run reports what happened,
    // one of the subclasses below decides what that looks like on stdout.
    //
    // Format is chosen once, by Create, and never again. Under --stream stdout
    // must stay one JSON object per line, and a single plain text line
    // interleaved with the NDJSON breaks that; dispatching on the writer's type
    // rather than re-testing a flag at every write site is what makes the wrong
    // line impossible rather than merely guarded against.
    //
    // The tally and the assembled suite list live here, not in the subclasses:
    // which tests passed is not a rendering decision, and the summary of a run
    // must not depend on how it was printed.
    internal abstract class RunReportWriter
    {
        private readonly List<SuiteReport> _suites = new List<SuiteReport>();
        private bool _anyFailed;

        protected RunReportWriter(TextWriter output)
        {
            Output = output;
            // Both FailureGuidance overloads are handed over: the report module
            // owns the shape of what goes on the wire, the guidance rules own
            // the wording that goes inside it.
            Reports = new RunReportBuilder(FailureGuidance.For, FailureGuidance.For);
        }

        public static RunReportWriter Create(TextWriter output, bool asJson, bool streaming)
        {
            if (streaming)
                return new NdjsonRunReportWriter(output);
            return asJson ? (RunReportWriter)new JsonRunReportWriter(output) : new TextRunReportWriter(output);
        }

        protected TextWriter Output { get; }

        protected RunReportBuilder Reports { get; }

        protected IReadOnlyList<SuiteReport> Suites => _suites;

        protected int PassCount { get; private set; }

        protected int FailCount { get; private set; }

        // Returns the exit code for a usage or discovery error, because
        // reporting one and exiting 2 are the same decision: this is the only
        // path that produces a run with no results at all.
        public int Error(
            string message,
            IReadOnlyList<SkippedFile> skipped,
            IReadOnlyList<DeclarationWarning> warnings,
            IReadOnlyList<PouCoverage> coverage)
        {
            WriteError(message, skipped, warnings, coverage);
            return 2;
        }

        protected abstract void WriteError(
            string message,
            IReadOnlyList<SkippedFile> skipped,
            IReadOnlyList<DeclarationWarning> warnings,
            IReadOnlyList<PouCoverage> coverage);

        public virtual void PluginsLoaded(IReadOnlyList<string> plugins)
        {
        }

        public virtual void Discovery(
            IReadOnlyList<string> suiteNames, IReadOnlyDictionary<string, string> filePathsByTypeName)
        {
        }

        public virtual void SuiteStart(string suiteName)
        {
        }

        // Shared by the two paths that have test results to report: a suite
        // that ran to completion, and one that faulted after some of its tests
        // had already finished. Counting them in one place is what keeps
        // `passed`/`failed` describing the same tests the report lists.
        public IReadOnlyList<TestReport> ReportTests(IReadOnlyList<TestCaseResult> results)
        {
            var reports = new List<TestReport>();
            foreach (var result in results)
            {
                var report = Reports.Test(result);
                reports.Add(report);
                WriteTest(report);
                if (result.Passed)
                    PassCount++;
                else
                {
                    FailCount++;
                    _anyFailed = true;
                }
            }
            return reports;
        }

        protected virtual void WriteTest(TestReport test)
        {
        }

        public void SuiteCompleted(
            string suiteName, string filePath, IReadOnlyList<TestReport> tests, long durationMs)
        {
            var suite = Reports.Suite(suiteName, filePath, tests, durationMs);
            _suites.Add(suite);
            WriteSuiteCompleted(suite);
        }

        protected virtual void WriteSuiteCompleted(SuiteReport suite)
        {
        }

        // `completedTests` are the tests that finished before the fault, in the
        // order they happened; the fault came after them.
        public void SuiteFailed(
            string suiteName, string filePath, Exception exception, IReadOnlyList<TestReport> completedTests)
        {
            var fault = SuiteFault.Classify(exception);
            var suite = Reports.SuiteError(
                suiteName, filePath, fault.Error, fault.Detail, fault.Kind, fault.Construct, completedTests,
                fault.Located);
            _suites.Add(suite);
            // The suite-level fault is a failure in its own right, on top of
            // whatever the tests above reported: a run cannot pass just because
            // everything that got to run passed.
            FailCount++;
            _anyFailed = true;
            WriteSuiteFailed(suite);
        }

        protected virtual void WriteSuiteFailed(SuiteReport suite)
        {
        }

        // Returns the run's exit code. Skipped files must never change it: a
        // run that skipped unsupported POUs but ran everything else is still a
        // completed run (0 or 1 by test outcome). Exit 2 stays reserved for
        // usage and discovery errors that produced no results at all; the skip
        // list is what tells the caller coverage was reduced.
        public int Summary(
            IReadOnlyList<SkippedFile> skipped,
            IReadOnlyList<DeclarationWarning> warnings,
            IReadOnlyList<PouCoverage> coverage)
        {
            var exitCode = _anyFailed ? 1 : 0;
            WriteSummary(exitCode, skipped, warnings, coverage);
            return exitCode;
        }

        protected abstract void WriteSummary(
            int exitCode,
            IReadOnlyList<SkippedFile> skipped,
            IReadOnlyList<DeclarationWarning> warnings,
            IReadOnlyList<PouCoverage> coverage);
    }

    // What a human reads at a console: results as they finish, then a count.
    internal sealed class TextRunReportWriter : RunReportWriter
    {
        public TextRunReportWriter(TextWriter output)
            : base(output)
        {
        }

        protected override void WriteError(
            string message,
            IReadOnlyList<SkippedFile> skipped,
            IReadOnlyList<DeclarationWarning> warnings,
            IReadOnlyList<PouCoverage> coverage)
        {
            WriteSkipLines(skipped);
            WriteWarningLines(warnings);
            // The bare message, without the guidance the structured shapes
            // append: this line is read by a human at a console who has the
            // rest of the terminal for context.
            Output.WriteLine($"error: {message}");
            WriteCoverageLines(coverage);
        }

        public override void PluginsLoaded(IReadOnlyList<string> plugins)
        {
            foreach (var plugin in plugins)
                Output.WriteLine($"plugin: {plugin}");
        }

        protected override void WriteTest(TestReport test)
        {
            Output.WriteLine(test.Passed
                ? $"{test.Name}: PASS"
                : $"{test.Name}: FAIL ({string.Join("; ", test.Failures.Select(f => f.Detail))})");

            foreach (var failure in test.Failures)
                WriteCallStack(failure.CallStack);
        }

        protected override void WriteSuiteFailed(SuiteReport suite)
        {
            Output.WriteLine($"{suite.Name}: FAIL ({suite.Detail})");
            WriteCallStack(suite.CallStack);
        }

        protected override void WriteSummary(
            int exitCode,
            IReadOnlyList<SkippedFile> skipped,
            IReadOnlyList<DeclarationWarning> warnings,
            IReadOnlyList<PouCoverage> coverage)
        {
            WriteSkipLines(skipped);
            WriteWarningLines(warnings);
            Output.WriteLine(CountLine(skipped, warnings));
            WriteCoverageLines(coverage);
        }

        // Skips and warnings are counted in different units on purpose: a skip
        // costs a whole file, so it is counted in files lost, while a warning
        // leaves the file running and is counted in files affected. Each part
        // is omitted at zero, so an all-green run still ends on the same bare
        // "N passed, M failed" line it always did.
        private string CountLine(
            IReadOnlyList<SkippedFile> skipped, IReadOnlyList<DeclarationWarning> warnings)
        {
            var line = $"{PassCount} passed, {FailCount} failed";

            if (skipped.Count > 0)
                line += $", {skipped.Count} skipped";

            if (warnings.Count > 0)
                line += warnings.Count == 1
                    ? ", 1 file with warnings"
                    : $", {warnings.Count} files with warnings";

            return line;
        }

        // Frames print beneath the line they belong to, never instead of it: a
        // consumer that reads only the PASS/FAIL line must keep working. Null
        // for a failure that never entered an ST body.
        private void WriteCallStack(IReadOnlyList<CallStackFrameReport> frames)
        {
            if (frames == null)
                return;

            foreach (var frame in frames)
                Output.WriteLine($"    at {frame.LocationWithLine}");
        }

        private void WriteCoverageLines(IReadOnlyList<PouCoverage> coverage)
        {
            if (coverage == null)
                return;

            foreach (var entry in coverage)
                Output.WriteLine($"{entry.PouTypeName}  suites: {(entry.IsCovered ? string.Join(", ", entry.SuiteTypeNames) : "(none)")}");
        }

        private void WriteSkipLines(IReadOnlyList<SkippedFile> skipped)
        {
            foreach (var skip in skipped)
                Output.WriteLine($"skipped: {skip.FileKey} ({skip.Message})");
        }

        // The offending lines print beneath the file that lost them: the file
        // alone is not enough to act on, since the whole point of the report
        // is to show which declaration went missing.
        private void WriteWarningLines(IReadOnlyList<DeclarationWarning> warnings)
        {
            foreach (var warning in warnings)
            {
                var rejectedLines = new HashSet<string>(warning.Rejections.Select(r => r.Line));
                var unreadable = warning.Lines.Where(line => !rejectedLines.Contains(line)).ToList();

                if (unreadable.Count > 0)
                {
                    Output.WriteLine(
                        $"warning: {warning.FileKey} - {unreadable.Count} declaration "
                        + (unreadable.Count == 1 ? "line" : "lines") + " not understood");

                    foreach (var line in unreadable)
                        Output.WriteLine($"    {line}");
                }

                if (warning.Rejections.Count > 0)
                {
                    Output.WriteLine(
                        $"warning: {warning.FileKey} - FB_init arguments rejected on {warning.Rejections.Count} declaration "
                        + (warning.Rejections.Count == 1 ? "line" : "lines"));

                    foreach (var rejection in warning.Rejections)
                    {
                        Output.WriteLine($"    {rejection.Line}");
                        Output.WriteLine($"      {rejection.Reason}");
                    }
                }
            }
        }
    }

    // `--format json`: one blob at the end, so nothing is written until the run
    // has an exit code to report alongside it.
    internal sealed class JsonRunReportWriter : RunReportWriter
    {
        public JsonRunReportWriter(TextWriter output)
            : base(output)
        {
        }

        protected override void WriteError(
            string message,
            IReadOnlyList<SkippedFile> skipped,
            IReadOnlyList<DeclarationWarning> warnings,
            IReadOnlyList<PouCoverage> coverage) =>
            Output.WriteLine(RunReportJson.Blob(Reports.Error(message, skipped, warnings, coverage)));

        protected override void WriteSummary(
            int exitCode,
            IReadOnlyList<SkippedFile> skipped,
            IReadOnlyList<DeclarationWarning> warnings,
            IReadOnlyList<PouCoverage> coverage) =>
            Output.WriteLine(RunReportJson.Blob(
                Reports.Summary(Suites, PassCount, FailCount, exitCode, skipped, warnings, coverage)));
    }

    // `--stream`: one NDJSON event per line as the run proceeds. Every method
    // here writes exactly one line, and nothing else on this writer writes at
    // all, which is the whole of the one-object-per-line contract.
    internal sealed class NdjsonRunReportWriter : RunReportWriter
    {
        public NdjsonRunReportWriter(TextWriter output)
            : base(output)
        {
        }

        protected override void WriteError(
            string message,
            IReadOnlyList<SkippedFile> skipped,
            IReadOnlyList<DeclarationWarning> warnings,
            IReadOnlyList<PouCoverage> coverage) =>
            // Stands alone: this can fire before any discovery or suite event
            // has been emitted (a bad path, "no suites found").
            Output.WriteLine(RunReportJson.Line(
                Reports.Error(message, skipped, warnings, coverage, StreamEventNames.Error)));

        public override void Discovery(
            IReadOnlyList<string> suiteNames, IReadOnlyDictionary<string, string> filePathsByTypeName) =>
            Output.WriteLine(RunReportJson.Line(RunReportBuilder.Discovery(suiteNames, filePathsByTypeName)));

        public override void SuiteStart(string suiteName) =>
            Output.WriteLine(RunReportJson.Line(RunReportBuilder.SuiteStart(suiteName)));

        protected override void WriteSuiteCompleted(SuiteReport suite) =>
            Output.WriteLine(RunReportJson.Line(suite.AsStreamEvent(StreamEventNames.SuiteResult)));

        protected override void WriteSuiteFailed(SuiteReport suite) =>
            // A suite that never ran to completion still emits exactly one
            // suite-result line, so a --stream consumer's "waiting" list always
            // empties out.
            Output.WriteLine(RunReportJson.Line(suite.AsStreamEvent(StreamEventNames.SuiteResult)));

        protected override void WriteSummary(
            int exitCode,
            IReadOnlyList<SkippedFile> skipped,
            IReadOnlyList<DeclarationWarning> warnings,
            IReadOnlyList<PouCoverage> coverage) =>
            Output.WriteLine(RunReportJson.Line(Reports.Summary(
                Suites, PassCount, FailCount, exitCode, skipped, warnings, coverage, StreamEventNames.Summary)));
    }
}
