using System.Threading.Tasks;
using xStunit.Vsix.TestRunner;
using Xunit;

namespace xStunit.Vsix.Tests
{
    // The branch that chooses between showing a streamed run and running the whole suite
    // set again lives here rather than in XstunitProcessRunner, which starts processes
    // and so is not source-linked into this project. Without these tests the retry -- the
    // most expensive thing the extension can do to a user -- has no coverage at all.
    public class XstunitRunResolverTests
    {
        [Fact]
        public async Task ResolveAsync_StreamThatReachedItsSummary_ReturnsThatRunWithoutRetrying()
        {
            var events = StreamOf(
                @"{""event"":""discovery"",""suites"":[{""name"":""FB_A"",""filePath"":null}]}",
                @"{""event"":""summary"",""suites"":[],""passed"":2,""failed"":0,""exitCode"":0,""skipped"":[]}");
            var retried = false;

            var result = await XstunitRunResolver.ResolveAsync(events, 0, string.Empty, () =>
            {
                retried = true;
                return Task.FromResult(new XstunitRunResult());
            });

            Assert.False(retried);
            Assert.Same(events.Result, result);
            Assert.Equal(2, result.Passed);
        }

        // Two exit codes describe one run: the one the CLI serialized into its summary
        // and the one the process actually returned. They agree in a normal run, and the
        // process is the authority when they don't - it knows how the run ended, the
        // summary was written before that.
        [Fact]
        public async Task ResolveAsync_StreamThatReachedItsSummary_ReportsTheProcessExitCode()
        {
            var events = StreamOf(
                @"{""event"":""summary"",""suites"":[],""passed"":0,""failed"":0,""exitCode"":0,""skipped"":[]}");

            var result = await XstunitRunResolver.ResolveAsync(events, 3, string.Empty, NeverRetried);

            Assert.Equal(3, result.ExitCode);
        }

        // A CLI too old to know --stream reads the flag as a directory name and prints one
        // plain-text error. That is the only thing worth a second run: the first one never
        // executed a single test.
        [Fact]
        public async Task ResolveAsync_CliThatDoesNotSpeakStreaming_RetriesBufferedAndReturnsThatResult()
        {
            var events = StreamOf("error: path does not exist: --stream");
            var buffered = new XstunitRunResult { Passed = 4, ExitCode = 0 };

            var result = await XstunitRunResolver.ResolveAsync(events, 2, string.Empty, () => Task.FromResult(buffered));

            Assert.Same(buffered, result);
        }

        // A stream that emitted events and then died is a failed run, and shown as one.
        // Retrying it under --format json would execute the user's suites a second time -
        // every timer and convergence loop, and any side effect with them - to re-learn
        // that this CLI streams perfectly well.
        [Fact]
        public async Task ResolveAsync_StreamThatStoppedAfterItsEvents_ReportsAFailedRunWithoutRetrying()
        {
            var events = StreamOf(
                @"{""event"":""discovery"",""suites"":[{""name"":""FB_A"",""filePath"":null}]}",
                @"{""event"":""suite-start"",""suite"":""FB_A""}");

            var result = await XstunitRunResolver.ResolveAsync(events, -1, "Unhandled exception.", NeverRetried);

            Assert.Equal(-1, result.ExitCode);
            Assert.Contains("Unhandled exception.", result.Error);
            Assert.Null(result.Suites);
        }

        // An invocation that wrote nothing at all is broken rather than old - a missing
        // executable, say - and re-running it would only fail a second time.
        [Fact]
        public async Task ResolveAsync_InvocationThatWroteNothing_ReportsTheStderrWithoutRetrying()
        {
            var result = await XstunitRunResolver.ResolveAsync(
                new XstunitEventStream(), 9009, "'xstunit' is not recognized", NeverRetried);

            Assert.Equal(9009, result.ExitCode);
            Assert.Contains("'xstunit' is not recognized", result.Error);
        }

        private static XstunitEventStream StreamOf(params string[] lines)
        {
            var events = new XstunitEventStream();
            foreach (var line in lines)
            {
                events.Append(line);
            }

            return events;
        }

        private static Task<XstunitRunResult> NeverRetried()
        {
            Assert.Fail("The buffered command line was run, re-executing every suite.");
            return null;
        }
    }
}
