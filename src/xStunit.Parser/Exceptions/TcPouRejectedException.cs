using System;

namespace xStunit.Parser
{
    public sealed class TcPouRejectedException : Exception
    {
        public TcPouRejectedException(string message) : base(message)
        {
        }
    }
}
