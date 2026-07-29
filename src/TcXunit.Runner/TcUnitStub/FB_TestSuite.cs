using System;
using System.Collections.Generic;
using System.Diagnostics;

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
                Stopwatch = new Stopwatch();
            }

            public string Name { get; }
            public List<AssertionFailure> Failures { get; }
            public int? OrderNumber { get; set; }
            public bool Finished { get; set; }
            public int LastCycleIndex { get; set; }

            // Per-test elapsed time (TcXunit-6fb.1): started when TEST()/
            // TEST_ORDERED() opens this record's bracket, stopped in the
            // shared FinishRecord. Restart() (not Start()) so a re-declared
            // test in a later cycle (see class-level comment) times only its
            // most recent open->finish span, matching how FinishRecord
            // replaces rather than appends its TestCaseResult.
            public Stopwatch Stopwatch { get; }
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
                existing.Stopwatch.Restart();
                _currentName = name;
                return;
            }

            var record = new TestRecord(name, CurrentCycle);
            record.Stopwatch.Restart();
            _records[name] = record;
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

        // TcXunit-3tx.3: whether a TEST()/TEST_FINISHED() bracket is currently
        // open, i.e. whether there is a test to charge an escaping fault to.
        // The engine asks this before deciding between failing one test and
        // failing the whole suite.
        internal bool HasOpenTest => _currentName != null;

        // Charges a fault that unwound out of the current test's body to that
        // test and closes its bracket, so the suite can carry on with the tests
        // after it (TcXunit-3tx.3).
        //
        // Unlike Fail(), this deliberately records even when the test already
        // has an assertion failure: upstream's first-failure-wins rule is about
        // several asserts in one test, whereas a fault ENDED the test and is
        // strictly the more informative of the two.
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

            // A re-declared test (TEST('X') again in a later cycle) can finish
            // more than once across cycles; replace its prior result rather
            // than appending a second entry for the same name (TcXunit-k28.5).
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

            // TcXunit-3tx.2: the same three substrings the line above embeds,
            // plus the assert's name and source location, recorded as fields so
            // a consumer never has to parse the line back apart. CurrentAssert/
            // CurrentSite are whatever the caller last announced - both stay at
            // their defaults for a C# fixture calling these asserts directly,
            // which reports as nulls rather than as invented values.
            failures.Add(new AssertionFailure(formatted, CurrentAssert, expected, actual, message, CurrentSite));
        }

        // TcXunit-3tx.2: the name and source location of the assert currently
        // being evaluated, announced by the interpreter immediately before it
        // dispatches the call (see TcUnitSuiteHost/Engine.Invocation). They are
        // set on this object rather than threaded through every assert
        // signature because each of the ~40 AssertEquals_<TYPE> entry points
        // would otherwise have to carry - and forward - four more parameters
        // that none of them uses.
        internal string CurrentAssert { get; set; }

        internal AssertSite CurrentSite { get; set; }

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

        protected void AssertEquals_WSTRING(string expected, string actual, string message) =>
            AssertEqualsScalar("WSTRING", expected, actual, null, message);

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

        // TIME/LTIME (TcXunit-gd2.3): durations compare exactly (no Delta
        // param), matching upstream - mirrors the DINT/DWORD-style
        // exact-match forward shape, not REAL/LREAL's delta-based one.
        protected void AssertEquals_TIME(uint expected, uint actual, string message) =>
            AssertEqualsScalar("TIME", expected, actual, null, message);

        protected void AssertEquals_LTIME(ulong expected, ulong actual, string message) =>
            AssertEqualsScalar("LTIME", expected, actual, null, message);

        // Table-driven ARRAY[*] equality dispatch (TcXunit-gd2.6), the
        // array-typed counterpart to AssertEqualsScalar (gd2.11): one
        // dimension-agnostic generic method backs every AssertArrayEquals_
        // <TYPE> overload, reusing each type's compare/format delegate from
        // the ScalarAssertType registry instead of a bespoke per-type
        // method. This project doesn't reference TcXunit.Interpreter (the
        // reference points the other way, Interpreter -> Runner, so there's
        // no ArrayValue type here) - the caller (TcUnitSuiteHost) flattens
        // an ArrayValue into element/size/lower-bound primitives first.
        //
        // Mirrors upstream AssertArrayEquals_<TYPE> (FB_TestSuite.TcPOU): a
        // per-dimension size mismatch (not exact bounds - two arrays are
        // allowed to start at different lower bounds, per upstream's own
        // comment) fails immediately with a "SIZE = n" EXP/ACT pair and
        // never touches the elements; otherwise elements are compared
        // pairwise in flattened order, stopping at the first mismatch
        // (upstream's FOR/EXIT), and reporting "ARRAY[i] = value" using
        // each array's own real (lower-bound-relative) index - not the flat
        // position - exactly like upstream's ExpectedsIndex/ActualsIndex
        // pair.
        //
        // REAL/LREAL (TcXunit-gd2.7) additionally take a Delta arg, exactly
        // like upstream AssertArrayEquals_REAL/_LREAL's VAR_INPUT Delta -
        // an absolute per-element tolerance (ABS(Expecteds[i] - Actuals[i])
        // > Delta fails), not scaled/proportional to the expected value
        // despite what that method's own doc comment claims upstream.
        // Message formatting for a per-element mismatch uses FormatActual
        // (a plain value, no "+/- delta" suffix) for BOTH sides - matching
        // upstream's array assert message (REAL_TO_STRING(Expecteds[i]),
        // no delta shown), unlike the scalar AssertEquals_REAL/_LREAL
        // failure message which does include "+/- delta" via FormatExpected.
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
        // FlattenIndex: given a 0-based flat element position, recovers
        // each dimension's real (lower-bound-relative) index.
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

        // Type-erased AssertEquals(Expected: ANY, Actual: ANY, Message)
        // dispatcher (TcXunit-gd2.5, upstream FB_TestSuite.TcPOU ~line
        // 2215). Upstream compares the ANY parameters' TypeClass tags
        // first - a mismatch fails immediately (EXP/ACT show the two type
        // names, values are never compared) - and only once they agree
        // does it delegate to the matching AssertEquals_<TYPE>, always
        // with Delta := 0.0 for REAL/LREAL (this overload takes no delta
        // parameter, unlike AssertEquals_REAL/_LREAL). The interpreter has
        // no ANY value carrying its own runtime type tag the way a real
        // TwinCAT ANY struct's TypeClass/pValue/diSize does, so both type
        // names are resolved by the caller (from the argument *expression*,
        // mirroring how SIZEOF() resolves a declared type - see
        // Engine.Invocation.cs/NativeMethodBridge.cs) and handed to this
        // method already known.
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
