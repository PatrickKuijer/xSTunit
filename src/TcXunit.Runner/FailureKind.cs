namespace TcXunit.Runner
{
    /// <summary>
    /// The machine-readable failure vocabulary shared by suite-level errors
    /// and per-test assertion failures (TcXunit-3tx.1/.2).
    /// </summary>
    /// <remarks>
    /// TcXunit is the verification step in an agentic edit/run/iterate loop, so
    /// the consumer of a failure is usually a model deciding what to change
    /// next. Before this, every failure class landed in the same untyped string
    /// and two opposite situations were indistinguishable: a genuine defect in
    /// the code under test, and valid IEC 61131-3 the interpreter has not grown
    /// support for yet. An agent that cannot tell them apart "fixes" the second
    /// by deleting correct code.
    ///
    /// Lives in the Runner project (not the Interpreter) because
    /// <see cref="TcUnitStub.AssertionFailure"/> uses the same constants and the
    /// dependency runs Interpreter -> Runner, never the other way.
    /// </remarks>
    public static class FailureKind
    {
        /// <summary>
        /// A TcUnit assertion said the value was wrong. The code under test
        /// (or the expectation) is what needs changing.
        /// </summary>
        public const string Assertion = "assertion";

        /// <summary>
        /// Interpreted ST faulted at run time - a call to a method that does
        /// not exist, an illegal narrowing conversion, a bad cast. A real
        /// defect in the code under test.
        /// </summary>
        public const string PlcFault = "plc-fault";

        /// <summary>
        /// The ST is valid and TwinCAT would compile it; TcXunit's hand-rolled
        /// interpreter does not implement it yet. STOP - escalate to a human
        /// and never rewrite the POU to make this "pass".
        /// </summary>
        public const string UnsupportedConstruct = "unsupported-construct";

        /// <summary>
        /// The failure happened before or outside any interpreted body -
        /// discovery, parsing or instantiation. Nothing ran, so nothing about
        /// the code under test has been established either way.
        /// </summary>
        public const string LoadError = "load-error";
    }
}
