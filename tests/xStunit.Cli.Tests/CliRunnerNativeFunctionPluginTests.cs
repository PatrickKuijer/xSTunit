using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace xStunit.Cli.Tests
{
    // End-to-end through the real loader (AssemblyLoadContext, reflection over
    // a DLL on disk) rather than an in-process registration: a suite reaching a
    // compiled-only library function must fail clearly without a plugin and
    // pass with one.
    public class CliRunnerNativeFunctionPluginTests : IDisposable
    {
        private readonly string _pluginDir;

        public CliRunnerNativeFunctionPluginTests()
        {
            // A directory holding ONLY the plugin: the sample's own bin folder
            // also contains transitively-copied host assemblies, and while the
            // loader skips those by name, these tests shouldn't depend on that
            // guard to get a clean result.
            _pluginDir = Path.Combine(Path.GetTempPath(), "xstunit-plugin-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_pluginDir);
            File.Copy(SamplePluginDll(), Path.Combine(_pluginDir, "xStunit.SamplePlugins.dll"));
        }

        public void Dispose()
        {
            // The plugin assembly stays loaded for the life of the process, so
            // on Windows the file may still be locked here. A stray temp
            // directory is not worth failing an otherwise-green test over.
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
        public void Run_WithoutPlugins_FailsNamingTheMissingLibraryFunction()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { FixtureDir() }, output);

            var text = output.ToString();
            Assert.Equal(1, exitCode);
            // An unresolved library function has to name itself; surfacing as a
            // bare null-reference message tells a reader nothing about what to
            // wire up.
            Assert.DoesNotContain("Object reference not set", text);
            Assert.Contains("F_CheckSum16", text);
            Assert.Contains("native function", text);
            // Attributed through the whole chain, so the wrapper FUNCTION that
            // made the call is named, not just the suite.
            Assert.Contains("F_ComputeChecksum", text);
        }

        [Fact]
        public void Run_WithPluginDirectory_ResolvesTheLibraryFunctionAndPasses()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { FixtureDir(), "--plugins", _pluginDir }, output);

            var text = output.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("3 passed, 0 failed", text);
            Assert.Contains("plugin: xStunit.SamplePlugins.dll (2 function(s))", text);
        }

        [Fact]
        public void Run_PluginsFlagAcceptsEqualsForm()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { FixtureDir(), $"--plugins={_pluginDir}" }, output);

            Assert.Equal(0, exitCode);
            Assert.Contains("3 passed, 0 failed", output.ToString());
        }

        [Fact]
        public void Run_PluginsFlagWithoutAValue_IsAUsageError()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { FixtureDir(), "--plugins" }, output);

            Assert.Equal(2, exitCode);
            Assert.Contains("--plugins requires a value", output.ToString());
        }

        [Fact]
        public void Run_MissingPluginDirectory_IsReportedAsASkipRatherThanKillingTheRun()
        {
            // Same resilience rule as an unloadable POU: reduced capability is
            // reported, never fatal. The run completes and fails on its own
            // merits (exit 1), never as a usage error (exit 2).
            var output = new StringWriter();
            var missing = Path.Combine(_pluginDir, "does-not-exist");

            var exitCode = CliRunner.Run(new[] { FixtureDir(), "--plugins", missing }, output);

            var text = output.ToString();
            Assert.Equal(1, exitCode);
            Assert.Contains("plugin directory does not exist", text);
        }

        [Fact]
        public void Run_PluginDirectoryWithAJunkDll_SkipsItAndKeepsGoing()
        {
            var junkDir = Path.Combine(_pluginDir, "junk");
            Directory.CreateDirectory(junkDir);
            File.Copy(SamplePluginDll(), Path.Combine(junkDir, "xStunit.SamplePlugins.dll"));
            File.WriteAllText(Path.Combine(junkDir, "NotAnAssembly.dll"), "this is not a PE file");

            var output = new StringWriter();
            var exitCode = CliRunner.Run(new[] { FixtureDir(), "--plugins", junkDir }, output);

            var text = output.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("3 passed", text);
            Assert.Contains("not a managed assembly", text);
        }

        private static string FixtureDir([CallerFilePath] string callerFile = "") =>
            Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(callerFile)!, "..", "Fixtures", "NativeFunctionPluginFixture"));

        // The sample plugin is built by a ReferenceOutputAssembly=false project
        // reference, so it lands in its own bin folder under whichever
        // configuration and target framework the run used - searched for rather
        // than named, since neither is knowable from here.
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
