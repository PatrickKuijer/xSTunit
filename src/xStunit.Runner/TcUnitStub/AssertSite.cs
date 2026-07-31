namespace xStunit.Runner.TcUnitStub
{
    /// <summary>
    /// Where an assert is written in PLC source. Default(AssertSite) means "no
    /// location known" - what a C# fixture calling FB_TestSuite's asserts
    /// directly (rather than through interpreted ST) produces, and what the
    /// JSON reports as nulls.
    /// </summary>
    public struct AssertSite
    {
        /// <summary>Shares the 0-means-unknown convention of Stmt.Line and
        /// PlcSourceLocationException.UnknownLine.</summary>
        public const int UnknownLine = 0;

        public AssertSite(string pouTypeName, string methodName, int line, int bodyLine)
        {
            PouTypeName = pouTypeName;
            MethodName = methodName;
            Line = line;
            BodyLine = bodyLine;
        }

        public string PouTypeName { get; }

        /// <summary>Null when the assert sits in the POU's own top-level body,
        /// which has no method to name.</summary>
        public string MethodName { get; }

        /// <summary>The raw 1-based .TcPOU XML line.</summary>
        public int Line { get; }

        /// <summary>The 1-based line within the method/POU body - the number
        /// TwinCAT XAE's implementation editor shows.</summary>
        public int BodyLine { get; }

        public string Location => MethodName == null ? PouTypeName : PouTypeName + "." + MethodName;

        /// <summary>
        /// The single definition of the "location(line)" rendering: the
        /// interpreter's PlcCallStackFrame.LocationWithLine delegates here
        /// rather than repeating it, so a frame printed in an exception message
        /// and the same frame printed in a test failure can't drift apart.
        /// </summary>
        public string LocationWithLine =>
            BodyLine == UnknownLine ? Location : Location + "(" + BodyLine + ")";
    }
}
