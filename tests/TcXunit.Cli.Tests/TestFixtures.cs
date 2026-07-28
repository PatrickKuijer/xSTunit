using System.IO;
using System.Runtime.CompilerServices;

namespace TcXunit.Cli.Tests
{
    // Resolves vendored fixture POUs (tests/Fixtures/...) relative to the
    // calling test file instead of a machine-local external repo path
    // (TcXunit-1ys).
    internal static class TestFixtures
    {
        public static string FbCounterFixtureDir([CallerFilePath] string callerFile = "") =>
            FixtureDir("FbCounterFixture", callerFile);

        // Suite + helper POU that fault on a known, not-first body line
        // (TcXunit-p3t.4).
        public static string FailingLineFixtureDir([CallerFilePath] string callerFile = "") =>
            FixtureDir("FailingLineFixture", callerFile);

        private static string FixtureDir(string name, string callerFile) =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerFile)!, "..", "Fixtures", name));
    }
}
