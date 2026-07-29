namespace TcXunit.Interpreter
{
    // One level of the interpreted call chain, captured as an exception
    // unwinds through nested ExecuteBody calls (TcXunit-1am). A flat data
    // carrier rather than a linked structure: PlcSourceLocationException
    // exposes an ordered list of these - innermost frame first, suite entry
    // point last - so CLI/VSIX consumers don't need walk logic of their own.
    public sealed class PlcCallStackFrame
    {
        public PlcCallStackFrame(string pouTypeName, string methodName, int line, int bodyLine)
        {
            PouTypeName = pouTypeName;
            MethodName = methodName;
            Line = line;
            BodyLine = bodyLine;
        }

        // POU type whose body was executing at this level, e.g. "FB_Deep".
        public string PouTypeName { get; }

        // METHOD whose body was executing at this level - null when this
        // frame is a POU's own top-level body (a suite body, a bare-invoked
        // FB body, or a StepCycles cycle), which has no method to name.
        public string MethodName { get; }

        // 1-based line in the originating .TcPOU file, or
        // PlcSourceLocationException.UnknownLine.
        public int Line { get; }

        // 1-based line within the METHOD/action/POU body, i.e. the number
        // TwinCAT XAE's implementation editor shows for that body, or
        // PlcSourceLocationException.UnknownLine.
        public int BodyLine { get; }

        // "FB_Y.MethodZ", or just "FB_Y" for a POU body.
        public string Location => MethodName == null ? PouTypeName : PouTypeName + "." + MethodName;

        // TcXunit-7s6: Location with "(bodyLine)" folded in when the line is
        // known - the one place this formatting lives, shared by
        // PlcSourceLocationException.FormatMessage (the innermost frame's
        // Message) and CliRunner's per-frame console rendering, so the two
        // can't drift into slightly different shapes for the same data.
        public string LocationWithLine =>
            BodyLine == PlcSourceLocationException.UnknownLine ? Location : Location + "(" + BodyLine + ")";
    }
}
