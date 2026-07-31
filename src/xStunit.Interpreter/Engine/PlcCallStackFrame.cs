using xStunit.Runner.TcUnitStub;

namespace xStunit.Interpreter
{
    // One level of the interpreted call chain, captured as an exception
    // unwinds through nested ExecuteBody calls. PlcSourceLocationException
    // exposes an ordered list of these - innermost frame first, suite entry
    // point last - so consumers need no walk logic of their own.
    public sealed class PlcCallStackFrame
    {
        public PlcCallStackFrame(string pouTypeName, string methodName, int line, int bodyLine)
        {
            Site = new AssertSite(pouTypeName, methodName, line, bodyLine);
        }

        // The same frame reaches a consumer either through a
        // PlcSourceLocationException's Message or through a per-test failure's
        // call stack; storing the Runner's own carrier (rather than mirroring
        // its four values in fields here) is what keeps those two renderings
        // from drifting apart.
        public AssertSite Site { get; }

        public string PouTypeName => Site.PouTypeName;

        // Null when this frame is a POU's own top-level body (a suite body, a
        // bare-invoked FB body, or a StepCycles cycle) - there is no method to
        // name.
        public string MethodName => Site.MethodName;

        // 1-based line in the originating .TcPOU file, or
        // PlcSourceLocationException.UnknownLine.
        public int Line => Site.Line;

        // 1-based line within the METHOD/action/POU body, i.e. the number
        // TwinCAT XAE's implementation editor shows for that body, or
        // PlcSourceLocationException.UnknownLine.
        public int BodyLine => Site.BodyLine;

        public string Location => Site.Location;

        public string LocationWithLine => Site.LocationWithLine;
    }
}
