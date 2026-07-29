using System;

namespace TcXunit.Interpreter
{
    // Wraps whatever an interpreted ST body threw with the PLC source location
    // that was executing at throw time (TcXunit-p3t.1). Before this, a failure
    // like "Implicit narrowing from LREAL to REAL is not allowed" reached
    // CliRunner with only the suite name plus a .NET stack trace of interpreter
    // internals (Engine.Statements.cs:256) - nothing said which POU/method of
    // the PLC code under test was running.
    //
    // Deliberately a *wrapper*, never a replacement: the original exception is
    // kept verbatim as InnerException (type and message both), because callers
    // - CliRunner, SuiteCaseRunner, tests - switch on concrete interpreter
    // exception types. Engine wraps exactly once, at the outermost (suite)
    // boundary, so PouTypeName/MethodName describe the INNERMOST interpreted
    // body that faulted rather than whichever frame happened to be unwinding.
    public sealed class PlcSourceLocationException : Exception
    {
        // Sentinel for "no line information" - matches Stmt.Line/Expr.Line's
        // own 0-means-unknown convention (TcXunit-p3t.2).
        public const int UnknownLine = 0;

        public PlcSourceLocationException(string pouTypeName, string methodName, Exception innerException)
            : this(pouTypeName, methodName, UnknownLine, UnknownLine, innerException)
        {
        }

        // TcXunit-gfs: two independent line numbers, both 1-based, both using
        // the same UnknownLine sentinel - fileLine is the raw .TcPOU XML line
        // (this type's original Line contract, TcXunit-p3t.4); bodyLine is the
        // XAE-implementation-editor-relative line, the one a human actually
        // wants in a log line (see FormatMessage/Message).
        public PlcSourceLocationException(string pouTypeName, string methodName, int fileLine, int bodyLine, Exception innerException)
            : base(FormatMessage(pouTypeName, methodName, bodyLine, innerException), innerException)
        {
            PouTypeName = pouTypeName;
            MethodName = methodName;
            Line = fileLine;
            BodyLine = bodyLine;
        }

        // POU type whose body was executing, e.g. "FB_DeepHelper". Never null.
        public string PouTypeName { get; }

        // METHOD whose body was executing, e.g. "Level3" - null when the fault
        // came from the POU's own top-level body (a suite body, a bare-invoked
        // FB body, or a StepCycles cycle), which has no method to name.
        public string MethodName { get; }

        // The 1-based line *in the originating .TcPOU file* (not within the
        // body), so consumers never need to know about the BodyStartLine +
        // node.Line - 1 arithmetic that produces it (TcXunit-p3t.4). Kept for
        // structured (JSON) consumers that open the .TcPOU file directly
        // rather than through XAE (TcXunit-gfs) - it is no longer what
        // Message embeds; see BodyLine for that.
        // UnknownLine when the faulting statement carried no line - a
        // hand-built AST, or any body that never went through the ST parser.
        public int Line { get; }

        // TcXunit-gfs: the 1-based line *within the METHOD/action/POU body*,
        // i.e. the same number TwinCAT XAE's implementation editor shows for
        // that body - what FormatMessage/Message now embed, replacing the raw
        // .TcPOU file line as the human-facing number. UnknownLine under the
        // same conditions as Line.
        public int BodyLine { get; }

        // "FB_Y.MethodZ", or just "FB_Y" for a POU body - the location without
        // the line. Message is what log lines want (it folds the line in when
        // there is one); this stays line-free for callers that need the two
        // parts apart.
        public string Location => MethodName == null ? PouTypeName : PouTypeName + "." + MethodName;

        private static string FormatMessage(string pouTypeName, string methodName, int bodyLine, Exception innerException)
        {
            var location = methodName == null ? pouTypeName : pouTypeName + "." + methodName;
            if (bodyLine != UnknownLine)
                location += "(" + bodyLine + ")";
            return location + ": " + innerException?.Message;
        }
    }
}
