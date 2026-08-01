using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace xStunit.Vsix.TestRunner
{
    /// <summary>
    /// The extension's whole test-execution path, and it is entirely out of process:
    /// <see cref="XstunitArgumentBuilder"/> builds an `xstunit ... --stream` command
    /// line, this class runs it and folds its NDJSON stdout into
    /// <see cref="XstunitRunResult"/> via <see cref="XstunitEventStream"/>. The
    /// extension holds no reference to any xStunit library and never hosts the
    /// interpreter itself.
    /// </summary>
    internal sealed class XstunitProcessRunner
    {
        /// <summary>
        /// Runs one `xstunit` invocation to completion. Cancelling kills the in-flight
        /// process tree and ends the returned task in the canceled state, so the caller
        /// can tell "stopped on purpose" apart from "the run failed".
        /// </summary>
        /// <param name="config">Where the executable is and which POU directories to scan.</param>
        /// <param name="workingDirectory">Directory the CLI process is started in.</param>
        /// <param name="cancellationToken">Cancelling kills the process tree; see above.</param>
        /// <param name="suiteNames">
        /// Null or empty runs every suite under config.Paths; a non-empty list restricts
        /// the run to those suites via a repeated --suite &lt;name&gt;, which is what the
        /// tool window's "rerun failed" is built on.
        /// </param>
        /// <param name="onEvent">
        /// Called once per NDJSON event as it arrives, on a thread pool thread, so a
        /// caller wanting live progress marshals it itself. Null asks for the completed
        /// result only.
        /// </param>
        /// <returns>
        /// The finished run - the same shape whether it came from the stream's summary
        /// line or from the buffered `--format json` retry
        /// <see cref="XstunitRunResolver"/> may ask for.
        /// </returns>
        public async Task<XstunitRunResult> RunAsync(
            XstunitConfig config,
            string workingDirectory,
            CancellationToken cancellationToken,
            IReadOnlyList<string> suiteNames = null,
            Action<IXstunitStreamEvent> onEvent = null)
        {
            var events = new XstunitEventStream();
            var stderr = new StringBuilder();

            var exitCode = await RunToExitAsync(
                BuildStartInfo(XstunitArgumentBuilder.BuildStreamingArguments(config.CliPath, config.Paths, suiteNames, config.Plugins), workingDirectory),
                cancellationToken,
                line =>
                {
                    var streamed = events.Append(line);
                    if (streamed != null)
                    {
                        onEvent?.Invoke(streamed);
                    }
                },
                line => stderr.AppendLine(line)).ConfigureAwait(false);

            return await XstunitRunResolver.ResolveAsync(
                events,
                exitCode,
                stderr.ToString(),
                () => RunBufferedAsync(config, workingDirectory, cancellationToken, suiteNames)).ConfigureAwait(false);
        }

        // The pre-stream path, unchanged: one blob on stdout, deserialized once the
        // process has exited.
        private static async Task<XstunitRunResult> RunBufferedAsync(XstunitConfig config, string workingDirectory, CancellationToken cancellationToken, IReadOnlyList<string> suiteNames)
        {
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            var exitCode = await RunToExitAsync(
                BuildStartInfo(XstunitArgumentBuilder.BuildJsonArguments(config.CliPath, config.Paths, suiteNames, config.Plugins), workingDirectory),
                cancellationToken,
                line => stdout.AppendLine(line),
                line => stderr.AppendLine(line)).ConfigureAwait(false);

            if (stdout.Length == 0)
            {
                return XstunitRunResolver.NoOutput(exitCode, stderr.ToString());
            }

            var stdoutText = stdout.ToString();
            var result = JsonSerializer.Deserialize<XstunitRunResult>(stdoutText, XstunitEventStream.SerializerOptions);
            result.ExitCode = exitCode;
            // Kept verbatim so the WebView2 host can forward the CLI's own JSON
            // rather than re-serializing this object (see XstunitRunResult.RawJson).
            result.RawJson = stdoutText;
            return result;
        }

        /// <param name="onOutputLine">Called per stdout line as it arrives, on a thread pool thread.</param>
        /// <param name="onErrorLine">Called per stderr line, same threading.</param>
        /// <param name="startInfo">Already-built command line; see BuildStartInfo.</param>
        /// <param name="cancellationToken">Cancelling kills the process tree and throws.</param>
        /// <returns>The process's exit code.</returns>
        /// <exception cref="OperationCanceledException">
        /// The run was cancelled - as opposed to any nonzero exit code, which is a
        /// completed run.
        /// </exception>
        private static async Task<int> RunToExitAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken, Action<string> onOutputLine, Action<string> onErrorLine)
        {
            using (var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true })
            {
                // Bridges Process.Exited to something awaitable: net472 predates
                // Process.WaitForExitAsync (.NET 5+). RunContinuationsAsynchronously keeps
                // the continuation off the thread raising the Exited event.
                var exitedTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                process.Exited += (s, e) => exitedTcs.TrySetResult(process.ExitCode);
                process.OutputDataReceived += (s, e) => { if (e.Data != null) onOutputLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) onErrorLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using (cancellationToken.Register(() => KillProcessTree(process)))
                {
                    await exitedTcs.Task.ConfigureAwait(false);
                }

                // Cancellation races the process's own natural exit - it may finish a
                // moment before Stop's kill lands - so ask the token rather than infer
                // "was it killed" from an exit code that varies by how it died.
                cancellationToken.ThrowIfCancellationRequested();

                return process.ExitCode;
            }
        }

        private static ProcessStartInfo BuildStartInfo(string arguments, string workingDirectory)
        {
            // Run via "cmd.exe /c" rather than invoking the configured CliPath directly:
            // Process.Start with UseShellExecute=false calls CreateProcess, which does
            // NOT do the PATHEXT (.exe/.cmd/.bat) or PATH resolution a real shell does,
            // so a PATH-based launcher or shim (a dotnet global tool's .cmd, say) that
            // runs fine from a terminal throws "The system cannot find the file
            // specified" here despite being on PATH.
            //
            // cmd's "/c" parsing then strips only the first and last quote of the whole
            // command line instead of respecting each argument's own quoting, mangling a
            // line with several quoted tokens into one bogus command. The extra outer
            // pair of quotes below is what cmd consumes, leaving the per-argument
            // quoting intact.
            return new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c \"" + arguments.TrimEnd() + "\"",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
        }

        // net472's Process.Kill() has no entireProcessTree overload (.NET 5+) and so
        // terminates only the process we started - which is cmd.exe, not the xstunit.exe
        // it launched - leaving the actual run executing in the background after Stop
        // supposedly stopped it. taskkill /T recurses the whole tree instead.
        // Best-effort: if taskkill itself can't run, fall back to a plain Kill so at
        // least the direct child is reaped rather than throwing out of a
        // CancellationToken callback.
        private static void KillProcessTree(Process process)
        {
            try
            {
                if (process.HasExited)
                {
                    return;
                }

                using (var killer = Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = "/PID " + process.Id + " /T /F",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }))
                {
                    killer?.WaitForExit(5000);
                }
            }
            catch (Exception)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }
                catch (Exception)
                {
                    // A CancellationToken.Register callback must not throw. exitedTcs
                    // still completes if the process ever exits on its own - which Stop
                    // cannot guarantee against a process that ignores termination.
                }
            }
        }
    }
}
