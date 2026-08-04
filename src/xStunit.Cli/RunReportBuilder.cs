using System.Collections.Generic;
using System.Linq;
using xStunit.Interpreter;
using xStunit.Parser;
using xStunit.Runner;
using xStunit.Runner.TcUnitStub;

namespace xStunit.Cli
{
    // Turns the runner's own result types into the report objects that go on
    // the wire. Every construction of a *Report goes through here, so the
    // translation from "what happened" to "what a consumer reads" exists once
    // and can be exercised without running a suite or capturing stdout.
    internal sealed class RunReportBuilder
    {
        // What to APPEND to a factual message - fix the ST, or stop and
        // escalate - is a rule about failure KINDS, not about the shape of the
        // report, so it is handed in rather than owned here. The two forms
        // differ only in what the caller already has to hand: a failure object,
        // or a bare message from a path that has none.
        public delegate string GuidanceRule(string message, string kind, bool isVerbatim, int bodyLine);

        public delegate string FailureGuidanceRule(AssertionFailure failure);

        private readonly GuidanceRule _guidance;
        private readonly FailureGuidanceRule _failureGuidance;

        public RunReportBuilder(GuidanceRule guidance, FailureGuidanceRule failureGuidance)
        {
            _guidance = guidance;
            _failureGuidance = failureGuidance;
        }

        // Guidance is appended here, once, rather than at each of the caller's
        // write paths. Text output keeps the bare "error: <message>" line: that
        // one is read by a human at a console, this one by a consumer with
        // nothing else to go on.
        public ErrorReport Error(
            string message,
            IReadOnlyList<SkippedFile> skipped,
            IReadOnlyList<PouCoverage> coverage,
            string streamEvent = null) =>
            new ErrorReport(
                _guidance(message, FailureKind.LoadError, isVerbatim: false, PlcSourceLocationException.UnknownLine),
                ToSkipReports(skipped),
                ToCoverageReports(coverage),
                streamEvent);

        // The `--format json` blob and --stream's final line are the same
        // report; only the event discriminator tells them apart.
        public RunReport Summary(
            IReadOnlyList<SuiteReport> suites,
            int passed,
            int failed,
            int exitCode,
            IReadOnlyList<SkippedFile> skipped,
            IReadOnlyList<PouCoverage> coverage,
            string streamEvent = null) =>
            new RunReport(
                suites, passed, failed, exitCode, ToSkipReports(skipped), ToCoverageReports(coverage), streamEvent);

        public TestReport Test(TestCaseResult result) =>
            new TestReport(
                result.Name, result.Passed, result.Failures.Select(ToFailureReport).ToArray(), result.ElapsedMilliseconds);

        // A suite that ran to completion: no error, and so none of the fields
        // that only an error populates.
        public SuiteReport Suite(
            string name,
            string filePath,
            IReadOnlyList<TestReport> tests,
            long durationMs,
            string streamEvent = null,
            string outcome = null) =>
            new SuiteReport(name, filePath, null, null, null, tests, durationMs, null, null, streamEvent, outcome);

        // A suite that faulted. `located` is null unless an interpreted ST body
        // actually faulted - a load-level failure has no PLC location - and
        // every location-derived field stays null for that case.
        public SuiteReport SuiteError(
            string name,
            string filePath,
            string error,
            string kind,
            string construct,
            IReadOnlyList<TestReport> completedTests,
            PlcSourceLocationException located,
            string streamEvent = null,
            string outcome = null) =>
            new SuiteReport(
                name,
                filePath,
                error,
                kind,
                construct,
                completedTests,
                null,
                located != null ? NullableLine(located.Line) : null,
                located?.CallStack.Select(frame => ToCallStackFrameReport(frame.Site)).ToArray(),
                streamEvent,
                outcome);

        public static DiscoveryEvent Discovery(
            IReadOnlyList<string> suiteNames, IReadOnlyDictionary<string, string> filePathsByTypeName)
        {
            var suites = suiteNames.Select(name =>
            {
                filePathsByTypeName.TryGetValue(name, out var filePath);
                return new SuiteDiscoveryEntry(name, filePath);
            }).ToList();
            return new DiscoveryEvent(suites);
        }

        public static SuiteStartEvent SuiteStart(string suiteName) => new SuiteStartEvent(suiteName);

        // Reads Construct and Site.BodyLine straight through: the engine
        // populates both from the ParseException's own structured fields, so
        // nothing here recovers them by re-parsing a message.
        private FailureReport ToFailureReport(AssertionFailure failure) =>
            new FailureReport(
                _failureGuidance(failure),
                failure.Kind,
                failure.Construct,
                failure.Assert,
                failure.Expected,
                failure.Actual,
                failure.AssertMessage,
                failure.Site.PouTypeName,
                failure.Site.MethodName,
                NullableLine(failure.Site.BodyLine),
                NullableLine(failure.Site.Line),
                failure.CallStack?.Select(ToCallStackFrameReport).ToArray());

        private static CallStackFrameReport ToCallStackFrameReport(AssertSite site) =>
            new CallStackFrameReport(site.PouTypeName, site.MethodName, NullableLine(site.Line), NullableLine(site.BodyLine));

        // The one place the UnknownLine sentinel becomes a JSON null, so every
        // line field on the wire uses null - never 0 - for "not known".
        private static int? NullableLine(int line) =>
            line != PlcSourceLocationException.UnknownLine ? (int?)line : null;

        private static IReadOnlyList<CoverageReport> ToCoverageReports(IReadOnlyList<PouCoverage> coverage) =>
            coverage?.Select(c => new CoverageReport(c.PouTypeName, c.SuiteTypeNames)).ToList();

        private static IReadOnlyList<SkipReport> ToSkipReports(IReadOnlyList<SkippedFile> skipped) =>
            skipped.Select(s => new SkipReport(s.FileKey, s.Message)).ToList();
    }
}
