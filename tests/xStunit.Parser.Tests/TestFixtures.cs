using System.IO;
using System.Runtime.CompilerServices;

namespace xStunit.Parser.Tests
{
    // Fixture paths are resolved from the caller's source location, so they
    // survive changes to the build output layout and never point at a
    // machine-local checkout outside the repo.
    internal static class TestFixtures
    {
        public static string FbCounterFixtureDir([CallerFilePath] string callerFile = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerFile)!, "..", "Fixtures", "FbCounterFixture"));
    }
}
