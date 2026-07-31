using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace xStunit.Vsix.TestRunner
{
    /// <summary>
    /// The "xstunit.json" sitting next to the open .plcproj/.sln (see
    /// ../xstunit.json.sample): which POU directories `xstunit` should scan, and
    /// where to find the executable. The CLI takes directory paths positionally and
    /// has no notion of a "project", so this file is the only place that mapping
    /// exists.
    /// </summary>
    internal sealed class XstunitConfig
    {
        // Case-insensitive so a casing drift between xstunit.json's keys and RawConfig's
        // fields cannot silently deserialize to null.
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        public List<string> Paths { get; set; } = new List<string>();

        public string CliPath { get; set; } = "xstunit";

        // Optional directory of plugin assemblies, forwarded to the CLI as
        // --plugins <dir>. Null when xstunit.json omits it.
        public string Plugins { get; set; }

        public static XstunitConfig Load(string directory)
        {
            var configPath = Path.Combine(directory, "xstunit.json");
            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException($"No xstunit.json found next to the project in '{directory}'.", configPath);
            }

            var json = File.ReadAllText(configPath);
            var raw = JsonSerializer.Deserialize<RawConfig>(json, SerializerOptions);

            if (raw.paths == null || raw.paths.Length == 0)
            {
                throw new InvalidOperationException($"xstunit.json at '{configPath}' must list at least one path in \"paths\".");
            }

            var cliPath = string.IsNullOrEmpty(raw.cliPath) ? "xstunit" : raw.cliPath;

            var plugins = raw.plugins;

            return new XstunitConfig
            {
                Paths = raw.paths.ToList(),
                CliPath = ResolveIfRelativePath(cliPath, directory),
                Plugins = ResolveIfRelativePath(plugins, directory),
            };
        }

        // A value containing a separator is a path and is resolved against xstunit.json's
        // own directory; a bare command name is left for PATH to resolve. This lets
        // xstunit.json point straight at a file inside the repo (a built xstunit binary,
        // a plugins folder) instead of depending on the IDE's working directory or on
        // PATH, which is unreliable from a long-running host process - an IDE launched
        // before PATH was updated for a newly installed tool never sees the change.
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

        // Field names deliberately mirror xstunit.json's own camelCase keys rather than
        // C# convention (SerializerOptions above makes the match case-insensitive anyway).
        private sealed class RawConfig
        {
            public string[] paths { get; set; }
            public string cliPath { get; set; }
            public string plugins { get; set; }
        }
    }
}
