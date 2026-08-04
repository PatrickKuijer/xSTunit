using System;
using System.IO;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // A fixture engine must fail loudly, naming what it was pointed at. The
    // loader underneath it is deliberately tolerant - a real tree always holds
    // files outside the parse subset, and one of them must not take a whole run
    // down - but a fixture directory holds nothing that is allowed to go
    // missing. Without these, a renamed fixture or a POU that quietly stopped
    // parsing would leave a half-populated registry, and the suite that needed
    // the dropped type would fail somewhere unrelated, saying nothing about the
    // real cause.
    public class MiniloadFixtureLoadFailureTests
    {
        [Fact]
        public void DirectoryThatDoesNotExist_ThrowsNamingTheDirectory()
        {
            var missing = Path.Combine(Path.GetTempPath(), "xstunit-absent-" + Guid.NewGuid().ToString("n"));

            var ex = Assert.Throws<IOException>(() => MiniloadFixtureEngine.Create(missing));

            Assert.Contains(missing, ex.Message);
        }

        [Fact]
        public void FileTheLoaderWouldSkip_ThrowsNamingThatFile_RatherThanLoadingWithoutIt()
        {
            var dir = Path.Combine(Path.GetTempPath(), "xstunit-fixture-" + Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(dir);
            try
            {
                var unreadable = Path.Combine(dir, "FB_Torn.TcPOU");
                File.WriteAllText(unreadable, "<TcPlcObject><POU Name=\"FB_Torn\">");

                var ex = Assert.Throws<IOException>(() => MiniloadFixtureEngine.Create(dir));

                Assert.Contains("FB_Torn.TcPOU", ex.Message);
                Assert.Contains(dir, ex.Message);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
