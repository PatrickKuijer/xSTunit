using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace xStunit.Cli.Tests
{
    // The WSTRING function set (WCONCAT/WDELETE/WFIND/WINSERT/WLEFT/WLEN/WMID/
    // WREPLACE/WRIGHT) is compiled-only, like every TwinCAT library, so it
    // resolves only through the plugin path - the wide half of what
    // CliRunnerTc2StandardPluginTests covers for narrow STRING.
    public class CliRunnerWideStringPluginTests : IDisposable
    {
        private readonly string _pluginDir;

        public CliRunnerWideStringPluginTests()
        {
            _pluginDir = Path.Combine(Path.GetTempPath(), "xstunit-wstring-plugin-test-" + Guid.NewGuid().ToString("N"));
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
            Assert.Contains("WLEN", text);
            // Same classification as the narrow suite's DELETE: an ALL-CAPS
            // unresolved call reads as an IEC standard-library name once the
            // native-function registry has had - and missed - its chance at it.
            Assert.Contains("isn't supported yet (grow-on-demand", text);
        }

        // The interpreter's CONCAT intrinsic is dispatched on its whole name,
        // which "WCONCAT" never matches - so the plugin's WCONCAT is the
        // only thing that can resolve it. Were an intrinsic to start shadowing
        // that name, this call would quietly succeed here and the plugin
        // function would become dead code.
        [Fact]
        public void Run_WithoutPlugins_LeavesWConcatUnresolvedRatherThanIntrinsicShadowed()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { FixtureDir() }, output);

            var text = output.ToString();
            Assert.Equal(1, exitCode);
            Assert.Contains("WConcatAppendsStr2ToStr1: FAIL", text);
            Assert.Contains("'WCONCAT' isn't supported yet", text);
        }

        [Fact]
        public void Run_WithPluginDirectory_ResolvesEveryWideFunctionAndPasses()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { FixtureDir(), "--plugins", _pluginDir }, output);

            var text = output.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("22 passed, 0 failed", text);
            Assert.Contains("plugin: xStunit.StandardStringPlugins.dll (17 function(s))", text);
        }

        private static string FixtureDir([CallerFilePath] string callerFile = "") =>
            Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(callerFile)!, "..", "Fixtures", "WideStringPluginFixture"));

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
