using System;

namespace xStunit.Interpreter
{
    // The FB_init arguments of an instance declaration cannot be bound. Its own
    // type so that global initialisation, which retries failed declarations,
    // can tell a structural error that will never resolve from a value that
    // is merely not ready yet.
    internal sealed class FbInitArgumentException : InvalidOperationException
    {
        public FbInitArgumentException(string message, Exception inner = null)
            : base(message, inner)
        {
        }
    }
}
