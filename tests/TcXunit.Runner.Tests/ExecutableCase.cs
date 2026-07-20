using TcXunit.Runner.TcUnitStub;

namespace TcXunit.Runner.Tests
{
    /// <summary>
    /// Wraps a discovered TEST() case with the suite instance it belongs to, so
    /// xUnit's Theory/MemberData can surface each case as its own Test Explorer
    /// entry (ToString drives the displayed test name) — see TcXunit-w5x.7 item 6.
    /// </summary>
    public sealed class ExecutableCase
    {
        public ExecutableCase(FB_TestSuite suite, TestCase testCase)
        {
            Suite = suite;
            Case = testCase;
        }

        public FB_TestSuite Suite { get; }
        public TestCase Case { get; }

        public override string ToString() => Case.Name;
    }
}
