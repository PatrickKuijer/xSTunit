using System.Collections.Generic;
using System.Linq;

namespace xStunit.Runner.TcUnitStub
{
    public sealed class TestCaseResult
    {
        public TestCaseResult(string name, IReadOnlyList<AssertionFailure> failures, long elapsedMilliseconds = 0)
        {
            Name = name;
            Failures = failures;
            ElapsedMilliseconds = elapsedMilliseconds;
        }

        public string Name { get; }
        public IReadOnlyList<AssertionFailure> Failures { get; }
        public long ElapsedMilliseconds { get; }
        public bool Passed => Failures.Count == 0;

        public override string ToString() =>
            Passed ? $"{Name}: PASS" : $"{Name}: FAIL ({string.Join("; ", Failures.Select(f => f.Message))})";
    }
}
