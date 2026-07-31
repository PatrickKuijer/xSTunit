using System.Collections.Generic;

namespace xStunit.Runner.TcUnitStub
{
    /// <summary>
    /// One recorded failure of a test case, in two flavours. Most are
    /// assertions - a comparison that came out wrong, with an expected/actual
    /// pair. The other is a fault that unwound out of a test method: it never
    /// compared anything, so it carries no Assert/Expected/Actual and has a
    /// <see cref="CallStack"/> instead.
    /// </summary>
    public sealed class AssertionFailure
    {
        public AssertionFailure(string message)
            : this(message, null, null, null, null, default(AssertSite))
        {
        }

        public AssertionFailure(
            string message,
            string assert,
            string expected,
            string actual,
            string assertMessage,
            AssertSite site)
        {
            Message = message;
            Kind = FailureKind.Assertion;
            Assert = assert;
            Expected = expected;
            Actual = actual;
            AssertMessage = assertMessage;
            Site = site;
        }

        private AssertionFailure(
            string message, string kind, string construct, AssertSite site, IReadOnlyList<AssertSite> callStack)
        {
            Message = message;
            Kind = kind;
            Construct = construct;
            Site = site;
            CallStack = callStack;
        }

        /// <summary>
        /// A fault that escaped the test's body and ended it, charged to that
        /// test rather than to the whole suite.
        /// </summary>
        public static AssertionFailure Fault(
            string message, string kind, string construct, AssertSite site, IReadOnlyList<AssertSite> callStack) =>
            new AssertionFailure(message, kind, construct, site, callStack);

        /// <summary>
        /// The human-readable line: for an assertion, formatted exactly as
        /// upstream TcUnit's FB_AdsAssertMessageFormatter does; for a fault,
        /// the located exception message.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// One of the <see cref="FailureKind"/> constants. Never null.
        /// </summary>
        public string Kind { get; }

        /// <summary>
        /// The unimplemented ST construct behind an unsupported-construct
        /// failure (e.g. "SEL"); null for every other kind.
        /// </summary>
        public string Construct { get; }

        /// <summary>
        /// The TcUnit assert that failed, e.g. "AssertEquals_INT". Null for a
        /// failure that no assert raised.
        /// </summary>
        public string Assert { get; }

        /// <summary>
        /// The compared values as the assert's own type rules format them -
        /// so REAL carries its "+/- delta" tolerance and an array assert
        /// carries "ARRAY[i] = v" - i.e. exactly the substrings
        /// <see cref="Message"/> embeds, without the prose around them.
        /// Null for a fault.
        /// </summary>
        public string Expected { get; }
        public string Actual { get; }

        /// <summary>
        /// The Message:= argument the suite author passed to the assert, on its
        /// own. Null or empty when the assert was called without one.
        /// </summary>
        public string AssertMessage { get; }

        /// <summary>
        /// Where the failure is: the assert's own source position, or for a
        /// fault the innermost interpreted frame. Default (all-null/zero) when
        /// no location is known - e.g. a C# fixture calling the asserts
        /// directly rather than through interpreted ST.
        /// </summary>
        public AssertSite Site { get; }

        /// <summary>
        /// For a fault, the full interpreted call chain that led to it,
        /// innermost frame first (CallStack[0] is <see cref="Site"/>) and the
        /// test's own body last. Null for an assertion failure, which needs no
        /// chain - its Site is where it is written.
        /// </summary>
        public IReadOnlyList<AssertSite> CallStack { get; }
    }
}
