using System;

namespace TcXunit.Interpreter
{
    // The ST front end could not read the text it was given: the lexer, the
    // recursive-descent parser, or one of the literal parsers they delegate to
    // (TIME/DATE/DATE_AND_TIME/TIME_OF_DAY).
    //
    // Exists so FailureClassifier can name parse-error BY TYPE rather than by
    // catching every FormatException in the process (TcXunit-g14q). The engine
    // makes ~31 Convert.To* calls while running interpreted ST, each one a
    // STRING operand away from a FormatException that is a genuine defect in
    // the code under test - a plc-fault. Classifying on the base type reported
    // those as parse-error, whose guidance says "STOP and escalate if it looks
    // like valid ST": an agent told to page a human over its own bug, which is
    // the exact failure mode the kind vocabulary exists to prevent. Origin is
    // the only thing that separates the two, and the front end is the only
    // place that knows it, so the front end says so.
    //
    // Derives from FormatException so every existing catch (FormatException)
    // around the front end keeps working unchanged. Note that xUnit's
    // Assert.Throws<T> is exact-match, so front-end tests that pinned the base
    // type were retargeted to ParseFailure; Assert.ThrowsAny<FormatException>
    // would also have held.
    //
    // Message-compatible only, deliberately. Carrying the offending token and
    // position as structured fields - and retiring FailureClassifier's
    // message-scraping with them - is TcXunit-fpw8, not this change.
    public class ParseFailure : FormatException
    {
        public ParseFailure(string message)
            : base(message)
        {
        }
    }
}
