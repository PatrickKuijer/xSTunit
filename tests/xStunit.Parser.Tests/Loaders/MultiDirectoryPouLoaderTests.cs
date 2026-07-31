using System;
using System.IO;
using System.Linq;
using Xunit;

namespace xStunit.Parser.Tests
{
    // TcXunit-98e.1: shared prefactor for multi-directory POU discovery
    // (Tests PLC project + separately-referenced Source PLC project).
    public class MultiDirectoryPouLoaderTests : IDisposable
    {
        private readonly string _dirA;
        private readonly string _dirB;

        public MultiDirectoryPouLoaderTests()
        {
            _dirA = CreateTempDir();
            _dirB = CreateTempDir();
        }

        public void Dispose()
        {
            Directory.Delete(_dirA, recursive: true);
            Directory.Delete(_dirB, recursive: true);
        }

        [Fact]
        public void Load_DistinctTypesAcrossTwoDirectories_MergeCleanly()
        {
            WritePou(_dirA, "FB_One.TcPOU", "FB_One");
            WritePou(_dirB, "FB_Two.TcPOU", "FB_Two");

            var loaded = MultiDirectoryPouLoader.Load(new[] { _dirA, _dirB });

            Assert.Equal(new[] { "FB_One", "FB_Two" }, loaded.Select(l => l.Pou.Name).OrderBy(n => n));
        }

        [Fact]
        public void Load_SameTypeNameInBothDirectories_ThrowsNamingBothFilePaths()
        {
            var pathA = WritePou(_dirA, "FB_Dup.TcPOU", "FB_Dup");
            var pathB = WritePou(_dirB, "FB_Dup.TcPOU", "FB_Dup");

            var ex = Assert.Throws<DuplicatePouTypeException>(
                () => MultiDirectoryPouLoader.Load(new[] { _dirA, _dirB }));

            Assert.Equal("FB_Dup", ex.TypeName);
            Assert.Contains(pathA, ex.FilePaths);
            Assert.Contains(pathB, ex.FilePaths);
            Assert.Contains("FB_Dup", ex.Message);
            Assert.Contains(pathA, ex.Message);
            Assert.Contains(pathB, ex.Message);
        }

        [Fact]
        public void Load_DuplicateTwiceWithinSameDirectoryTree_Throws()
        {
            var subDir = Directory.CreateDirectory(Path.Combine(_dirA, "Nested")).FullName;
            var pathA = WritePou(_dirA, "FB_Dup.TcPOU", "FB_Dup");
            var pathB = WritePou(subDir, "FB_Dup2.TcPOU", "FB_Dup");

            var ex = Assert.Throws<DuplicatePouTypeException>(
                () => MultiDirectoryPouLoader.Load(new[] { _dirA }));

            Assert.Equal("FB_Dup", ex.TypeName);
            Assert.Contains(pathA, ex.FilePaths);
            Assert.Contains(pathB, ex.FilePaths);
        }

        [Fact]
        public void Load_SameDirectoryPassedTwice_DoesNotThrowAndHasNoDuplicateEntries()
        {
            WritePou(_dirA, "FB_One.TcPOU", "FB_One");

            var loaded = MultiDirectoryPouLoader.Load(new[] { _dirA, _dirA });

            Assert.Single(loaded);
            Assert.Equal("FB_One", loaded[0].Pou.Name);
        }

        [Fact]
        public void Load_NestedInputDirectory_DoesNotThrowAndHasNoDuplicateEntries()
        {
            var subDir = Directory.CreateDirectory(Path.Combine(_dirA, "Nested")).FullName;
            WritePou(subDir, "FB_One.TcPOU", "FB_One");

            var loaded = MultiDirectoryPouLoader.Load(new[] { _dirA, subDir });

            Assert.Single(loaded);
            Assert.Equal("FB_One", loaded[0].Pou.Name);
        }

        [Fact]
        public void FindGvlFiles_FindsTcGvlFilesAcrossDirectoriesButNotTcPouOrTcDut()
        {
            var gvlPath = Path.Combine(_dirA, "gFoo.TcGVL");
            File.WriteAllText(gvlPath, "not real xml, just needs to exist for globbing");
            WritePou(_dirA, "FB_One.TcPOU", "FB_One");
            File.WriteAllText(Path.Combine(_dirA, "ST_Foo.TcDUT"), "not real xml either");

            var found = MultiDirectoryPouLoader.FindGvlFiles(new[] { _dirA, _dirB });

            Assert.Equal(new[] { gvlPath }, found);
        }

        [Fact]
        public void FindDutFiles_FindsTcDutFilesAcrossDirectoriesButNotTcPouOrTcGvl()
        {
            var dutPath = Path.Combine(_dirA, "ST_Foo.TcDUT");
            File.WriteAllText(dutPath, "not real xml, just needs to exist for globbing");
            WritePou(_dirA, "FB_One.TcPOU", "FB_One");
            File.WriteAllText(Path.Combine(_dirA, "gFoo.TcGVL"), "not real xml either");

            var found = MultiDirectoryPouLoader.FindDutFiles(new[] { _dirA, _dirB });

            Assert.Equal(new[] { dutPath }, found);
        }

        [Fact]
        public void FindDutFiles_MergesAcrossTwoDirectories()
        {
            var pathA = Path.Combine(_dirA, "ST_One.TcDUT");
            var pathB = Path.Combine(_dirB, "ST_Two.TcDUT");
            File.WriteAllText(pathA, "not real xml, just needs to exist for globbing");
            File.WriteAllText(pathB, "not real xml, just needs to exist for globbing");

            var found = MultiDirectoryPouLoader.FindDutFiles(new[] { _dirA, _dirB });

            Assert.Equal(new[] { pathA, pathB }.OrderBy(f => f).ToArray(), found.OrderBy(f => f).ToArray());
        }

        [Fact]
        public void FindDutFiles_SameDirectoryPassedTwice_DoesNotThrowAndHasNoDuplicateEntries()
        {
            var path = Path.Combine(_dirA, "ST_One.TcDUT");
            File.WriteAllText(path, "not real xml, just needs to exist for globbing");

            var found = MultiDirectoryPouLoader.FindDutFiles(new[] { _dirA, _dirA });

            Assert.Equal(new[] { path }, found);
        }

        [Fact]
        public void FindDutFiles_NestedInputDirectory_DoesNotThrowAndHasNoDuplicateEntries()
        {
            var subDir = Directory.CreateDirectory(Path.Combine(_dirA, "Nested")).FullName;
            var path = Path.Combine(subDir, "ST_One.TcDUT");
            File.WriteAllText(path, "not real xml, just needs to exist for globbing");

            var found = MultiDirectoryPouLoader.FindDutFiles(new[] { _dirA, subDir });

            Assert.Equal(new[] { path }, found);
        }

        private static string CreateTempDir() =>
            Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-multidir-" + Guid.NewGuid())).FullName;

        private static string WritePou(string dir, string fileName, string typeName)
        {
            var path = Path.Combine(dir, fileName);
            File.WriteAllText(path, PouXml(typeName));
            return path;
        }

        private static string PouXml(string typeName) => $@"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""{typeName}"" Id=""{{00000000-0000-0000-0000-000000000001}}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK {typeName}]]></Declaration>
    <Implementation>
      <ST><![CDATA[]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";
    }
}
