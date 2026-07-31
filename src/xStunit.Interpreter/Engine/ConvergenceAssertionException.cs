using System;

namespace xStunit.Interpreter
{
    // Thrown by AssertConverges/AssertConvergesAndLatches on non-convergence
    // (TcXunit-w5x.15.9) - distinct from TcUnit's Fail()-recorded assertions
    // since T6's design calls for an immediate, actionable diagnosis rather
    // than a queued per-test failure.
    public sealed class ConvergenceAssertionException : Exception
    {
        public ConvergenceAssertionException(string message) : base(message)
        {
        }
    }
}
