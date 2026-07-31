namespace xStunit.Runner
{
    /// <summary>
    /// The machine-readable failure vocabulary shared by suite-level errors
    /// and per-test assertion failures.
    /// </summary>
    /// <remarks>
    /// The consumer of a failure is usually a model deciding what to change
    /// next, and two opposite situations must not look alike: a genuine defect
    /// in the code under test, and valid IEC 61131-3 the interpreter has not
    /// grown support for yet. An agent that cannot tell them apart "fixes" the
    /// second by deleting correct code.
    ///
    /// Lives in the Runner project (not the Interpreter) because
    /// <see cref="TcUnitStub.AssertionFailure"/> uses the same constants and the
    /// dependency runs Interpreter -> Runner, never the other way.
    /// </remarks>
    public static class FailureKind
    {
        public const string Assertion = "assertion";

        /// <summary>
        /// Interpreted ST faulted at run time - a real defect in the code under
        /// test, not a gap in the interpreter.
        /// </summary>
        public const string PlcFault = "plc-fault";

        /// <summary>
        /// Valid ST that TwinCAT compiles and this interpreter does not
        /// implement yet. Escalate to a human; never rewrite the POU to make
        /// it "pass".
        /// </summary>
        public const string UnsupportedConstruct = "unsupported-construct";

        /// <summary>
        /// The failure happened at CONTAINER level, before or outside any
        /// interpreted body. Nothing ran, so nothing about the code under test
        /// has been established either way.
        /// </summary>
        /// <remarks>
        /// Body text that can't be read is <see cref="ParseError"/>, not this:
        /// a suite whose sibling tests did run and did report verdicts
        /// contradicts "nothing ran".
        /// </remarks>
        public const string LoadError = "load-error";

        /// <summary>
        /// The ST front end could not read a body at all, so the body never
        /// ran. Either the source uses ST beyond this interpreter's subset or
        /// it is genuinely invalid ST - the front end cannot tell which, and
        /// neither can this kind, which is what it says out loud.
        /// </summary>
        /// <remarks>
        /// Distinct from <see cref="UnsupportedConstruct"/>, which is claimed
        /// only where a throw site NAMES the construct it doesn't implement;
        /// parse-error is the shrinking residue as the parser learns to
        /// recognize more constructs by name. See FailureClassifier for the
        /// structural rule.
        ///
        /// Distinct from <see cref="LoadError"/> because the container loaded
        /// fine: one body inside a loaded suite failed, so it fails a test
        /// (exit 1) and sibling tests still report their own verdicts.
        /// </remarks>
        public const string ParseError = "parse-error";

        /// <summary>
        /// What to DO about a failure of the given kind, carried in the
        /// failure's own message rather than left to the README - the consumer
        /// is usually a model reading one JSON object. A purely factual message
        /// ("Unexpected character '@' at position 33") leaves the two opposite
        /// responses (rewrite the POU vs. stop and escalate) exactly as
        /// ambiguous as the untyped error string this vocabulary replaced.
        /// </summary>
        /// <returns>
        /// Null for an unknown or null kind, rather than throwing - a missing
        /// sentence must never turn a reported failure into a second failure.
        /// </returns>
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
