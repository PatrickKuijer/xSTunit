using System;

namespace xStunit.Parser
{
    /// <summary>
    /// A well-formed .TcPOU whose ST body uses a construct outside the
    /// parser's supported subset - as opposed to a structural failure
    /// (malformed XML, missing element), which is what
    /// <see cref="StructuralParseGuard"/> handles. That guard deliberately
    /// lets this type through so callers can report it their own way.
    /// </summary>
    public sealed class TcPouRejectedException : Exception
    {
        public TcPouRejectedException(string message) : base(message)
        {
        }
    }
}
