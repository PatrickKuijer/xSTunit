namespace xStunit.Runner
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
        /// The failure happened at CONTAINER level, before or outside any
        /// interpreted body - the file or its XML couldn't be read, no suites
        /// were discovered, instantiation failed. Nothing ran, so nothing about
        /// the code under test has been established either way.
        /// </summary>
        /// <remarks>
        /// Deliberately narrowed by TcXunit-229.15: "parsing" used to be listed
        /// here, which made a body-level parse failure a load-error. It cannot
        /// live here - the sentence above says nothing ran, and a suite whose
        /// sibling tests did run and did report verdicts contradicts that. Body
        /// text that can't be read is <see cref="ParseError"/>.
        /// </remarks>
        public const string LoadError = "load-error";

        /// <summary>
        /// The ST front end (lexer/parser) could not read a body at all, so the
        /// body never ran. Either the source uses ST beyond TcXunit's hand-rolled
        /// subset, or it is genuinely invalid ST - the front end cannot tell
        /// which, and neither can this vocabulary, which is exactly what this
        /// kind says out loud (TcXunit-229.9/.15).
        /// </summary>
        /// <remarks>
        /// Distinct from <see cref="UnsupportedConstruct"/>, which is claimed
        /// only where a throw site NAMES the construct it doesn't implement.
        /// parse-error is the shrinking residue: as the parser learns to
        /// recognize a construct by name, that construct is promoted out of
        /// parse-error into unsupported-construct. See FailureClassifier for
        /// the structural rule.
        ///
        /// Distinct from <see cref="LoadError"/> because the container loaded
        /// fine: a parse-error is a failure of one body inside a suite that
        /// loaded, so it fails a test (exit 1), and sibling tests in the same
        /// suite still report their own verdicts.
        /// </remarks>
        public const string ParseError = "parse-error";

        /// <summary>
        /// The agent-facing guidance sentence for <paramref name="kind"/> -
        /// what to DO about a failure of that kind, in the failure's own
        /// message rather than in the README.
        /// </summary>
        /// <remarks>
        /// TcXunit-229.15: TcXunit's consumer is usually a model deciding what
        /// to change next, and it reads one JSON object - not this repo's
        /// documentation. A message that is only a factual string ("Unexpected
        /// character '@' at position 33") leaves the two opposite responses
        /// (rewrite the POU vs. stop and escalate) exactly as ambiguous as the
        /// untyped `error` string this vocabulary replaced. So the guidance is
        /// carried, per kind, in the message itself.
        ///
        /// One function so the CLI's JSON, its text output and the README can
        /// never disagree about what a kind means. Returns null for an unknown
        /// or null kind rather than throwing - a missing sentence must never
        /// turn a reported failure into a second failure.
        /// </remarks>
        public static string Guidance(string kind)
        {
            switch (kind)
            {
                case Assertion:
                    return "An assert compared values and they differed: fix the code under test, " +
                        "or fix the expectation if the expectation is what's wrong.";
                case PlcFault:
                    return "The interpreted ST faulted at run time - a real defect in the code under " +
                        "test. Fix the code under test; this is not a TcXunit limitation.";
                case UnsupportedConstruct:
                    return "This is valid IEC 61131-3 that TwinCAT compiles and TcXunit does not " +
                        "implement yet. STOP: escalate it as a grammar gap. Never rewrite the POU, " +
                        "and never delete the construct, to make this pass.";
                case LoadError:
                    return "Nothing ran, so nothing about the code under test has been established. " +
                        "Check the file, the path, the .TcPOU XML and suite discovery - this is not " +
                        "about the code under test.";
                case ParseError:
                    return "Open the cited line. Fix it if it is genuinely malformed ST; if it looks " +
                        "like valid ST, STOP and escalate it as a grammar gap - never rewrite the POU " +
                        "to make this pass.";
                default:
                    return null;
            }
        }
    }
}
