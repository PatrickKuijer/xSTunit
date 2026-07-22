using System;
using System.Collections.Generic;

namespace TcXunit.Runner.TcUnitStub
{
    /// <summary>
    /// Native C# stand-in for TcUnit's FB_TestSuite. A real suite POU calls
    /// TEST('name') / TEST_FINISHED() imperatively inside its cyclic body — the
    /// case list isn't known ahead of time, it's discovered by running the body
    /// (see TcXunit-w5x.7 item 3, grounded against real TcUnit-Verifier suites).
    /// v1 runs the body once per suite instead of PLC-cyclically; a repeated
    /// TEST() name in one pass means the suite needs cyclic semantics v1 doesn't
    /// support, so it's rejected rather than silently misreported.
    /// </summary>
    public abstract class FB_TestSuite
    {
        private readonly List<TestCaseResult> _finished = new List<TestCaseResult>();
        private readonly HashSet<string> _seenNames = new HashSet<string>();
        private string _currentName;
        private List<AssertionFailure> _currentFailures;

        protected abstract void Body();

        internal IReadOnlyList<TestCaseResult> Run()
        {
            Body();

            if (_currentName != null)
                throw new InvalidOperationException($"TEST('{_currentName}') was never closed with TEST_FINISHED()");

            return _finished;
        }

        protected void TEST(string name)
        {
            if (_currentName != null)
                throw new InvalidOperationException($"TEST('{name}') called before TEST_FINISHED() for '{_currentName}'");

            if (!_seenNames.Add(name))
                throw new NotSupportedException(
                    $"TEST('{name}') called twice in one pass — cyclic/stateful test suites aren't supported in v1");

            _currentName = name;
            _currentFailures = new List<AssertionFailure>();
        }

        protected void TEST_FINISHED()
        {
            if (_currentName == null)
                throw new InvalidOperationException("TEST_FINISHED() called with no matching TEST()");

            _finished.Add(new TestCaseResult(_currentName, _currentFailures));
            _currentName = null;
            _currentFailures = null;
        }

        // Upstream FB_Test.SetAssertionMessage()/SetAssertionType() only set
        // AssertionMessage/AssertionType 'if not already set' - a test with
        // several failing asserts still fails, but only the first failure's
        // message is ever recorded. Later Fail() calls in the same TEST()
        // bracket are dropped here to match (TcXunit-k28.1).
        private void Fail(string message)
        {
            if (_currentName == null)
                throw new InvalidOperationException("Assertion called outside a TEST()/TEST_FINISHED() bracket");

            if (_currentFailures.Count == 0)
                _currentFailures.Add(new AssertionFailure(message));
        }

        protected void AssertTrue(bool condition, string message)
        {
            if (!condition)
                Fail(message);
        }

        protected void AssertFalse(bool condition, string message)
        {
            if (condition)
                Fail(message);
        }

        // Upstream operates on IEC 61131-3 INT, a signed 16-bit type - a real
        // INT variable would already be truncated/wrapped to that range by
        // the time it reaches this assert. Params stay C# int (the rest of
        // this codebase has no narrower INT representation), but the compare
        // wraps both operands to 16 bits first so out-of-range values that
        // would collide as INT compare equal here too (TcXunit-k28.4).
        protected void AssertEquals_INT(int expected, int actual, string message)
        {
            var expectedInt = unchecked((short)expected);
            var actualInt = unchecked((short)actual);
            if (expectedInt != actualInt)
                Fail($"{message}: expected {expectedInt}, got {actualInt}");
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
