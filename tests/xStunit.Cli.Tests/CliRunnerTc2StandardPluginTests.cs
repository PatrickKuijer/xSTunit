using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace xStunit.Cli.Tests
{
    // The standard string functions (DELETE/FIND/INSERT/LEFT/LEN/MID/REPLACE/
    // RIGHT) are compiled-only, like every TwinCAT library, so they resolve
    // through the plugin path and never as a built-in intrinsic. Loads the real
    // DLL through the CLI's --plugins loader rather than registering in
    // process.
    public class CliRunnerTc2StandardPluginTests : IDisposable
    {
        private readonly string _pluginDir;

        public CliRunnerTc2StandardPluginTests()
        {
            _pluginDir = Path.Combine(Path.GetTempPath(), "tcxunit-tc2std-plugin-test-" + Guid.NewGuid().ToString("N"));
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
        public void Run_WithoutPlugins_FailsNamingTheMissingLibraryFunction()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { FixtureDir() }, output);

            var text = output.ToString();
            Assert.Equal(1, exitCode);
            Assert.DoesNotContain("Object reference not set", text);
            Assert.Contains("DELETE", text);
            // An ALL-CAPS unresolved call from inside a suite reads as an IEC
            // standard-library name once the native-function registry has had -
            // and missed - its chance at it, so the diagnostic is the
            // grow-on-demand one rather than the "native function" wording a
            // mixed-case name gets.
            Assert.Contains("isn't supported yet (grow-on-demand", text);
        }

        [Fact]
        public void Run_WithPluginDirectory_ResolvesEveryFunctionAndPasses()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { FixtureDir(), "--plugins", _pluginDir }, output);

            var text = output.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("13 passed, 0 failed", text);
            // 17 = 8 narrow-STRING functions plus the 9 WSTRING counterparts
            // CliRunnerWideStringPluginTests exercises; both live in this one
            // assembly.
            Assert.Contains("plugin: xStunit.StandardStringPlugins.dll (17 function(s))", text);
        }

        private static string FixtureDir([CallerFilePath] string callerFile = "") =>
            Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(callerFile)!, "..", "Fixtures", "Tc2StandardPluginFixture"));

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
                $"Tc2StandardPlugins not built - expected xStunit.StandardStringPlugins.dll under {binRoot}. " +
                "Build samples/xStunit.StandardStringPlugins (the Cli.Tests project reference should do this automatically).");
        }
    }
}
