using xStunit.Runner.TcUnitStub;

namespace xStunit.Interpreter
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
            Site = new AssertSite(pouTypeName, methodName, line, bodyLine);
        }

        // TcXunit-3tx.2/.3: the same four values, as the carrier the Runner
        // already defines. Stored rather than mirrored in four fields of this
        // type's own: the identical frame reaches a consumer either as part of
        // a PlcSourceLocationException's Message or as part of a per-test
        // failure's call stack, and one representation is what stops the two
        // from drifting into slightly different shapes for the same data.
        // The four properties below stay, unchanged, as this type's API.
        public AssertSite Site { get; }

        // POU type whose body was executing at this level, e.g. "FB_Deep".
        public string PouTypeName => Site.PouTypeName;

        // METHOD whose body was executing at this level - null when this
        // frame is a POU's own top-level body (a suite body, a bare-invoked
        // FB body, or a StepCycles cycle), which has no method to name.
        public string MethodName => Site.MethodName;

        // 1-based line in the originating .TcPOU file, or
        // PlcSourceLocationException.UnknownLine.
        public int Line => Site.Line;

        // 1-based line within the METHOD/action/POU body, i.e. the number
        // TwinCAT XAE's implementation editor shows for that body, or
        // PlcSourceLocationException.UnknownLine.
        public int BodyLine => Site.BodyLine;

        // TcXunit-7s6: "FB_Y.MethodZ", and the same with "(bodyLine)" folded
        // in when the line is known - defined once, on AssertSite.
        public string Location => Site.Location;

        public string LocationWithLine => Site.LocationWithLine;
    }
}
