using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace TcXunit.Vsix.TestRunner
{
    /// <summary>
    /// Shells out to `tcxunit &lt;path-a&gt; [&lt;path-b&gt; ...] --format json`
    /// (chosen over referencing TcXunit's libraries in-process - see
    /// TcXunit-6nt) and parses the resulting JSON.
    /// Proves out whether Process.Start works unrestricted from inside XAE Shell.
    /// </summary>
    internal sealed class TcxunitProcessRunner
    {
        /// <summary>
        /// Async, cancellable version of the (former) synchronous Run(...) --
        /// added by TcXunit-1tt.3 so ResultsToolWindowControl's UI thread isn't
        /// blocked for the duration of a run (WaitForExit() used to run right on
        /// the WPF Button_Click handler) and so a Stop click has something to
        /// cancel. Cancelling kills the in-flight tcxunit process tree and the
        /// returned Task ends in the canceled state (OperationCanceledException),
        /// which the caller distinguishes from a genuine run failure -- "stopped
        /// on purpose" vs. "errored".
        /// </summary>
        public async Task<TcxunitRunResult> RunAsync(TcxunitConfig config, string workingDirectory, CancellationToken cancellationToken)
        {
            var startInfo = BuildStartInfo(config, workingDirectory);

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            using (var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true })
            {
                // TaskCompletionSource bridges Process.Exited (an event) to something
                // awaitable -- net472 (this project's TFM) predates
                // Process.WaitForExitAsync (added in .NET 5). RunContinuationsAsynchronously
                // keeps the Exited event's raising thread from being blocked running our
                // continuation synchronously.
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

                // Cancellation races the process's own natural exit (it may finish a
                // moment before Stop's kill lands) -- check the token explicitly
                // rather than inferring "was it killed" from the exit code, which
                // varies by how it died.
                cancellationToken.ThrowIfCancellationRequested();

                if (stdout.Length == 0)
                {
                    return new TcxunitRunResult
                    {
                        Error = $"tcxunit produced no output (exit code {process.ExitCode}). stderr: {stderr}",
                        ExitCode = process.ExitCode,
                    };
                }

                var serializer = new JavaScriptSerializer();
                var stdoutText = stdout.ToString();
                var result = serializer.Deserialize<TcxunitRunResult>(stdoutText);
                result.ExitCode = process.ExitCode;
                // Keep the CLI's own JSON text around (see TcxunitRunResult.RawJson)
                // so the WebView2 host can forward it verbatim rather than
                // re-serializing this object -- see TcXunit-1tt.2.
                result.RawJson = stdoutText;
                return result;
            }
        }

        private static ProcessStartInfo BuildStartInfo(TcxunitConfig config, string workingDirectory)
        {
            var arguments = new StringBuilder();
            arguments.Append(EscapeArgument(config.CliPath)).Append(' ');
            foreach (var path in config.Paths)
            {
                arguments.Append(EscapeArgument(path)).Append(' ');
            }
            arguments.Append("--format json");

            // Run via "cmd.exe /c" rather than invoking config.CliPath directly.
            // Process.Start with UseShellExecute=false calls CreateProcess directly,
            // which does NOT do the PATHEXT-based resolution (.exe/.cmd/.bat) or PATH
            // search that a real shell does - so a PATH-based launcher/shim (e.g. a
            // dotnet global tool's .cmd shim) that runs fine from a terminal can throw
            // "The system cannot find the file specified" here even though it's on
            // PATH. cmd.exe /c replicates the normal shell resolution behavior.
            // cmd.exe's "/c" parsing only strips the first and last quote character
            // of the whole command line (rather than respecting each argument's own
            // quoting), so a command line with multiple quoted tokens - e.g.
            // "...\tcxunit.exe" "path with spaces" --format json - gets mangled into
            // a single bogus command (`tcxunit.exe" "path...` is not recognized).
            // Wrapping the entire thing in one extra pair of quotes makes cmd strip
            // only that outer pair, leaving the inner per-argument quoting intact.
            return new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c \"" + arguments.ToString().TrimEnd() + "\"",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
        }

        // Process.Kill() on net472 (no Kill(entireProcessTree: true) overload -- that's
        // .NET 5+) only terminates the exact process we started, which is cmd.exe, not
        // the tcxunit.exe it launched as a child ("cmd.exe /c ..." per BuildStartInfo
        // above) -- a plain Kill() here would leave the actual tcxunit run still
        // executing in the background after Stop supposedly stopped it. taskkill /T
        // recurses the whole process tree rooted at cmd.exe's PID, which is what
        // Stop's acceptance criteria ("kills the running tcxunit child process")
        // actually requires. Best-effort: if taskkill itself can't run (missing from
        // PATH, process already gone), fall back to a plain Kill so at least the
        // direct child is reaped rather than throwing out of a CancellationToken
        // callback.
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
                    // Nothing more we can do -- the CancellationToken.Register callback
                    // must not throw, and exitedTcs.Task will still complete once the
                    // process exits on its own (or never, which Stop can't fully
                    // guarantee against on a process that ignores termination signals).
                }
            }
        }

        // Win32/CommandLineToArgvW argument-quoting algorithm (the same one .NET Core's
        // ProcessStartInfo.ArgumentList uses internally - not available on net472's
        // Process, so it's reimplemented here). Naively wrapping a path in quotes breaks
        // as soon as the path itself ends in a backslash (e.g. "C:\Foo\") because that
        // backslash then escapes the closing quote instead of being a path separator; a
        // path containing a literal '"' would break unquoted concatenation too. This
        // always quotes and doubles any run of backslashes that's immediately followed by
        // a quote (embedded or closing).
        private static string EscapeArgument(string argument)
        {
            var result = new StringBuilder();
            result.Append('"');

            var backslashCount = 0;
            foreach (var c in argument)
            {
                if (c == '\\')
                {
                    backslashCount++;
                    continue;
                }

                if (c == '"')
                {
                    // Backslashes immediately before a quote must be doubled, plus one
                    // more to escape the quote itself.
                    result.Append('\\', backslashCount * 2 + 1);
                    result.Append('"');
                    backslashCount = 0;
                    continue;
                }

                if (backslashCount > 0)
                {
                    result.Append('\\', backslashCount);
                    backslashCount = 0;
                }

                result.Append(c);
            }

            // Backslashes immediately before the closing quote must be doubled so they
            // aren't read as escaping it.
            result.Append('\\', backslashCount * 2);
            result.Append('"');
            return result.ToString();
        }
    }
}
