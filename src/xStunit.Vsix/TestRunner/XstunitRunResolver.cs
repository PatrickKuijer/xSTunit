using System;
using System.Threading.Tasks;

namespace xStunit.Vsix.TestRunner
{
    /// <summary>
    /// Turns one finished `xstunit --stream` attempt into the run the extension shows,
    /// deciding on the way whether the pre-stream `--format json` command line has to be
    /// run in its place.
    /// </summary>
    /// <remarks>
    /// Kept out of <see cref="XstunitProcessRunner"/>, which starts processes and so is
    /// not source-linked into tests/xStunit.Vsix.Tests: this decision costs a second full
    /// execution of the user's suites when it goes the wrong way - every timer and
    /// convergence loop, and any side effect with them - which is too expensive to leave
    /// untested.
    /// </remarks>
    internal static class XstunitRunResolver
    {
        /// <param name="events">The fold of that attempt's stdout.</param>
        /// <param name="exitCode">What the streaming process exited with.</param>
        /// <param name="standardError">Its stderr, verbatim, for the error text below.</param>
        /// <param name="retryBuffered">
        /// Runs the pre-stream `--format json` command line. Invoked only for a CLI that
        /// turned out not to speak --stream at all, never to fill in a run that streamed
        /// and then stopped.
        /// </param>
        /// <returns>The finished run, whichever attempt produced it.</returns>
        public static async Task<XstunitRunResult> ResolveAsync(
            XstunitEventStream events,
            int exitCode,
            string standardError,
            Func<Task<XstunitRunResult>> retryBuffered)
        {
            if (events.Result != null)
            {
                // The process is the authority on how the run ended: the summary's own
                // exitCode was serialized before the CLI could exit.
                events.Result.ExitCode = exitCode;
                return events.Result;
            }

            // Output that the fold made nothing of means a CLI predating --stream: it
            // read the flag as a directory name and complained about that. Re-running the
            // pre-stream command line beats surfacing a complaint about an argument the
            // user never wrote - and costs nothing twice, because that run executed no
            // tests at all.
            if (events.NeedsJsonFallback)
            {
                return await retryBuffered().ConfigureAwait(false);
            }

            if (events.EndedMidStream)
            {
                return Failed("stopped before reporting a result", exitCode, standardError);
            }

            return NoOutput(exitCode, standardError);
        }

        /// <summary>
        /// The run to show for an invocation that wrote nothing at all - a missing
        /// executable, say - which is a broken invocation rather than a failed run.
        /// </summary>
        /// <param name="exitCode">What the process exited with.</param>
        /// <param name="standardError">Its stderr, carried into the error text.</param>
        public static XstunitRunResult NoOutput(int exitCode, string standardError) =>
            Failed("produced no output", exitCode, standardError);

        private static XstunitRunResult Failed(string what, int exitCode, string standardError) =>
            new XstunitRunResult
            {
                Error = $"xstunit {what} (exit code {exitCode}). stderr: {standardError}",
                ExitCode = exitCode,
            };
    }
}
