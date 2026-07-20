using System;
using System.Collections.Generic;

namespace TcXunit.Runner.TcUnitStub
{
    /// <summary>
    /// Native C# stand-in for TcUnit's FB_TestSuite. A real interpreted suite POU
    /// extends this and registers cases via <see cref="TEST"/> instead of TcUnit's
    /// cyclic IF TEST('name') THEN pattern — spike simplification, see TcXunit-w5x.7.
    /// </summary>
    public abstract class FB_TestSuite
    {
        private readonly List<TestCase> _cases = new List<TestCase>();
        private List<AssertionFailure> _currentFailures;

        public IReadOnlyList<TestCase> Cases => _cases;

        protected void TEST(string name, Action body)
        {
            _cases.Add(new TestCase(name, body));
        }

        internal TestCaseResult Run(TestCase testCase)
        {
            _currentFailures = new List<AssertionFailure>();
            testCase.Body();
            return new TestCaseResult(testCase.Name, _currentFailures);
        }

        private void Fail(string message)
        {
            _currentFailures.Add(new AssertionFailure(message));
        }

        protected void AssertEquals_INT(int expected, int actual, string message)
        {
            if (expected != actual)
                Fail($"{message}: expected {expected}, got {actual}");
        }

        protected void AssertEquals_BOOL(bool expected, bool actual, string message)
        {
            if (expected != actual)
                Fail($"{message}: expected {expected}, got {actual}");
        }

        protected void AssertEquals_STRING(string expected, string actual, string message)
        {
            if (expected != actual)
                Fail($"{message}: expected '{expected}', got '{actual}'");
        }

        protected void AssertEquals_REAL(double expected, double actual, double delta, string message)
        {
            if (Math.Abs(expected - actual) > delta)
                Fail($"{message}: expected {expected} +/- {delta}, got {actual}");
        }
    }
}
