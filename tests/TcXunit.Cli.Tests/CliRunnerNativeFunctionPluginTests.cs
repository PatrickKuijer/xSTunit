using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace TcXunit.Cli.Tests
{
    // TcXunit-6k2 end-to-end: a suite whose call chain reaches a compiled-only
    // TwinCAT library function fails clearly without a plugin, and passes with
    // one - exercising the real loader (AssemblyLoadContext, reflection over a
    // DLL on disk), not an in-process registration.
    public class CliRunnerNativeFunctionPluginTests : IDisposable
    {
        private readonly string _pluginDir;

        public CliRunnerNativeFunctionPluginTests()
        {
            // A directory containing ONLY the plugin: pointing the loader at
            // the sample's own bin folder would also hand it TcXunit.Parser/
            // TcXunit.Runner (copied there transitively), and while the loader
            // skips host assemblies by name, the test shouldn't depend on that
            // guard to get a clean result.
            _pluginDir = Path.Combine(Path.GetTempPath(), "tcxunit-plugin-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_pluginDir);
            File.Copy(SamplePluginDll(), Path.Combine(_pluginDir, "TcXunit.SamplePlugins.dll"));
        }

        public void Dispose()
        {
            // The plugin assembly stays loaded (its context is collectible but
            // nothing unloads it mid-process), so on Windows the file may still
            // be locked here. Cleanup is best-effort: a stray temp directory is
            // not worth failing an otherwise-green test over.
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
            // The TcXunit-kii regression: this used to be a bare "Object
            // reference not set to an instance of an object."
            Assert.DoesNotContain("Object reference not set", text);
            Assert.Contains("F_CheckSum16", text);
            Assert.Contains("native function", text);
            // And the fault is still attributed through the whole chain, so the
            // wrapper FUNCTION that made the call is named, not just the suite.
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
            Assert.Contains("plugin: TcXunit.SamplePlugins.dll (2 function(s))", text);
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
            // Same resilience rule as an unloadable POU (TcXunit-iyd.7):
            // reduced coverage is reported, never fatal. The run still
            // completes and still fails on its own merits (exit 1, because the
            // library function is genuinely unresolved) - not exit 2.
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
            File.Copy(SamplePluginDll(), Path.Combine(junkDir, "TcXunit.SamplePlugins.dll"));
            File.WriteAllText(Path.Combine(junkDir, "NotAnAssembly.dll"), "this is not a PE file");

            var output = new StringWriter();
            var exitCode = CliRunner.Run(new[] { FixtureDir(), "--plugins", junkDir }, output);

            var text = output.ToString();
            // The good plugin still loaded and the suites still passed.
            Assert.Equal(0, exitCode);
            Assert.Contains("3 passed", text);
            Assert.Contains("not a managed assembly", text);
        }

        private static string FixtureDir([CallerFilePath] string callerFile = "") =>
            Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(callerFile)!, "..", "Fixtures", "NativeFunctionPluginFixture"));

        // The sample plugin is built by a ReferenceOutputAssembly=false project
        // reference (see the .csproj), so it lands in its own bin folder under
        // whichever configuration the test run used - resolved from this source
        // file's path rather than a hardcoded configuration name.
        private static string SamplePluginDll([CallerFilePath] string callerFile = "")
        {
            var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerFile)!, "..", ".."));
            var binRoot = Path.Combine(repoRoot, "samples", "TcXunit.SamplePlugins", "bin");

            if (Directory.Exists(binRoot))
            {
                var found = Directory.GetFiles(binRoot, "TcXunit.SamplePlugins.dll", SearchOption.AllDirectories);
                if (found.Length > 0)
                {
                    Array.Sort(found, (a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
                    return found[0];
                }
            }

            throw new InvalidOperationException(
                $"sample plugin not built - expected TcXunit.SamplePlugins.dll under {binRoot}. " +
                "Build samples/TcXunit.SamplePlugins (the Cli.Tests project reference should do this automatically).");
        }
    }
}
