using System;
using xStunit.Runner;

namespace xStunit.Interpreter
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
        // not supported between Int32 and String"), so that type is no evidence
        // of anything on its own. Widening this to a base type would report PLC
        // bugs as "stop, escalate to a human", which is this discriminator's
        // own failure mode inverted. A throw site opts in by name instead; see
        // UnsupportedConstructException.
        //
        // THE STRUCTURAL RULE (TcXunit-229.9/.15). Three kinds sit on one axis
        // and the boundaries between them are structural, not stylistic:
        //
        //   load-error           container level: the file, its XML, discovery
        //                        or instantiation failed. Nothing ran.
        //   parse-error          body level: the front end could not read this
        //                        body's text. This body never ran; the suite,
        //                        and its other tests, did.
        //   unsupported-construct  statement level: a throw site recognized the
        //                        construct BY NAME and says it isn't implemented.
        //
        // parse-error is therefore the shrinking residue, and it shrinks in one
        // direction only: a construct the parser learns to recognize by name is
        // PROMOTED out of parse-error into unsupported-construct. There is no
        // upfront superset grammar to sort this out in advance - TcXunit-229.5
        // keeps the hand-rolled, grow-on-demand parser - so the residue is
        // retired construct by construct, as each one is taught, and never by
        // guessing at the classifier.
        //
        // What this deliberately gives up: a FormatException cannot say whether
        // the source was beyond the subset or simply wrong, so parse-error
        // claims neither. Its guidance (FailureKind.Guidance) hands that one
        // judgement to the reader, with instructions for both answers - which is
        // honest, where picking one would be a coin flip presented as a verdict.
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

                // TcXunit-229.15: the ST front end (Lexer/Parser and the
                // literal parsers) reports every body it cannot read as a
                // ParseException, and that is now its own kind - LOCATED OR NOT.
                // It used to split on whether an ExecuteBody frame happened to
                // have stamped a location on the way out, which made the same
                // unreadable body a plc-fault when it was reached through a
                // call and a load-error when it wasn't. That distinction
                // described TcXunit's own call path, not the failure, and both
                // answers were wrong: nothing about the code under test was
                // established (so not plc-fault) and the container loaded fine
                // (so not load-error).
                //
                // TcXunit-g14q: matched by the front end's OWN type, never by
                // the FormatException base. FormatException is thrown all over
                // the process by things that are not the front end - every
                // Convert.To* the engine performs on interpreted values, and
                // ArrayTypeInfo.Parse during instantiation - and claiming those
                // as parse-error told an agent to escalate a genuine PLC defect
                // instead of fixing it. Everything that is not a ParseException
                // falls through to the located/unlocated split below.
                //
                // xstunit-fpw8: Token is stamped by the throw site itself, not
                // recovered here by re-parsing ex.Message - the message is
                // prose for a human, never a structured value a consumer
                // switches on.
                case ParseException parseError:
                    construct = parseError.Token;
                    return FailureKind.ParseError;
            }

            // Some interpreted body claimed this fault, and nothing above
            // reclassified it: the ST under test really did misbehave.
            return located ? FailureKind.PlcFault : FailureKind.LoadError;
        }

        // The ParseException behind ex, unwrapping PlcSourceLocationException
        // exactly as Classify does above, or null when ex is not (and does not
        // wrap) a ParseException. Lets a caller that already knows kind ==
        // FailureKind.ParseError reach the front end's own structured
        // BodyLine/Token without a second classification pass.
        public static ParseException UnwrapParseException(Exception ex)
        {
            while (ex is PlcSourceLocationException wrapper && wrapper.InnerException != null)
                ex = wrapper.InnerException;
            return ex as ParseException;
        }
    }
}
