using System.IO;
using System.Runtime.CompilerServices;

namespace TcXunit.Interpreter.Tests
{
    // Resolves vendored fixture POUs (tests/Fixtures/...) relative to the
    // calling test file instead of a machine-local external repo path
    // (TcXunit-1ys). Callers live one level deeper now
    // (tests/TcXunit.Interpreter.Tests/<Topic>/...), hence the extra ".."
    // to still reach tests/Fixtures.
    internal static class TestFixtures
    {
        public static string FbCounterFixtureDir([CallerFilePath] string callerFile = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerFile)!, "..", "..", "Fixtures", "FbCounterFixture"));
    }
}
