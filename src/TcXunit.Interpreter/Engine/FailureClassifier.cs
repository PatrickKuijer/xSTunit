using System;
using TcXunit.Runner;

namespace TcXunit.Interpreter
{
    // Maps an exception that escaped a run onto the FailureKind vocabulary
    // (TcXunit-3tx.1). One place, so the CLI's JSON, its text output and any
    // future consumer can never disagree about what a given failure means.
    public static class FailureClassifier
    {
        // Returns the FailureKind constant for ex, and the named construct when
        // the throw site knew one (null otherwise).
        //
        // unsupported-construct is claimed ONLY for an UnsupportedConstructException,
        // never inferred from a base type. The engine throws plain
        // NotSupportedException for both real gaps ("Statement type X not
        // supported") and real defects in the code under test ("Operator '<' is
        // not supported between Int32 and String"), and the ST front end throws
        // FormatException for source it can't read whether that source is
        // malformed or merely beyond the grammar - so neither type is evidence
        // of anything on its own. Widening this to a base type would report PLC
        // bugs as "stop, escalate to a human", which is this discriminator's
        // own failure mode inverted. A throw site opts in by name instead; see
        // UnsupportedConstructException.
        public static string Classify(Exception ex, out string construct)
        {
            construct = null;
            if (ex == null)
                return FailureKind.LoadError;

            // PlcSourceLocationException is a pure wrapper (see its own
            // remarks): it says WHERE, never WHAT, so classify the exception it
            // wraps. Loop rather than a single unwrap so a doubly-wrapped
            // exception can't slip through as plc-fault.
            var located = false;
            while (ex is PlcSourceLocationException wrapper && wrapper.InnerException != null)
            {
                located = true;
                ex = wrapper.InnerException;
            }

            switch (ex)
            {
                case UnsupportedConstructException unsupported:
                    construct = unsupported.Construct;
                    return FailureKind.UnsupportedConstruct;

                case ConvergenceAssertionException _:
                    return FailureKind.Assertion;
            }

            // Some interpreted body claimed this fault, and nothing above
            // reclassified it: the ST under test really did misbehave.
            return located ? FailureKind.PlcFault : FailureKind.LoadError;
        }
    }
}
