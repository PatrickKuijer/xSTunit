using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace xStunit.Cli.Tests
{
    // A narrow STRING is Latin-1, so a character above U+00FF is one no
    // TwinCAT STRING can hold without the TcEncoding pragma nothing here
    // parses. The plugin functions used to count it as one character and the
    // wire format used to keep its low byte, so a run reported a confident
    // wrong answer; it must now surface as a named, diagnosable failure
    // instead - through the real plugin loader, since the narrow measure and
    // the engine share one definition of a byte across that boundary.
    public class CliRunnerNarrowStringEncodingTests : IDisposable
    {
        private readonly string _pluginDir;

        public CliRunnerNarrowStringEncodingTests()
        {
            _pluginDir = Path.Combine(Path.GetTempPath(), "xstunit-narrow-encoding-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_pluginDir);
            File.Copy(PluginDll(), Path.Combine(_pluginDir, "xStunit.StandardStringPlugins.dll"));
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_pluginDir, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        [Fact]
        public void Run_NarrowStringFunctionOverCharacterAboveLatin1_FailsNamingTheCharacter()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { FixtureDir(), "--plugins", _pluginDir }, output);

            var text = output.ToString();
            Assert.Equal(1, exitCode);
            Assert.Contains("LenOfCharacterAboveLatin1: FAIL", text);
            Assert.Contains("U+20AC", text);
            Assert.Contains("Latin-1", text);
        }

        // Classified as a gap in this interpreter, not a defect in the POU: the
        // opposite verdict tells a reader to edit ST that TwinCAT may well
        // compile, which is the confusion the failure vocabulary exists for.
        [Fact]
        public void Run_NarrowStringFunctionOverCharacterAboveLatin1_ReportsAnUnsupportedConstruct()
        {
            var output = new StringWriter();

            CliRunner.Run(new[] { FixtureDir(), "--plugins", _pluginDir, "--format", "json" }, output);

            Assert.Contains("unsupported-construct", output.ToString());
        }

        private static string FixtureDir([CallerFilePath] string callerFile = "") =>
            Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(callerFile)!, "..", "Fixtures", "NarrowStringEncodingFixture"));

        private static string PluginDll([CallerFilePath] string callerFile = "")
        {
            var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerFile)!, "..", ".."));
            var binRoot = Path.Combine(repoRoot, "samples", "xStunit.StandardStringPlugins", "bin");

            if (Directory.Exists(binRoot))
            {
                var found = Directory.GetFiles(binRoot, "xStunit.StandardStringPlugins.dll", SearchOption.AllDirectories);
                if (found.Length > 0)
                {
                    Array.Sort(found, (a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
                    return found[0];
                }
            }

            throw new InvalidOperationException(
                $"StandardStringPlugins not built - expected xStunit.StandardStringPlugins.dll under {binRoot}. " +
                "Build samples/xStunit.StandardStringPlugins (the Cli.Tests project reference should do this automatically).");
        }
    }
}
