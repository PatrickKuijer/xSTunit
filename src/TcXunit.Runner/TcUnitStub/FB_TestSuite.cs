using System;
using System.Collections.Generic;

namespace TcXunit.Runner.TcUnitStub
{
    /// <summary>
    /// Native C# stand-in for TcUnit's FB_TestSuite. A real suite POU calls
    /// TEST('name') / TEST_FINISHED() imperatively inside its cyclic body — the
    /// case list isn't known ahead of time, it's discovered by running the body
    /// (see TcXunit-w5x.7 item 3, grounded against real TcUnit-Verifier suites).
    /// Duplicate-name detection is keyed off CurrentCycle, mirroring upstream's
    /// CycleCount-keyed TestCycleCountIndex[] comparison in AddTest: a repeat
    /// TEST('X') in the SAME cycle is a real duplicate (error); a repeat in a
    /// LATER cycle is the normal cyclic re-declaration flow and re-attaches to
    /// the existing test instead (TcXunit-k28.5). No runner today advances
    /// CurrentCycle (single pass per suite, always 0), so this is currently
    /// behavior-identical to always erroring on a repeat name; it's structured
    /// so a future multi-cycle runner can drive re-declaration without further
    /// changes here.
    /// </summary>
    public abstract class FB_TestSuite
    {
        // A test's in-progress record, keyed by name. Kept around after
        // finishing (rather than removed) so IS_TEST_FINISHED('X') can still
        // resolve 'X' by name after it completes.
        private sealed class TestRecord
        {
            public TestRecord(string name, int cycleIndex)
            {
                Name = name;
                Failures = new List<AssertionFailure>();
                LastCycleIndex = cycleIndex;
            }

            public string Name { get; }
            public List<AssertionFailure> Failures { get; }
            public int? OrderNumber { get; set; }
            public bool Finished { get; set; }
            public int LastCycleIndex { get; set; }
        }

        private readonly List<TestCaseResult> _finished = new List<TestCaseResult>();
        private readonly Dictionary<string, TestRecord> _records = new Dictionary<string, TestRecord>();
        private string _currentName;

        // Cycle index the suite is currently executing at. Defaults to 0 to
        // match today's single-pass runner; a future multi-cycle runner would
        // advance this between passes (TcXunit-k28.5, out-of-scope multi-cycle
        // loop tracked separately).
        protected int CurrentCycle { get; set; }

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

            if (_records.TryGetValue(name, out var existing))
            {
                if (existing.LastCycleIndex == CurrentCycle)
                    throw new NotSupportedException(
                        $"TEST('{name}') called twice in the same cycle — this is a real duplicate test name");

                // Re-declaration in a later cycle: re-attach to the existing
                // test rather than erroring, matching upstream AddTest.
                existing.LastCycleIndex = CurrentCycle;
                _currentName = name;
                return;
            }

            _records[name] = new TestRecord(name, CurrentCycle);
            _currentName = name;
        }

