using System.IO;
using System.Runtime.CompilerServices;

namespace xStunit.Parser.Tests
{
    // Fixture paths are resolved from THIS file's own source location, so they
    // survive changes to the build output layout, never point at a
    // machine-local checkout outside the repo, and do not depend on how deep
    // in the project tree the calling test sits.
    internal static class TestFixtures
    {
        public static string FbCounterFixtureDir() => FixtureDir("FbCounterFixture");

        public static string MiniloadSensorFixtureDir() => FixtureDir("MiniloadSensorFixture");

        private static string FixtureDir(string name, [CallerFilePath] string thisFile = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "Fixtures", name));
    }
}
