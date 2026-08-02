using System.IO;
using System.Runtime.CompilerServices;

namespace xStunit.Interpreter.Tests
{
    // Fixture POUs are located from the caller's own source path rather than
    // the working directory, so a run is independent of where it was started
    // from. The two ".." hops assume every caller sits in a topic folder under
    // tests/xStunit.Interpreter.Tests/ - a caller at the project root, or one
    // folder deeper, would resolve somewhere else entirely.
    internal static class TestFixtures
    {
        public static string FbCounterFixtureDir([CallerFilePath] string callerFile = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerFile)!, "..", "..", "Fixtures", "FbCounterFixture"));

        public static string LayoutOracleFixtureDir([CallerFilePath] string callerFile = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerFile)!, "..", "..", "Fixtures", "LayoutOracleFixture"));
    }
}
