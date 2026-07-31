using xStunit.Runner.TcUnitStub;

namespace xStunit.Runner.Tests
{
    /// <summary>
    /// Wraps an already-run TEST() case so a Theory/MemberData row shows up as
    /// its own Test Explorer entry — <see cref="ToString"/> is what names it.
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
