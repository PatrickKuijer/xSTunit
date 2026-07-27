using System;
using System.IO;
using System.Web.Script.Serialization;

namespace TcXunitResultsSpike.TestRunner
{
    /// <summary>
    /// THROWAWAY SPIKE. Reads the "tcxunit.json" config placed next to the
    /// open .plcproj/.sln (see ../../tcxunit.json.sample). Schema is a first
    /// guess, not finalized - see README.md "Not done here".
    /// </summary>
    internal sealed class TcxunitConfig
    {
        public string TestProjectPath { get; set; }

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

            return new TcxunitConfig
            {
                TestProjectPath = raw.testProjectPath,
                CliPath = string.IsNullOrEmpty(raw.cliPath) ? "tcxunit" : raw.cliPath,
            };
        }

        // Field names match tcxunit.json's camelCase keys for JavaScriptSerializer's default binding.
        private sealed class RawConfig
        {
            public string testProjectPath { get; set; }
            public string cliPath { get; set; }
        }
    }
}
