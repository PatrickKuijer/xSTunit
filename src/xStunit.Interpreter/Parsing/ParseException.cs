using System;

namespace xStunit.Interpreter
{
    /// <summary>
    /// The ST front end (lexer, recursive-descent parser, or one of the
    /// literal parsers they delegate to for TIME/DATE/DATE_AND_TIME/
    /// TIME_OF_DAY) could not read the text it was given.
    /// </summary>
    /// <remarks>
    /// Exists so <see cref="FailureClassifier"/> can recognise a front-end
    /// failure BY TYPE. The engine makes many <c>Convert.To*</c> calls while
    /// running interpreted ST, each one a STRING operand away from a
    /// <see cref="FormatException"/> that is a genuine defect in the code
    /// under test - a plc-fault, not a parse-error. Origin is the only thing
    /// separating the two, and the front end is the only place that knows it.
    ///
    /// Still derives from <see cref="FormatException"/> so existing
    /// <c>catch (FormatException)</c> around the front end keeps working.
    /// Note xUnit's <c>Assert.Throws&lt;T&gt;</c> is exact-match, so a test
    /// pinning the base type must use <c>Assert.ThrowsAny</c>.
    ///
    /// <see cref="Token"/> and <see cref="BodyLine"/> are filled in by the
    /// throw site from values it already holds, rather than recovered later
    /// by re-parsing <see cref="Exception.Message"/>.
    /// </remarks>
    public class ParseException : FormatException
    {
        /// <param name="message">Human-readable description of the failure.</param>
        /// <param name="token">
        /// The rejected token or character, reported as the `construct`
        /// field - the same field unsupported-construct uses, so `kind` +
        /// `construct` is one vocabulary throughout the JSON. Null when the
        /// throw site names none.
        /// </param>
        /// <param name="bodyLine">
        /// The 1-based line within the body being read, or
        /// <see cref="PlcSourceLocationException.UnknownLine"/> when the
        /// throw site cannot derive one - a literal parser only ever sees the
        /// literal's own substring, never the body it sits in.
        /// </param>
        public ParseException(string message, string token = null, int bodyLine = PlcSourceLocationException.UnknownLine)
            : base(message)
        {
            Token = token;
            BodyLine = bodyLine;
        }

        /// <summary>Null when the throw site named no offending token.</summary>
        public string Token { get; }

        /// <summary>
        /// 1-based line within the body, or
        /// <see cref="PlcSourceLocationException.UnknownLine"/> when the
        /// throw site could not derive one.
        /// </summary>
        public int BodyLine { get; }
    }
}
