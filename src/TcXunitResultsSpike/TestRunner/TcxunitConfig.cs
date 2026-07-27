using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace TcXunitResultsSpike.TestRunner
{
    /// <summary>
    /// THROWAWAY SPIKE. Reads the "tcxunit.json" config placed next to the
    /// open .plcproj/.sln (see ../../tcxunit.json.sample). Schema is a first
    /// guess, not finalized - see README.md "Not done here". Its only job is
    /// to configure which POU directories `tcxunit` should scan for this
    /// project (TcXunit.Cli.CliRunner takes N directory paths positionally,
    /// there is no "project" concept on the CLI side).
    /// </summary>
    internal sealed class TcxunitConfig
    {
        public List<string> Paths { get; set; } = new List<string>();

        public string CliPath { get; set; } = "tcxunit";

        public static TcxunitConfig Load(string directory)
        {
            var configPath = Path.Combine(directory, "tcxunit.json");
            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException($"No tcxunit.json found next to the project in '{directory}'.", configPath);
            }

            var json = File.ReadAllText(configPath);
            var serializer = new JavaScriptSerializer();
            var raw = serializer.Deserialize<RawConfig>(json);

            if (raw.paths == null || raw.paths.Length == 0)
            {
                throw new InvalidOperationException($"tcxunit.json at '{configPath}' must list at least one path in \"paths\".");
            }

            var cliPath = string.IsNullOrEmpty(raw.cliPath) ? "tcxunit" : raw.cliPath;

            // If cliPath looks like a path (relative or rooted) rather than a bare
            // command name meant to be resolved via PATH, resolve it relative to the
            // directory containing tcxunit.json. This lets tcxunit.json point directly
            // at a built binary living inside a repo instead of depending on PATH
            // resolution, which is unreliable from a long-running host process (e.g. an
            // IDE launched before PATH was updated for a newly installed tool).
            if (cliPath.IndexOfAny(new[] { '\\', '/' }) >= 0 && !Path.IsPathRooted(cliPath))
            {
                cliPath = Path.GetFullPath(Path.Combine(directory, cliPath));
            }

            return new TcxunitConfig
            {
                Paths = raw.paths.ToList(),
                CliPath = cliPath,
            };
        }

        // Field names match tcxunit.json's camelCase keys for JavaScriptSerializer's default binding.
        private sealed class RawConfig
        {
            public string[] paths { get; set; }
            public string cliPath { get; set; }
        }
    }
}
