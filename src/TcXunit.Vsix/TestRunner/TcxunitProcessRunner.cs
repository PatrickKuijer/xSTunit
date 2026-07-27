using System;
using System.Diagnostics;
using System.IO;
using System.Text;
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
        public TcxunitRunResult Run(TcxunitConfig config, string workingDirectory)
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
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c \"" + arguments.ToString().TrimEnd() + "\"",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            using (var process = new Process { StartInfo = startInfo })
            {
                process.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

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
