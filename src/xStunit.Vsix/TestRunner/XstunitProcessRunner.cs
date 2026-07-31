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
    /// <see cref="XstunitArgumentBuilder"/> builds an `xstunit ... --format json`
    /// command line, this class runs it and deserializes its stdout into
    /// <see cref="XstunitRunResult"/>. The extension holds no reference to any xStunit
    /// library and never hosts the interpreter itself.
    /// </summary>
    internal sealed class XstunitProcessRunner
    {
        // XstunitModels.cs's properties are PascalCase; the CLI's JSON is camelCase (see
        // XstunitModelsDeserializationTests.cs, which asserts against this same option set).
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        /// <summary>
        /// Runs one `xstunit` invocation to completion. Cancelling kills the in-flight
        /// process tree and ends the returned task in the canceled state, so the caller
        /// can tell "stopped on purpose" apart from "the run failed".
        /// </summary>
        /// <remarks>
        /// Null or empty <paramref name="suiteNames"/> runs every suite under
        /// config.Paths; a non-empty list restricts the run to those suites via a
        /// repeated --suite &lt;name&gt;, which is what the tool window's "rerun failed"
        /// is built on.
        /// </remarks>
        public async Task<XstunitRunResult> RunAsync(XstunitConfig config, string workingDirectory, CancellationToken cancellationToken, IReadOnlyList<string> suiteNames = null)
        {
            var startInfo = BuildStartInfo(config, workingDirectory, suiteNames);

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            using (var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true })
            {
                // Bridges Process.Exited to something awaitable: net472 predates
                // Process.WaitForExitAsync (.NET 5+). RunContinuationsAsynchronously keeps
                // the continuation off the thread raising the Exited event.
                var exitedTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                process.Exited += (s, e) => exitedTcs.TrySetResult(process.ExitCode);
                process.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

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

                if (stdout.Length == 0)
                {
                    return new XstunitRunResult
                    {
                        Error = $"xstunit produced no output (exit code {process.ExitCode}). stderr: {stderr}",
                        ExitCode = process.ExitCode,
                    };
                }

                var stdoutText = stdout.ToString();
                var result = JsonSerializer.Deserialize<XstunitRunResult>(stdoutText, SerializerOptions);
                result.ExitCode = process.ExitCode;
                // Kept verbatim so the WebView2 host can forward the CLI's own JSON
                // rather than re-serializing this object (see XstunitRunResult.RawJson).
                result.RawJson = stdoutText;
                return result;
            }
        }

        private static ProcessStartInfo BuildStartInfo(XstunitConfig config, string workingDirectory, IReadOnlyList<string> suiteNames)
        {
            var arguments = XstunitArgumentBuilder.BuildArguments(config.CliPath, config.Paths, suiteNames, config.Plugins);

            // Run via "cmd.exe /c" rather than invoking config.CliPath directly:
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
