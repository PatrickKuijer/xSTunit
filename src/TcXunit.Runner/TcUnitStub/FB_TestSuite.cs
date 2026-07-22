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
        // A test's in-progress record, keyed by name. Kept around after
        // finishing (rather than removed) so IS_TEST_FINISHED('X') can still
        // resolve 'X' by name after it completes.
        private sealed class TestRecord
        {
            public TestRecord(string name)
            {
                Name = name;
                Failures = new List<AssertionFailure>();
            }

            public string Name { get; }
            public List<AssertionFailure> Failures { get; }
            public int? OrderNumber { get; set; }
            public bool Finished { get; set; }
        }

        private readonly List<TestCaseResult> _finished = new List<TestCaseResult>();
        private readonly Dictionary<string, TestRecord> _records = new Dictionary<string, TestRecord>();
        private string _currentName;

        // Mirrors upstream's per-suite NumberOfOrderedTests / CurrentlyRunningOrderedTestInTestSuite.
        // Order numbers and the "whose turn is it" counter both start at 1.
        private int _nextOrderNumber = 1;
        private int _currentOrderedTurn = 1;

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

            if (_records.ContainsKey(name))
                throw new NotSupportedException(
                    $"TEST('{name}') called twice in one pass — cyclic/stateful test suites aren't supported in v1");

            _records[name] = new TestRecord(name);
            _currentName = name;
        }

        // Declares (or re-attaches to) an ordered test. Returns TRUE only when
        // it's currently this test's turn and it hasn't finished yet - matching
        // upstream TEST_ORDERED() semantics for a single-pass run (TcXunit-k28.7).
        protected bool TEST_ORDERED(string name)
        {
            if (!_records.TryGetValue(name, out var record))
            {
                record = new TestRecord(name) { OrderNumber = _nextOrderNumber++ };
                _records[name] = record;
            }

            if (record.OrderNumber != _currentOrderedTurn || record.Finished)
                return false;

            if (_currentName != null)
                throw new InvalidOperationException(
                    $"TEST_ORDERED('{name}') called before TEST_FINISHED() for '{_currentName}'");

            _currentName = name;
            return true;
        }

        protected void TEST_FINISHED()
        {
            if (_currentName == null)
                throw new InvalidOperationException("TEST_FINISHED() called with no matching TEST()");

            FinishRecord(_records[_currentName]);
            _currentName = null;
        }

        // Finishes a named test regardless of which test is "current" (used
        // by multi-cycle/async test bodies). Unlike TEST_FINISHED(), an
        // unknown name fails fast rather than upstream's log-and-abort
        // mechanic, which TcXunit has no equivalent for (TcXunit-k28.7).
        protected void TEST_FINISHED_NAMED(string name)
        {
            if (!_records.TryGetValue(name, out var record))
                throw new InvalidOperationException(
                    $"TEST_FINISHED_NAMED('{name}') called for a test never declared with TEST()/TEST_ORDERED()");

            if (record.Finished)
                return;

            FinishRecord(record);
            if (_currentName == name)
                _currentName = null;
        }

        // Polls whether a named test in this suite has finished. Unlike
        // upstream (whose Tests[] scan silently returns FALSE for an unknown
        // name), an unknown name fails fast here to surface suite-authoring
        // typos (TcXunit-k28.7).
        protected bool IS_TEST_FINISHED(string name)
        {
            if (!_records.TryGetValue(name, out var record))
                throw new InvalidOperationException(
                    $"IS_TEST_FINISHED('{name}') called for a test never declared with TEST()/TEST_ORDERED()");

            return record.Finished;
        }

        private void FinishRecord(TestRecord record)
        {
            record.Finished = true;
            _finished.Add(new TestCaseResult(record.Name, record.Failures));

            if (record.OrderNumber.HasValue && record.OrderNumber.Value == _currentOrderedTurn)
                _currentOrderedTurn++;
        }

        // Upstream FB_Test.SetAssertionMessage()/SetAssertionType() only set
        // AssertionMessage/AssertionType 'if not already set' - a test with
        // several failing asserts still fails, but only the first failure's
        // message is ever recorded. Later Fail() calls in the same TEST()
        // bracket are dropped here to match (TcXunit-k28.1).
        //
        // Message format mirrors upstream FB_AdsAssertMessageFormatter:
        // "FAILED TEST '<name>', EXP: <expected>, ACT: <actual>[, MSG: <message>]"
        // - MSG is only appended when message is non-empty (TcXunit-k28.2).
        private void Fail(string expected, string actual, string message)
        {
            if (_currentName == null)
                throw new InvalidOperationException("Assertion called outside a TEST()/TEST_FINISHED() bracket");

            var failures = _records[_currentName].Failures;
            if (failures.Count > 0)
                return;

            var formatted = $"FAILED TEST '{_currentName}', EXP: {expected}, ACT: {actual}";
            if (!string.IsNullOrEmpty(message))
                formatted += $", MSG: {message}";

            failures.Add(new AssertionFailure(formatted));
        }

        private static string FormatBool(bool value) => value ? "TRUE" : "FALSE";

        // Upstream delegates both through AssertEquals_BOOL(Expected:=TRUE/
        // FALSE, Actual:=Condition, Message) rather than failing with just a
        // bare message, so a failing AssertTrue/AssertFalse carries the same
        // EXP/ACT detail as any other assert (TcXunit-k28.3).
        protected void AssertTrue(bool condition, string message) =>
            AssertEquals_BOOL(true, condition, message);

        protected void AssertFalse(bool condition, string message) =>
            AssertEquals_BOOL(false, condition, message);

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
                Fail(expectedInt.ToString(), actualInt.ToString(), message);
        }

        protected void AssertEquals_BOOL(bool expected, bool actual, string message)
        {
            if (expected != actual)
                Fail(FormatBool(expected), FormatBool(actual), message);
        }

        protected void AssertEquals_STRING(string expected, string actual, string message)
        {
            if (expected != actual)
                Fail($"'{expected}'", $"'{actual}'", message);
        }

        protected void AssertEquals_REAL(double expected, double actual, double delta, string message)
        {
            if (Math.Abs(expected - actual) > delta)
                Fail($"{expected} +/- {delta}", actual.ToString(), message);
        }
    }
}
