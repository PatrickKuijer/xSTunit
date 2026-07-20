using TcXunit.Runner.TcUnitStub;

namespace TcXunit.Runner.Tests
{
    /// <summary>
    /// Wraps an already-run TEST() case result so xUnit's Theory/MemberData can
    /// surface each one as its own Test Explorer entry (ToString drives the
    /// displayed test name) — see TcXunit-w5x.7 item 6.
    /// </summary>
    public sealed class ExecutableCase
    {
        public ExecutableCase(TestCaseResult result)
        {
            Result = result;
        }

        public TestCaseResult Result { get; }

        public override string ToString() => Result.Name;
    }
}
