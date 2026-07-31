using System;
using System.Collections.Generic;

namespace xStunit.Interpreter
{
    // Wraps whatever an interpreted ST body threw with the PLC source location
    // that was executing at throw time.
    //
    // Deliberately a *wrapper*, never a replacement: the original exception is
    // kept verbatim as InnerException (type and message both), because callers
    // switch on concrete interpreter exception types. Engine wraps exactly
    // once, at the outermost (suite) boundary, so PouTypeName/MethodName
    // describe the INNERMOST interpreted body that faulted rather than
    // whichever frame happened to be unwinding.
    public sealed class PlcSourceLocationException : Exception
    {
        // Sentinel for "no line information" - matches Stmt.Line/Expr.Line's
        // own 0-means-unknown convention.
        public const int UnknownLine = 0;

        public PlcSourceLocationException(string pouTypeName, string methodName, Exception innerException)
            : this(pouTypeName, methodName, UnknownLine, UnknownLine, innerException)
        {
        }

        // fileLine and bodyLine are two independent 1-based numbers sharing the
        // UnknownLine sentinel; see the Line/BodyLine properties. Degrades to a
        // single-frame CallStack holding exactly the frame given here.
        public PlcSourceLocationException(string pouTypeName, string methodName, int fileLine, int bodyLine, Exception innerException)
            : this(pouTypeName, methodName, fileLine, bodyLine, null, innerException)
        {
        }

        // pouTypeName/methodName/fileLine/bodyLine are only the null-or-empty-
        // callStack fallback: whenever a callStack is present, the single-frame
        // properties are DERIVED from callStack[0] rather than taking their own
        // copies, so the two can't drift apart.
        public PlcSourceLocationException(
            string pouTypeName,
            string methodName,
            int fileLine,
            int bodyLine,
            IReadOnlyList<PlcCallStackFrame> callStack,
            Exception innerException)
            : this(
                callStack != null && callStack.Count > 0
                    ? callStack
                    : new[] { new PlcCallStackFrame(pouTypeName, methodName, fileLine, bodyLine) },
                innerException)
        {
        }

        private PlcSourceLocationException(IReadOnlyList<PlcCallStackFrame> callStack, Exception innerException)
            : base(callStack[0].LocationWithLine + ": " + innerException?.Message, innerException)
        {
            CallStack = callStack;
            PouTypeName = callStack[0].PouTypeName;
            MethodName = callStack[0].MethodName;
            Line = callStack[0].Line;
            BodyLine = callStack[0].BodyLine;
        }

        // Never null.
        public string PouTypeName { get; }

        // Null when the fault came from the POU's own top-level body (a suite
        // body, a bare-invoked FB body, or a StepCycles cycle), which has no
        // method to name.
        public string MethodName { get; }

        // The 1-based line *in the originating .TcPOU file* (not within the
        // body), for structured consumers that open the .TcPOU directly rather
        // than through XAE - the BodyStartLine + node.Line - 1 arithmetic is
        // already applied. UnknownLine when the faulting statement carried no
        // line: a hand-built AST, or any body that never went through the ST
        // parser.
        public int Line { get; }

        // The 1-based line *within the METHOD/action/POU body*, i.e. the number
        // TwinCAT XAE's implementation editor shows - the human-facing number,
        // and the one Message embeds. UnknownLine under the same conditions as
        // Line.
        public int BodyLine { get; }

        // "FB_Y.MethodZ", or just "FB_Y" for a POU body. Line-free, unlike
        // Message, which folds the line in when there is one.
        public string Location => MethodName == null ? PouTypeName : PouTypeName + "." + MethodName;

        // The interpreted call chain that led to this fault, innermost frame
        // first (CallStack[0] is what PouTypeName/MethodName/Line/BodyLine are
        // derived from) and the suite entry point last. Never null or empty -
        // degrades to a single frame when only one body was on the stack.
        public IReadOnlyList<PlcCallStackFrame> CallStack { get; }
    }
}
