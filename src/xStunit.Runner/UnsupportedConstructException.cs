using System;

namespace xStunit.Runner
{
    /// <summary>
    /// A deliberate grow-on-demand gap: ST that TwinCAT compiles and TcXunit
    /// does not implement yet (TcXunit-3tx.1).
    /// </summary>
    /// <remarks>
    /// Distinct from every other throw here because the correct response is the
    /// opposite one: the code under test is fine and must not be edited.
    ///
    /// This is the ONLY signal <see cref="FailureKind.UnsupportedConstruct"/> is
    /// derived from, and that is deliberate. A plain
    /// <see cref="NotSupportedException"/> is not enough evidence: the engine
    /// throws it both for real gaps ("Statement type X not supported") AND for
    /// genuine defects in the code under test ("Operator '&lt;' is not supported
    /// between Int32 and String"). Classifying on the base type would report
    /// real PLC bugs as "stop, escalate to a human" - the same failure this
    /// discriminator exists to prevent, only inverted. So a throw site opts in
    /// by name, and anything that has not opted in reports as an ordinary
    /// fault.
    ///
    /// Derives from <see cref="NotSupportedException"/> so every existing
    /// `catch (NotSupportedException)` and every test asserting that type keeps
    /// working unchanged.
    ///
    /// Lives in the Runner project alongside <see cref="FailureKind"/> - both
    /// are the shared failure vocabulary, and the dependency runs
    /// Interpreter -> Runner, so the Runner's own grow-on-demand throw
    /// (FB_TestSuite.AssertEqualsAny) can use it too.
    /// </remarks>
    public sealed class UnsupportedConstructException : NotSupportedException
    {
        public UnsupportedConstructException(string construct, string message)
            : base(message)
        {
            Construct = construct;
        }

        /// <summary>
        /// The ST construct that isn't implemented - a native call name
        /// ("SEL"), an operator, a statement form. Null when the throw site
        /// knows only that something was unsupported, not what to call it.
        /// </summary>
        public string Construct { get; }
    }
}