        // Declares (or re-attaches to) an ordered test. Returns TRUE only when
        // it's currently this test's turn and it hasn't finished yet - matching
        // upstream TEST_ORDERED() semantics for a single-pass run (TcXunit-k28.7).
        protected bool TEST_ORDERED(string name)
        {
            if (!_records.TryGetValue(name, out var record))
            {
                record = new TestRecord(name, CurrentCycle) { OrderNumber = _nextOrderNumber++ };
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
            // A re-declared test (TEST('X') again in a later cycle) can finish
            // more than once across cycles; replace its prior result rather
            // than appending a second entry for the same name (TcXunit-k28.5).
            var previousIndex = _finished.FindIndex(r => r.Name == record.Name);
            var result = new TestCaseResult(record.Name, record.Failures);
            if (previousIndex >= 0)
                _finished[previousIndex] = result;
            else
                _finished.Add(result);

            record.Finished = true;

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

        // Upstream delegates both through AssertEquals_BOOL(Expected:=TRUE/
        // FALSE, Actual:=Condition, Message) rather than failing with just a
        // bare message, so a failing AssertTrue/AssertFalse carries the same
        // EXP/ACT detail as any other assert (TcXunit-k28.3).
        protected void AssertTrue(bool condition, string message) =>
            AssertEquals_BOOL(true, condition, message);

        protected void AssertFalse(bool condition, string message) =>
            AssertEquals_BOOL(false, condition, message);

        // Table-driven scalar dispatch (TcXunit-gd2.11): each named
        // AssertEquals_<TYPE> method below is kept as the real, compile-time
        // API surface (C# fixtures under tests/TcXunit.Runner.Tests/Fakes/
        // extend FB_TestSuite directly and call these by name), but each is
        // now a 1-line forward into this one generic method, which looks up
        // compare/format behavior from the ScalarAssertType registry instead
        // of every type re-implementing its own Fail()-on-mismatch method.
        protected void AssertEqualsScalar(string typeName, object expected, object actual, object delta, string message)
        {
            var type = ScalarAssertType.Registry[typeName];
            if (!type.AreEqual(expected, actual, delta))
                Fail(type.FormatExpected(expected, delta), type.FormatActual(actual), message);
        }

        protected void AssertEquals_INT(int expected, int actual, string message) =>
            AssertEqualsScalar("INT", expected, actual, null, message);

        protected void AssertEquals_BOOL(bool expected, bool actual, string message) =>
            AssertEqualsScalar("BOOL", expected, actual, null, message);

        protected void AssertEquals_STRING(string expected, string actual, string message) =>
            AssertEqualsScalar("STRING", expected, actual, null, message);

        protected void AssertEquals_REAL(double expected, double actual, double delta, string message) =>
            AssertEqualsScalar("REAL", expected, actual, delta, message);

        protected void AssertEquals_LREAL(double expected, double actual, double delta, string message) =>
            AssertEqualsScalar("LREAL", expected, actual, delta, message);

        // Integer-family types (TcXunit-gd2.1): each is a 1-line forward
        // into AssertEqualsScalar, matching the INT/BOOL/STRING/REAL pattern
        // above. Parameter CLR types mirror the natural .NET type for each
        // IEC type's range/signedness; ScalarAssertType.AsLong64/AsULong64
        // widen whatever boxed shape arrives (this or an interpreted Cell's
        // own boxing per IecNumericType.cs) before narrowing/comparing.
        protected void AssertEquals_BYTE(byte expected, byte actual, string message) =>
            AssertEqualsScalar("BYTE", expected, actual, null, message);

        protected void AssertEquals_SINT(sbyte expected, sbyte actual, string message) =>
            AssertEqualsScalar("SINT", expected, actual, null, message);

        protected void AssertEquals_USINT(byte expected, byte actual, string message) =>
            AssertEqualsScalar("USINT", expected, actual, null, message);

        protected void AssertEquals_WORD(ushort expected, ushort actual, string message) =>
            AssertEqualsScalar("WORD", expected, actual, null, message);

        protected void AssertEquals_UINT(ushort expected, ushort actual, string message) =>
            AssertEqualsScalar("UINT", expected, actual, null, message);

        protected void AssertEquals_DINT(int expected, int actual, string message) =>
            AssertEqualsScalar("DINT", expected, actual, null, message);

        protected void AssertEquals_DWORD(uint expected, uint actual, string message) =>
            AssertEqualsScalar("DWORD", expected, actual, null, message);

        protected void AssertEquals_UDINT(uint expected, uint actual, string message) =>
            AssertEqualsScalar("UDINT", expected, actual, null, message);

        protected void AssertEquals_LINT(long expected, long actual, string message) =>
            AssertEqualsScalar("LINT", expected, actual, null, message);

        protected void AssertEquals_LWORD(ulong expected, ulong actual, string message) =>
            AssertEqualsScalar("LWORD", expected, actual, null, message);

        protected void AssertEquals_ULINT(ulong expected, ulong actual, string message) =>
            AssertEqualsScalar("ULINT", expected, actual, null, message);
    }
}
