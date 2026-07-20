using System.Collections.Generic;
using System.Linq;

namespace TcXunit.Runner.TcUnitStub
{
    public sealed class TestCaseResult
    {
        public TestCaseResult(string name, IReadOnlyList<AssertionFailure> failures)
        {
            Name = name;
            Failures = failures;
        }

        public string Name { get; }
        public IReadOnlyList<AssertionFailure> Failures { get; }
        public bool Passed => Failures.Count == 0;

        public override string ToString() =>
            Passed ? $"{Name}: PASS" : $"{Name}: FAIL ({string.Join("; ", Failures.Select(f => f.Message))})";
    }
}
