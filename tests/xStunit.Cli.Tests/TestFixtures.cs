using System.IO;
using System.Runtime.CompilerServices;

namespace xStunit.Cli.Tests
{
    internal static class TestFixtures
    {
        public static string FbCounterFixtureDir([CallerFilePath] string callerFile = "") =>
            FixtureDir("FbCounterFixture", callerFile);

        // The fault sits on a deliberately not-first body line, so the
        // line-number assertions that consume this fixture would pass
        // vacuously if the reported line ever collapsed to 1.
        public static string FailingLineFixtureDir([CallerFilePath] string callerFile = "") =>
            FixtureDir("FailingLineFixture", callerFile);

        public static string TestSuiteWithClockFixtureDir([CallerFilePath] string callerFile = "") =>
            FixtureDir("TestSuiteWithClock", callerFile);

        private static string FixtureDir(string name, string callerFile) =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerFile)!, "..", "Fixtures", name));
    }
}
