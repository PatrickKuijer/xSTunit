using System;

namespace xStunit.Interpreter
{
    /// <summary>
    /// The ST front end (lexer, recursive-descent parser, or one of the
    /// literal parsers they delegate to for TIME/DATE/DATE_AND_TIME/
    /// TIME_OF_DAY) could not read the text it was given.
    /// </summary>
    /// <remarks>
    /// Exists so <see cref="FailureClassifier"/> can name parse-error BY TYPE
    /// rather than by catching every <see cref="FormatException"/> in the
    /// process. The engine makes ~31 <c>Convert.To*</c> calls
    /// while running interpreted ST, each one a STRING operand away from a
    /// <see cref="FormatException"/> that is a genuine defect in the code
    /// under test - a plc-fault. Classifying on the base type reported those
    /// as parse-error, whose guidance says "STOP and escalate if it looks
    /// like valid ST": an agent told to page a human over its own bug, which
    /// is the exact failure mode the kind vocabulary exists to prevent.
    /// Origin is the only thing that separates the two, and the front end is
    /// the only place that knows it, so the front end says so.
    ///
    /// Derives from <see cref="FormatException"/> so every existing
    /// <c>catch (FormatException)</c> around the front end keeps working
    /// unchanged. xUnit's <c>Assert.Throws&lt;T&gt;</c> is exact-match, so
    /// front-end tests that pinned the base type were retargeted to
    /// <see cref="ParseException"/>; <c>Assert.ThrowsAny&lt;FormatException&gt;</c>
    /// would also have held.
    ///
    /// <see cref="Token"/> and <see cref="BodyLine"/> are populated by the
    /// throw site itself from values it already has in hand (the character
    /// or token it rejected, the offset it was reading) rather than
    /// recovered later by re-parsing <see cref="Exception.Message"/> in
    /// <c>FailureClassifier</c>. A throw site with nothing to offer leaves
    /// either at its default (null,
    /// <see cref="PlcSourceLocationException.UnknownLine"/>) - every literal
    /// parser only ever sees the literal's own substring, never the body it
    /// sits in, so it can never derive a line.
    /// </remarks>
    public class ParseException : FormatException
    {
        /// <summary>
        /// Constructs a parse failure, optionally carrying the offending
        /// token and/or the 1-based line within the body where it occurred.
        /// </summary>
        /// <param name="message">Human-readable description of the failure.</param>
        /// <param name="token">
        /// The rejected token or character, for the `construct` field -
        /// the same field unsupported-construct uses, so `kind` +
        /// `construct` is one vocabulary at every level of the JSON. Null
        /// when the throw site names none.
        /// </param>
        /// <param name="bodyLine">
        /// The 1-based line within the body being read, or
        /// <see cref="PlcSourceLocationException.UnknownLine"/> when the
        /// throw site cannot derive one (every literal parser, and the
        /// recursive-descent parser's own token-index-based errors).
        /// </param>
        public ParseException(string message, string token = null, int bodyLine = PlcSourceLocationException.UnknownLine)
            : base(message)
        {
            Token = token;
            BodyLine = bodyLine;
        }

        /// <summary>
        /// The offending token or character, or null when the throw site
        /// named none.
        /// </summary>
        public string Token { get; }

        /// <summary>
        /// The 1-based line within the body being read, or
        /// <see cref="PlcSourceLocationException.UnknownLine"/> when it
        /// could not be derived at the throw site.
        /// </summary>
        public int BodyLine { get; }
    }
}
