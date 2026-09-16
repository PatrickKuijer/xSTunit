using System;
using System.Collections.Generic;
using xStunit.Interpreter.Extensibility;

namespace xStunit.SystemLibraryPlugins
{
    // Tc2_System's FB_IecCriticalSection:
    //   METHOD Enter : BOOL
    //   METHOD Leave : BOOL
    //
    // WHAT THIS DOES AND DOES NOT MODEL
    //
    // On a real system the point of this FB is mutual exclusion between tasks:
    // Enter blocks the calling task while another holds the section, and the
    // TwinCAT scheduler decides who proceeds. xStunit runs one interpreted task
    // and has no scheduler, so there is no second task to be blocked by and
    // Enter can never fail for the documented reason.
    //
    // What remains, and what a suite can actually test, is the bookkeeping: a
    // POU that leaves a section it never entered, or that returns from a path
    // without leaving one it did, is a real defect visible without any
    // concurrency at all. That is what this reproduces - Leave answers FALSE
    // when the section was not held, exactly as the vendor documents for "the
    // section wasn't previously entered".
    //
    // Nesting is counted rather than rejected: with a single task, an Enter
    // inside an Enter cannot deadlock here, and collapsing the pair would make
    // the inner Leave release the outer section - which would turn correct
    // nested code into a spurious failure.
    public sealed class CriticalSectionBlock : IXstunitNativeFunctionBlock
    {
        private int _depth;

        public string TypeName => "FB_IecCriticalSection";

        // No VAR_INPUT or VAR_OUTPUT at all: everything this FB says, it says
        // through its methods' return values.
        public IReadOnlyList<NativeFieldDeclaration> Fields => Array.Empty<NativeFieldDeclaration>();

        public IReadOnlyList<string> PositionalInputNames => Array.Empty<string>();

        public IReadOnlyList<string> MethodNames => new[] { "Enter", "Leave" };

        public IXstunitNativeFunctionBlock CreateInstance() => new CriticalSectionBlock();

        public object Invoke(NativeFunctionBlockCall call)
        {
            // A bare fbCs() call is not how this FB is used - it has no cyclic
            // behavior - but ST can write one, and silently doing nothing would
            // hide the mistake.
            if (call.IsBareInvocation)
            {
                throw new InvalidOperationException(
                    "FB_IecCriticalSection has no cyclic behavior - call Enter() and Leave(), " +
                    "not the block itself");
            }

            if (string.Equals(call.MethodName, "Enter", StringComparison.OrdinalIgnoreCase))
            {
                _depth++;
                return true;
            }

            if (_depth == 0)
                return false;

            _depth--;
            return true;
        }
    }
}
