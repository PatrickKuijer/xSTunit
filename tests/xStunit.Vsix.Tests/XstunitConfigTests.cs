using System;
using System.IO;
using xStunit.Vsix.TestRunner;
using Xunit;

namespace xStunit.Vsix.Tests
{
    // XstunitConfig.Load() was previously untestable outside net472 (see
    // XstunitConfig.cs's history and TcXunit-cmp): it used
    // System.Web.Script.Serialization.JavaScriptSerializer, which has no
    // net8.0-compatible package. Swapping it to System.Text.Json let this file be
    // source-linked into this net8.0 test project (see this project's own csproj
    // comment) -- these tests cover the "XstunitConfig loading/defaults" gap the
    // TcXunit-1tt epic's own Testing Decisions originally called for but which
    // TcXunit-1tt.2 had to defer.
    public class XstunitConfigTests
    {
        private static string CreateTempDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "XstunitConfigTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        [Fact]
        public void Load_ValidConfig_MapsPathsAndCliPath()
        {
            var directory = CreateTempDirectory();
            try
            {
                File.WriteAllText(Path.Combine(directory, "xstunit.json"),
                    @"{ ""paths"": [""./POUs"", ""./MorePOUs""], ""cliPath"": ""xstunit"" }");

                var config = XstunitConfig.Load(directory);

                Assert.Equal(new[] { "./POUs", "./MorePOUs" }, config.Paths);
                Assert.Equal("xstunit", config.CliPath);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void Load_MissingCliPath_DefaultsToBareCommandName()
        {
            var directory = CreateTempDirectory();
            try
            {
                File.WriteAllText(Path.Combine(directory, "xstunit.json"), @"{ ""paths"": [""./POUs""] }");

                var config = XstunitConfig.Load(directory);

                Assert.Equal("xstunit", config.CliPath);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void Load_CliPathLooksLikeARelativePath_ResolvesAgainstConfigDirectory()
        {
            var directory = CreateTempDirectory();
            try
            {
                File.WriteAllText(Path.Combine(directory, "xstunit.json"),
                    @"{ ""paths"": [""./POUs""], ""cliPath"": ""./tools/xstunit.exe"" }");

                var config = XstunitConfig.Load(directory);

                Assert.Equal(Path.GetFullPath(Path.Combine(directory, "./tools/xstunit.exe")), config.CliPath);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void Load_CliPathIsBareCommandName_LeftUnresolvedForPathLookup()
        {
            var directory = CreateTempDirectory();
            try
            {
                File.WriteAllText(Path.Combine(directory, "xstunit.json"),
                    @"{ ""paths"": [""./POUs""], ""cliPath"": ""xstunit"" }");

                var config = XstunitConfig.Load(directory);

                // No slash/backslash in "xstunit" -- must stay a bare command name so
                // XstunitProcessRunner's cmd.exe /c invocation resolves it via PATH,
                // not get rewritten into a path relative to the config directory.
                Assert.Equal("xstunit", config.CliPath);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void Load_MissingPlugins_LeavesPluginsNull()
        {
            var directory = CreateTempDirectory();
            try
            {
                File.WriteAllText(Path.Combine(directory, "xstunit.json"), @"{ ""paths"": [""./POUs""] }");

                var config = XstunitConfig.Load(directory);

                Assert.Null(config.Plugins);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void Load_PluginsLooksLikeARelativePath_ResolvesAgainstConfigDirectory()
        {
            var directory = CreateTempDirectory();
            try
            {
                File.WriteAllText(Path.Combine(directory, "xstunit.json"),
                    @"{ ""paths"": [""./POUs""], ""plugins"": ""./plugins"" }");

                var config = XstunitConfig.Load(directory);

                Assert.Equal(Path.GetFullPath(Path.Combine(directory, "./plugins")), config.Plugins);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void Load_PluginsIsRooted_LeftUnchanged()
        {
            var directory = CreateTempDirectory();
            var rooted = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "SomePlugins"));
            try
            {
                File.WriteAllText(Path.Combine(directory, "xstunit.json"),
                    $@"{{ ""paths"": [""./POUs""], ""plugins"": ""{rooted.Replace("\\", "\\\\")}"" }}");

                var config = XstunitConfig.Load(directory);

                Assert.Equal(rooted, config.Plugins);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void Load_PluginsIsBareName_LeftUnresolved()
        {
            var directory = CreateTempDirectory();
            try
            {
                File.WriteAllText(Path.Combine(directory, "xstunit.json"),
                    @"{ ""paths"": [""./POUs""], ""plugins"": ""Plugins"" }");

                var config = XstunitConfig.Load(directory);

                // No slash/backslash in "Plugins" -- mirrors cliPath's bare-name case:
                // left unresolved rather than joined against the config directory.
                Assert.Equal("Plugins", config.Plugins);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void Load_NoConfigFile_ThrowsFileNotFoundException()
        {
            var directory = CreateTempDirectory();
            try
            {
                Assert.Throws<FileNotFoundException>(() => XstunitConfig.Load(directory));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void Load_EmptyPathsArray_ThrowsInvalidOperationException()
        {
            var directory = CreateTempDirectory();
            try
            {
                File.WriteAllText(Path.Combine(directory, "xstunit.json"), @"{ ""paths"": [] }");

                Assert.Throws<InvalidOperationException>(() => XstunitConfig.Load(directory));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void Load_MissingPathsField_ThrowsInvalidOperationException()
        {
            var directory = CreateTempDirectory();
            try
            {
                File.WriteAllText(Path.Combine(directory, "xstunit.json"), @"{ ""cliPath"": ""xstunit"" }");

                Assert.Throws<InvalidOperationException>(() => XstunitConfig.Load(directory));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void Load_UppercasePropertyNames_StillBindsCaseInsensitively()
        {
            // Mirrors JavaScriptSerializer's default case-insensitive member binding,
            // which this class's deserialization relied on before the System.Text.Json
            // swap -- SerializerOptions.PropertyNameCaseInsensitive keeps that contract.
            var directory = CreateTempDirectory();
            try
            {
                File.WriteAllText(Path.Combine(directory, "xstunit.json"),
                    @"{ ""Paths"": [""./POUs""], ""CliPath"": ""xstunit"" }");

                var config = XstunitConfig.Load(directory);

                Assert.Equal(new[] { "./POUs" }, config.Paths);
                Assert.Equal("xstunit", config.CliPath);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
