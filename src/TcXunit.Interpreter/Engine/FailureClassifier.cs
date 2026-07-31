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
                // ParseFailure, and that is now its own kind - LOCATED OR NOT.
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
                // instead of fixing it. Everything that is not a ParseFailure
                // falls through to the located/unlocated split below.
                case ParseFailure _:
                    construct = OffendingToken(ex.Message);
                    return FailureKind.ParseError;
            }

            // Some interpreted body claimed this fault, and nothing above
            // reclassified it: the ST under test really did misbehave.
            return located ? FailureKind.PlcFault : FailureKind.LoadError;
        }

        // The token a parse-error is about, for the `construct` field
        // (TcXunit-229.15) - deliberately the SAME field unsupported-construct
        // uses, so `kind` + `construct` is one vocabulary at every level of the
        // JSON instead of a second field nobody would know to read.
        //
        // Recovered from the front end's own message text because ParseFailure
        // is message-compatible only for now; carrying the token and position as
        // structured fields, and retiring this scraping with them, is
        // TcXunit-fpw8. Best effort by construction: null when the message names
        // no token, which is a fine answer - `construct` is already nullable for
        // every other kind.
        public static string OffendingToken(string message)
        {
            if (string.IsNullOrEmpty(message))
                return null;

            // Lexer shape: "... '<token>' at position N in: <body>", and the
            // literal parsers' "... '<literal>' ...". First quoted run wins.
            var open = message.IndexOf('\'');
            if (open >= 0)
            {
                var close = message.IndexOf('\'', open + 1);
                if (close > open + 1)
                    return message.Substring(open + 1, close - open - 1);
            }

            // Parser shapes: "Expected X but got <Type>:<Text> at token index N"
            // and "Unexpected token <Type>:<Text> at index N" - Token.ToString()
            // is "Type:Text", so the text after the colon is the source token.
            var token = AfterMarker(message, "but got ") ?? AfterMarker(message, "Unexpected token ");
            if (token == null)
                return null;

            var colon = token.IndexOf(':');
            return colon >= 0 && colon + 1 < token.Length ? token.Substring(colon + 1) : token;
        }

        // The 1-based line WITHIN THE BODY the front end failed to read, or
        // PlcSourceLocationException.UnknownLine (0) when the message doesn't
        // say (TcXunit-229.15).
        //
        // A parse failure happens before any statement runs, so no Stmt.Line
        // exists for ExecuteBody to stamp - which is why a parse-error's frame
        // carries no line of its own. The lexer's message does carry the offset
        // and the body text it was reading, so the line is recoverable here,
        // and "open line N" is the whole of this kind's guidance. The parser's
        // messages carry a token index rather than an offset; those degrade to
        // "unknown" and the guidance drops the line, rather than citing a
        // number that would be wrong.
        public static int ParseErrorBodyLine(string message)
        {
            if (string.IsNullOrEmpty(message))
                return PlcSourceLocationException.UnknownLine;

            const string positionMarker = " at position ";
            const string textMarker = " in: ";

            var positionAt = message.IndexOf(positionMarker, StringComparison.Ordinal);
            if (positionAt < 0)
                return PlcSourceLocationException.UnknownLine;

            var numberAt = positionAt + positionMarker.Length;
            var textAt = message.IndexOf(textMarker, numberAt, StringComparison.Ordinal);
            if (textAt < 0)
                return PlcSourceLocationException.UnknownLine;

            if (!int.TryParse(message.Substring(numberAt, textAt - numberAt), out var offset) || offset < 0)
                return PlcSourceLocationException.UnknownLine;

            var body = message.Substring(textAt + textMarker.Length);
            if (offset > body.Length)
                return PlcSourceLocationException.UnknownLine;

            var line = 1;
            for (var i = 0; i < offset; i++)
            {
                if (body[i] == '\n')
                    line++;
            }

            return line;
        }

        private static string AfterMarker(string message, string marker)
        {
            var at = message.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0)
                return null;

            var rest = message.Substring(at + marker.Length);
            var end = rest.IndexOf(" at ", StringComparison.Ordinal);
            rest = (end >= 0 ? rest.Substring(0, end) : rest).Trim();
            return rest.Length > 0 ? rest : null;
        }
    }
}
