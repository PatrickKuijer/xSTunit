using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace xStunit.Cli.Tests
{
    // Tc2_System is the manufacturer-specific half of the TwinCAT base install
    // and is compiled-only like every other TwinCAT library, so its functions
    // and blocks resolve through the plugin path and never as an intrinsic.
    // Loads the real DLL through the CLI's --plugins loader rather than
    // registering in process, so the reflection and load-context half of the
    // contract is covered too.
    public class CliRunnerSystemLibraryPluginTests : IDisposable
    {
        private readonly string _pluginDir;

        public CliRunnerSystemLibraryPluginTests()
        {
            _pluginDir = Path.Combine(Path.GetTempPath(), "xstunit-system-plugin-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_pluginDir);
            File.Copy(PluginDll(), Path.Combine(_pluginDir, "xStunit.SystemLibraryPlugins.dll"));
        }

        public void Dispose()
        {
            // The plugin assembly stays loaded for the life of the process, so
            // on Windows the file may still be locked here.
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
        public void Run_WithPluginDirectory_ResolvesTheSystemLibrarySymbols()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { FixtureDir(), "--plugins", _pluginDir }, output);

            var text = output.ToString();
            Assert.Equal(0, exitCode);
            // The count, not just "0 failed": a fixture that stopped being
            // discovered would pass a zero-failure assertion while testing
            // nothing at all.
            Assert.Contains("36 passed, 0 failed", text);
        }

        [Fact]
        public void Run_WithoutPlugins_FailsNamingTheMissingLibraryFunction()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { FixtureDir() }, output);

            var text = output.ToString();
            Assert.Equal(1, exitCode);
            Assert.DoesNotContain("Object reference not set", text);
            Assert.Contains("F_CreateAmsNetId", text);
        }

        [Fact]
        public void Run_TimeFixture_ReadsTheSimulatedClockFromItsOrigin()
        {
            // A separate fixture directory, and a separate run, on purpose: the
            // simulated clock is shared by every suite in a run and there is no
            // ST way to set it back, so the absolute-timestamp assertions in
            // this suite need a run nothing else has advanced the clock in.
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { TimeFixtureDir(), "--plugins", _pluginDir }, output);

            Assert.Equal(0, exitCode);
            Assert.Contains("3 passed, 0 failed", output.ToString());
        }

        private static string FixtureDir([CallerFilePath] string callerFile = "") =>
            Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(callerFile)!, "..", "Fixtures", "SystemLibraryPluginFixture"));

        private static string TimeFixtureDir([CallerFilePath] string callerFile = "") =>
            Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(callerFile)!, "..", "Fixtures", "SystemLibraryTimeFixture"));

        // Searched for rather than named: neither the configuration nor the
        // target framework of the sample's own build output is knowable here.
        private static string PluginDll([CallerFilePath] string callerFile = "")
        {
            var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerFile)!, "..", ".."));
            var binRoot = Path.Combine(repoRoot, "samples", "xStunit.SystemLibraryPlugins", "bin");

            if (Directory.Exists(binRoot))
            {
                var found = Directory.GetFiles(binRoot, "xStunit.SystemLibraryPlugins.dll", SearchOption.AllDirectories);
                if (found.Length > 0)
                {
                    Array.Sort(found, (a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
                    return found[0];
                }
            }

            throw new InvalidOperationException(
                $"system-library plugin not built - expected xStunit.SystemLibraryPlugins.dll under {binRoot}. " +
                "Build samples/xStunit.SystemLibraryPlugins (the Cli.Tests project reference should do this automatically).");
        }
    }
}
