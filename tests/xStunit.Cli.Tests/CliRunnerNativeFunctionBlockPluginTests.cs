using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace xStunit.Cli.Tests
{
    // End-to-end through the real loader for the STATEFUL plugin surface: a
    // vendor FB with a bExecute/bBusy/bDone handshake, reached by reflection
    // over a DLL on disk rather than an in-process registration. The
    // no-plugin case matters as much as the plugin case - a suite that
    // instantiates a block nothing supplies must say which type it could not
    // resolve.
    public class CliRunnerNativeFunctionBlockPluginTests : IDisposable
    {
        private readonly string _pluginDir;

        public CliRunnerNativeFunctionBlockPluginTests()
        {
            _pluginDir = Path.Combine(Path.GetTempPath(), "xstunit-fb-plugin-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_pluginDir);
            File.Copy(SamplePluginDll(), Path.Combine(_pluginDir, "xStunit.SamplePlugins.dll"));
        }

        public void Dispose()
        {
            // Same reason as the native-function plugin tests: the assembly
            // stays loaded for the life of the process, so the file may still
            // be locked on Windows.
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
        public void Run_WithPluginDirectory_DrivesTheStatefulBlockAcrossCycles()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { FixtureDir(), "--plugins", _pluginDir }, output);

            var text = output.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("3 passed, 0 failed", text);
            // The block count only appears once a plugin actually supplies
            // one, so this line is also what says the FB half of the contract
            // was found at all.
            Assert.Contains("1 function block(s)", text);
        }

        [Fact]
        public void Run_WithoutPlugins_FailsRatherThanSilentlyTreatingTheBlockAsASuite()
        {
            // An unresolved FB type name is the one failure mode this surface
            // could hide: without the plugin the ancestry walk falls through to
            // the TcUnit-suite classification, and a bare invocation of it would
            // otherwise be a no-op that quietly fails the assertions.
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { FixtureDir() }, output);

            Assert.Equal(1, exitCode);
            Assert.DoesNotContain("Object reference not set", output.ToString());
        }

        private static string FixtureDir([CallerFilePath] string callerFile = "") =>
            Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(callerFile)!, "..", "Fixtures", "NativeFunctionBlockPluginFixture"));

        // Searched for rather than named, for the same reason as the
        // native-function plugin tests: neither the configuration nor the
        // target framework of the sample's own build output is knowable here.
        private static string SamplePluginDll([CallerFilePath] string callerFile = "")
        {
            var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerFile)!, "..", ".."));
            var binRoot = Path.Combine(repoRoot, "samples", "xStunit.SamplePlugins", "bin");

            if (Directory.Exists(binRoot))
            {
                var found = Directory.GetFiles(binRoot, "xStunit.SamplePlugins.dll", SearchOption.AllDirectories);
                if (found.Length > 0)
                {
                    Array.Sort(found, (a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
                    return found[0];
                }
            }

            throw new InvalidOperationException(
                $"sample plugin not built - expected xStunit.SamplePlugins.dll under {binRoot}. " +
                "Build samples/xStunit.SamplePlugins (the Cli.Tests project reference should do this automatically).");
        }
    }
}
