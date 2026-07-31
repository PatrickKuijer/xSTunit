using System;

namespace xStunit.Parser
{
    /// <summary>
    /// Thrown when a .TcPOU's implementation text uses a construct outside
    /// the parser's supported subset, naming which construct and where.
    /// </summary>
    /// <remarks>
    /// Distinct from a structural parse failure (malformed XML, missing
    /// expected element): this is a well-formed .TcPOU whose ST body is
    /// simply not one this project's v1 subset accepts.
    /// <see cref="StructuralParseGuard"/> deliberately does not catch this
    /// type, leaving it to propagate to the caller's own dedicated handling.
    /// </remarks>
    public sealed class TcPouRejectedException : Exception
    {
        /// <summary>Constructs the exception with a message naming the rejected construct and its POU/method scope.</summary>
        public TcPouRejectedException(string message) : base(message)
        {
        }
    }
}
