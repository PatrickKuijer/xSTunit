using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace xStunit.Runner.TcUnitStub
{
    /// <summary>
    /// Native C# stand-in for TcUnit's FB_TestSuite. A suite POU calls
    /// TEST('name') / TEST_FINISHED() imperatively inside its cyclic body, so
    /// the case list is not known ahead of time - it is discovered by running
    /// the body.
    /// </summary>
    /// <remarks>
    /// Whether a repeated TEST('X') is an error depends on the cycle it lands
    /// in, mirroring upstream's CycleCount-keyed AddTest: a repeat in the SAME
    /// cycle is a genuine duplicate name; a repeat in a LATER cycle is the
    /// normal cyclic re-declaration and re-attaches to the existing test. No
    /// runner advances <see cref="CurrentCycle"/> today, so in practice every
    /// repeat is currently an error - the structure is here so a multi-cycle
    /// runner needs no changes in this class.
    /// </remarks>
    public abstract class FB_TestSuite
    {
        // Kept in _records after finishing rather than removed, so
        // IS_TEST_FINISHED('X') can still resolve 'X' once it completes.
        private sealed class TestRecord
        {
            public TestRecord(string name, int cycleIndex)
            {
                Name = name;
                Failures = new List<AssertionFailure>();
                LastCycleIndex = cycleIndex;
                Stopwatch = new Stopwatch();
            }

            public string Name { get; }
            public List<AssertionFailure> Failures { get; }
            public int? OrderNumber { get; set; }
            public bool Finished { get; set; }
            public int LastCycleIndex { get; set; }

            // Callers Restart() rather than Start() it, so a test re-declared
            // in a later cycle times only its most recent open->finish span -
            // matching how FinishRecord replaces rather than appends its
            // TestCaseResult.
            public Stopwatch Stopwatch { get; }
        }

        private readonly List<TestCaseResult> _finished = new List<TestCaseResult>();
        private readonly Dictionary<string, TestRecord> _records = new Dictionary<string, TestRecord>();
        private string _currentName;

        // Stays 0 under today's single-pass runner; a multi-cycle runner would
        // advance it between passes.
        protected int CurrentCycle { get; set; }

        // Mirrors upstream's per-suite NumberOfOrderedTests /
        // CurrentlyRunningOrderedTestInTestSuite, which are 1-based.
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

                // Re-declaration in a later cycle re-attaches to the existing
                // test rather than erroring, matching upstream AddTest.
                existing.LastCycleIndex = CurrentCycle;
                existing.Stopwatch.Restart();
                _currentName = name;
                return;
            }

            var record = new TestRecord(name, CurrentCycle);
            record.Stopwatch.Restart();
            _records[name] = record;
            _currentName = name;
        }

        // Declares (or re-attaches to) an ordered test, returning TRUE only
        // when it is this test's turn and it hasn't finished - the caller's
        // body is meant to run only then, matching upstream TEST_ORDERED().
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

            record.Stopwatch.Restart();
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

        // Finishes a named test regardless of which test is "current", for
        // multi-cycle/async bodies. An unknown name fails fast, upstream's
        // log-and-abort mechanic having no equivalent here.
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

        // An unknown name throws rather than returning FALSE the way upstream's
        // Tests[] scan does, so a suite-authoring typo surfaces instead of
        // reading as "not finished yet".
        protected bool IS_TEST_FINISHED(string name)
        {
            if (!_records.TryGetValue(name, out var record))
                throw new InvalidOperationException(
                    $"IS_TEST_FINISHED('{name}') called for a test never declared with TEST()/TEST_ORDERED()");

            return record.Finished;
        }

        // Whether there is an open bracket to charge an escaping fault to. The
        // engine asks before deciding between failing one test and failing the
        // whole suite.
        internal bool HasOpenTest => _currentName != null;

        // Charges a fault that unwound out of the current test's body to that
        // test and closes its bracket, so the suite carries on with the tests
        // after it.
        //
        // Unlike Fail(), records even when the test already has an assertion
        // failure: first-failure-wins is about several asserts in one test,
        // whereas a fault ENDED the test and is the more informative of the two.
        internal void AbortCurrentTest(AssertionFailure failure)
        {
            if (_currentName == null)
                throw new InvalidOperationException("AbortCurrentTest called with no open TEST() bracket");

            var record = _records[_currentName];
            record.Failures.Add(failure);
            FinishRecord(record);
            _currentName = null;
        }

        private void FinishRecord(TestRecord record)
        {
            record.Stopwatch.Stop();

            // A test re-declared in a later cycle finishes more than once;
            // replace its prior result rather than reporting the same name
            // twice.
            var previousIndex = _finished.FindIndex(r => r.Name == record.Name);
            var result = new TestCaseResult(record.Name, record.Failures, record.Stopwatch.ElapsedMilliseconds);
            if (previousIndex >= 0)
                _finished[previousIndex] = result;
            else
                _finished.Add(result);

            record.Finished = true;

            if (record.OrderNumber.HasValue && record.OrderNumber.Value == _currentOrderedTurn)
                _currentOrderedTurn++;
        }

        // First failure wins: a test with several failing asserts still fails,
        // but only the first one's message is recorded, so later Fail() calls
        // in the same bracket are dropped. Upstream's
        // FB_Test.SetAssertionMessage()/SetAssertionType() do the same by
        // writing only 'if not already set'.
        //
        // Message format mirrors upstream FB_AdsAssertMessageFormatter:
        // "FAILED TEST '<name>', EXP: <expected>, ACT: <actual>[, MSG: <message>]"
        // - MSG only when message is non-empty.
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

            // The same substrings the formatted line embeds, recorded as fields
            // so a consumer never has to parse that line back apart.
            // CurrentAssert/CurrentSite stay at their defaults for a C# fixture
            // calling these asserts directly, which reports as nulls rather
            // than as invented values.
            failures.Add(new AssertionFailure(formatted, CurrentAssert, expected, actual, message, CurrentSite));
        }

        // The name and source location of the assert being evaluated, which the
        // interpreter announces immediately before dispatching the call (see
        // SuiteHost/Engine.Invocation). Carried on the suite rather than
        // threaded through every assert signature: each of the ~40
        // AssertEquals_<TYPE> entry points would otherwise have to take - and
        // forward - four more parameters that none of them reads.
        internal string CurrentAssert { get; set; }

        internal AssertSite CurrentSite { get; set; }

        // Delegated to AssertEquals_BOOL rather than failing with a bare
        // message, so a failing AssertTrue/AssertFalse carries the same EXP/ACT
        // detail as any other assert - as upstream does.
        protected void AssertTrue(bool condition, string message) =>
            AssertEquals_BOOL(true, condition, message);

        protected void AssertFalse(bool condition, string message) =>
            AssertEquals_BOOL(false, condition, message);

        // Table-driven scalar dispatch. The named AssertEquals_<TYPE> methods
        // below stay as the compile-time API surface - C# fixtures extend
        // FB_TestSuite and call them by name - but each is a 1-line forward
        // into here, so compare/format behaviour lives once in the
        // ScalarAssertType registry instead of once per type.
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

        protected void AssertEquals_WSTRING(string expected, string actual, string message) =>
            AssertEqualsScalar("WSTRING", expected, actual, null, message);

        protected void AssertEquals_REAL(double expected, double actual, double delta, string message) =>
            AssertEqualsScalar("REAL", expected, actual, delta, message);

        protected void AssertEquals_LREAL(double expected, double actual, double delta, string message) =>
            AssertEqualsScalar("LREAL", expected, actual, delta, message);

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

        protected void AssertEquals_TIME(uint expected, uint actual, string message) =>
            AssertEqualsScalar("TIME", expected, actual, null, message);

        protected void AssertEquals_LTIME(ulong expected, ulong actual, string message) =>
            AssertEqualsScalar("LTIME", expected, actual, null, message);

        // The array counterpart to AssertEqualsScalar: one dimension-agnostic
        // method backing every AssertArrayEquals_<TYPE>, reusing the same
        // registry delegates. Takes flattened element/size/lower-bound
        // primitives because there is no ArrayValue type to take - the
        // reference runs Interpreter -> Runner, so the caller (SuiteHost)
        // flattens one first.
        //
        // Semantics mirror upstream AssertArrayEquals_<TYPE>: sizes are
        // compared per dimension but bounds are not, two arrays being allowed
        // to start at different lower bounds; a size mismatch fails with a
        // "SIZE = n" pair and never touches the elements; otherwise elements
        // are compared pairwise in flattened order and the first mismatch wins,
        // reported at each array's own lower-bound-relative index rather than
        // the flat position.
        //
        // Delta is an ABSOLUTE per-element tolerance, not one scaled to the
        // expected value - upstream's own doc comment claims otherwise, its
        // code does not. A per-element mismatch formats BOTH sides with
        // FormatActual, so no "+/- delta" appears here even though the scalar
        // AssertEquals_REAL/_LREAL message shows it.
        protected void AssertArrayEquals(
            string typeName,
            IReadOnlyList<int> expectedSizes, IReadOnlyList<int> expectedLowerBounds, object[] expectedElements,
            IReadOnlyList<int> actualSizes, IReadOnlyList<int> actualLowerBounds, object[] actualElements,
            string message, object delta = null)
        {
            var type = ScalarAssertType.Registry[typeName];

            var sizeEquals = expectedSizes.Count == actualSizes.Count;
            for (var d = 0; sizeEquals && d < expectedSizes.Count; d++)
                sizeEquals = expectedSizes[d] == actualSizes[d];

            if (!sizeEquals)
            {
                Fail($"SIZE = {string.Join("x", expectedSizes)}", $"SIZE = {string.Join("x", actualSizes)}", message);
                return;
            }

            for (var flat = 0; flat < expectedElements.Length; flat++)
            {
                if (type.AreEqual(expectedElements[flat], actualElements[flat], delta))
                    continue;

                var expectedIndex = UnflattenIndex(expectedSizes, expectedLowerBounds, flat);
                var actualIndex = UnflattenIndex(actualSizes, actualLowerBounds, flat);
                Fail(
                    $"ARRAY[{string.Join(",", expectedIndex)}] = {type.FormatActual(expectedElements[flat])}",
                    $"ARRAY[{string.Join(",", actualIndex)}] = {type.FormatActual(actualElements[flat])}",
                    message);
                return;
            }
        }

        // Reverses the row-major flattening in Engine.Statements.cs'
        // FlattenIndex, recovering each dimension's lower-bound-relative index
        // from a 0-based flat position.
        private static int[] UnflattenIndex(IReadOnlyList<int> sizes, IReadOnlyList<int> lowerBounds, int flat)
        {
            var offsets = new int[sizes.Count];
            for (var d = sizes.Count - 1; d >= 0; d--)
            {
                offsets[d] = flat % sizes[d];
                flat /= sizes[d];
            }

            var result = new int[sizes.Count];
            for (var d = 0; d < sizes.Count; d++)
                result[d] = lowerBounds[d] + offsets[d];
            return result;
        }

        // Type-erased AssertEquals(Expected: ANY, Actual: ANY, Message).
        // Type names arrive already resolved because the interpreter has no
        // ANY value carrying its own runtime tag the way a TwinCAT ANY struct's
        // TypeClass does - the caller resolves both from the argument
        // *expression*, the way SIZEOF() resolves a declared type.
        //
        // As upstream: a type mismatch fails on the type names alone and the
        // values are never compared, and a REAL/LREAL that gets past that
        // compares with Delta := 0.0, this overload having no delta parameter.
        protected void AssertEqualsAny(
            string expectedTypeName, object expectedValue,
            string actualTypeName, object actualValue,
            string message)
        {
            if (expectedTypeName != actualTypeName)
            {
                Fail(
                    $"(Type class = {expectedTypeName ?? "UNKNOWN"})",
                    $"(Type class = {actualTypeName ?? "UNKNOWN"})",
                    message);
                return;
            }

            if (!ScalarAssertType.Registry.ContainsKey(expectedTypeName ?? string.Empty))
                throw new UnsupportedConstructException(
                    expectedTypeName,
                    $"AssertEquals(ANY) doesn't support type '{expectedTypeName}' yet (grow-on-demand, TcXunit-gd2.5).");

            AssertEqualsScalar(expectedTypeName, expectedValue, actualValue, 0.0, message);
        }
    }
}
