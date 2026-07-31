using System;

namespace xStunit.Interpreter
{
    // Thrown by AssertConverges/AssertConvergesAndLatches on non-convergence.
    // Deliberately thrown rather than recorded as a TcUnit Fail(): the
    // per-field diff is an immediate diagnosis, not a queued per-test failure.
    public sealed class ConvergenceAssertionException : Exception
    {
        public ConvergenceAssertionException(string message) : base(message)
        {
        }
    }
}
