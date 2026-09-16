using System;

namespace xStunit.Parser
{
    /// <summary>
    /// A .TcPOU the parser understood and will not run: its body uses a
    /// construct outside the supported subset, or is written in a language
    /// other than ST. As opposed to a structural failure - a file that is
    /// broken rather than unsupported - which is what
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
