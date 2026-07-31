using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace xStunit.Cli.Tests
{
    // TcXunit-8po end-to-end: Tc2_Standard's DELETE/FIND/INSERT/LEFT/LEN/MID/
    // REPLACE/RIGHT are compiled-only, like every TwinCAT library, and
    // resolve only through the native-function plugin/registry path
    // (TcXunit-6k2) - never a built-in intrinsic. Mirrors
    // CliRunnerNativeFunctionPluginTests, loading the real plugin DLL through
    // the CLI's --plugins loader rather than an in-process registration.
    //
    // CONCAT is out of scope here: TcXunit-8po.1 was closed as already
    // resolved by TcXunit-3lt's interpreter intrinsic (see the fixture POU's
    // header comment).
    public class CliRunnerTc2StandardPluginTests : IDisposable
    {
        private readonly string _pluginDir;

        public CliRunnerTc2StandardPluginTests()
        {
            _pluginDir = Path.Combine(Path.GetTempPath(), "tcxunit-tc2std-plugin-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_pluginDir);
            File.Copy(PluginDll(), Path.Combine(_pluginDir, "TcXunit.Tc2StandardPlugins.dll"));
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
            // Unlike F_CheckSum16 (mixed-case), an ALL-CAPS unresolved call
            // from inside a suite is classified as "looks like an IEC
            // standard-library call" (NativeMethodBridge.LooksLikeTcUnitApiName,
            // TcXunit-w5x.12/TcXunit-2o9.1) once the native-function registry
            // has already had - and missed - its chance to resolve it, so the
            // diagnostic here is the grow-on-demand message, not "native
            // function".
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
            // 8 narrow-STRING functions (TcXunit-8po) + 9 WSTRING
            // counterparts (TcXunit-93l9, exercised by
            // CliRunnerWideStringPluginTests) in the one plugin assembly.
            Assert.Contains("plugin: TcXunit.Tc2StandardPlugins.dll (17 function(s))", text);
        }

        private static string FixtureDir([CallerFilePath] string callerFile = "") =>
            Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(callerFile)!, "..", "Fixtures", "Tc2StandardPluginFixture"));

        private static string PluginDll([CallerFilePath] string callerFile = "")
        {
            var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerFile)!, "..", ".."));
            var binRoot = Path.Combine(repoRoot, "samples", "TcXunit.Tc2StandardPlugins", "bin");

            if (Directory.Exists(binRoot))
            {
                var found = Directory.GetFiles(binRoot, "TcXunit.Tc2StandardPlugins.dll", SearchOption.AllDirectories);
                if (found.Length > 0)
                {
                    Array.Sort(found, (a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
                    return found[0];
                }
            }

            throw new InvalidOperationException(
                $"Tc2StandardPlugins not built - expected TcXunit.Tc2StandardPlugins.dll under {binRoot}. " +
                "Build samples/TcXunit.Tc2StandardPlugins (the Cli.Tests project reference should do this automatically).");
        }
    }
}
