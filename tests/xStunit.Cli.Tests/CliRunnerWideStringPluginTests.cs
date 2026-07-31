using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace xStunit.Cli.Tests
{
    // TcXunit-93l9 end-to-end: Tc2_Standard's WSTRING function set
    // (WCONCAT/WDELETE/WFIND/WINSERT/WLEFT/WLEN/WMID/WREPLACE/WRIGHT) is
    // compiled-only, like every TwinCAT library, and resolves only through the
    // native-function plugin/registry path (TcXunit-6k2). Same shape as
    // CliRunnerTc2StandardPluginTests (the narrow-STRING half, TcXunit-8po):
    // the real plugin DLL is loaded through the CLI's --plugins loader rather
    // than registered in process.
    //
    // WCONCAT *is* covered here, unlike CONCAT in the narrow suite: the
    // interpreter's CONCAT intrinsic is dispatched by exact ordinal name match
    // (Engine.Expressions.cs `call.MethodName == "CONCAT"`), which "WCONCAT"
    // never hits, so a plugin WCONCAT is reachable rather than dead code.
    public class CliRunnerWideStringPluginTests : IDisposable
    {
        private readonly string _pluginDir;

        public CliRunnerWideStringPluginTests()
        {
            _pluginDir = Path.Combine(Path.GetTempPath(), "tcxunit-wstring-plugin-test-" + Guid.NewGuid().ToString("N"));
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
            // unresolved call from inside a suite reads as "looks like an IEC
            // standard-library call" once the native-function registry has had
            // - and missed - its chance to resolve it.
            Assert.Contains("isn't supported yet (grow-on-demand", text);
        }

        // The evidence behind implementing WCONCAT as a plugin function rather
        // than closing it as already-resolved the way CONCAT was
        // (TcXunit-8po.1): with no plugin loaded, a WCONCAT call reaches the
        // *unresolved* path. Were an intrinsic handling it, this would have
        // concatenated and passed instead. Locks in the reachability the
        // plugin implementation depends on, so a future intrinsic named
        // WCONCAT can't silently turn WConcatFunction into dead code.
        [Fact]
        public void Run_WithoutPlugins_LeavesWConcatUnresolvedRatherThanIntrinsicShadowed()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { FixtureDir() }, output);

            var text = output.ToString();
            Assert.Equal(1, exitCode);
            Assert.Contains("WConcat: FAIL", text);
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
