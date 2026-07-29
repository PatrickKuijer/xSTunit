using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TcXunit.Vsix.TestRunner
{
    /// <summary>
    /// Reads the "tcxunit.json" config placed next to the
    /// open .plcproj/.sln (see ../tcxunit.json.sample). Schema is a first
    /// guess, not finalized - see README.md. Its only job is
    /// to configure which POU directories `tcxunit` should scan for this
    /// project (TcXunit.Cli.CliRunner takes N directory paths positionally,
    /// there is no "project" concept on the CLI side).
    /// </summary>
    internal sealed class TcxunitConfig
    {
        // Matches JavaScriptSerializer's default case-insensitive member binding, which this
        // class's deserialization relied on before the System.Text.Json swap (see
        // TcXunit-cmp) -- tcxunit.json's actual keys are already lowercase/camelCase and match
        // RawConfig's fields exactly, but this keeps the contract from being case-sensitive by
        // accident if either side's casing ever drifts.
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        public List<string> Paths { get; set; } = new List<string>();

        public string CliPath { get; set; } = "tcxunit";

        public string Plugins { get; set; }

        public static TcxunitConfig Load(string directory)
        {
            var configPath = Path.Combine(directory, "tcxunit.json");
            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException($"No tcxunit.json found next to the project in '{directory}'.", configPath);
            }

            var json = File.ReadAllText(configPath);
            var raw = JsonSerializer.Deserialize<RawConfig>(json, SerializerOptions);

            if (raw.paths == null || raw.paths.Length == 0)
            {
                throw new InvalidOperationException($"tcxunit.json at '{configPath}' must list at least one path in \"paths\".");
            }

            var cliPath = string.IsNullOrEmpty(raw.cliPath) ? "tcxunit" : raw.cliPath;

            // plugins (TcXunit-qhc): optional directory of ITcXunitNativeFunction plugin
            // assemblies, forwarded to the CLI as --plugins <dir> (TcXunit-6k2).
            var plugins = raw.plugins;

            return new TcxunitConfig
            {
                Paths = raw.paths.ToList(),
                CliPath = ResolveIfRelativePath(cliPath, directory),
                Plugins = ResolveIfRelativePath(plugins, directory),
            };
        }

        // If value looks like a path (relative or rooted) rather than a bare command
        // name meant to be resolved via PATH, resolve it relative to the directory
        // containing tcxunit.json. This lets tcxunit.json point directly at a file
        // inside a repo (a built tcxunit binary, a plugins folder) instead of depending
        // on the IDE's working directory or PATH resolution, which is unreliable from a
        // long-running host process (e.g. an IDE launched before PATH was updated for a
        // newly installed tool).
        private static string ResolveIfRelativePath(string value, string directory)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            if (value.IndexOfAny(new[] { '\\', '/' }) >= 0 && !Path.IsPathRooted(value))
            {
                return Path.GetFullPath(Path.Combine(directory, value));
            }

            return value;
        }

        // Field names match tcxunit.json's camelCase keys (SerializerOptions above makes the
        // match case-insensitive regardless).
        private sealed class RawConfig
        {
            public string[] paths { get; set; }
            public string cliPath { get; set; }
            public string plugins { get; set; }
        }
    }
}
