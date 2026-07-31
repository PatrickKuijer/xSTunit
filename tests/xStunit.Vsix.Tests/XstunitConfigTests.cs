using System;
using System.IO;
using xStunit.Vsix.TestRunner;
using Xunit;

namespace xStunit.Vsix.Tests
{
    // XstunitConfig.cs is source-linked into this net8.0 project (see the csproj);
    // the net472 extension it ships in does not build outside Visual Studio, so
    // these are the only tests that ever run against xstunit.json's loading rules
    // and defaults.
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

                // A bare name has to stay bare so the cmd.exe invocation resolves it via
                // PATH, rather than being rewritten relative to the config directory.
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
            // xstunit.json is hand-written, so a casing difference against the config's
            // own fields must still bind rather than leave the property silently null.
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
