using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace TcXunitResultsSpike.TestRunner
{
    /// <summary>
    /// THROWAWAY SPIKE. Shells out to `tcxunit run &lt;path&gt; --format json`
    /// (chosen over referencing TcXunit's libraries in-process - see the
    /// handoff doc's "Decisions made so far") and parses the resulting JSON.
    /// Proves out whether Process.Start works unrestricted from inside XAE Shell.
    /// </summary>
    internal sealed class TcxunitProcessRunner
    {
        public TcxunitRunResult Run(TcxunitConfig config, string workingDirectory)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = config.CliPath,
                Arguments = $"run \"{config.TestProjectPath}\" --format json",
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
                var result = serializer.Deserialize<TcxunitRunResult>(stdout.ToString());
                result.ExitCode = process.ExitCode;
                return result;
            }
        }
    }
}
