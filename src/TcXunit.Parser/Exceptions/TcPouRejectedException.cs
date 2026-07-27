using System;

namespace TcXunit.Parser
{
    public sealed class TcPouRejectedException : Exception
    {
        public TcPouRejectedException(string message) : base(message)
        {
        }
    }
}
