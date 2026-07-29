namespace TcXunit.Runner.TcUnitStub
{
    /// <summary>
    /// Where an assert is written in PLC source (TcXunit-3tx.2).
    /// </summary>
    /// <remarks>
    /// A test method routinely contains several asserts; without a location,
    /// a reported failure says which TEST failed but not which assert, and a
    /// consumer has to re-read the POU and guess. The interpreter already
    /// tracks all four numbers per executing frame - this is the carrier that
    /// gets them from there onto the failure record.
    ///
    /// Default(AssertSite) means "no location known", which is what a C#
    /// fixture calling FB_TestSuite's asserts directly (rather than through
    /// interpreted ST) produces, and what the JSON reports as nulls.
    /// </remarks>
    public struct AssertSite
    {
        /// <summary>Line value meaning "unknown", matching Stmt.Line's and
        /// PlcSourceLocationException.UnknownLine's own 0-means-unknown
        /// convention.</summary>
        public const int UnknownLine = 0;

        public AssertSite(string pouTypeName, string methodName, int line, int bodyLine)
        {
            PouTypeName = pouTypeName;
            MethodName = methodName;
            Line = line;
            BodyLine = bodyLine;
        }

        /// <summary>POU type whose body contains the assert, e.g.
        /// "FB_CounterTests".</summary>
        public string PouTypeName { get; }

        /// <summary>METHOD containing the assert; null when it sits in the
        /// POU's own top-level body, which has no method to name.</summary>
        public string MethodName { get; }

        /// <summary>The raw 1-based .TcPOU XML line.</summary>
        public int Line { get; }

        /// <summary>The 1-based line within the method/POU body - the number
        /// TwinCAT XAE's implementation editor shows.</summary>
        public int BodyLine { get; }

        /// <summary>"FB_Y.MethodZ", or just "FB_Y" for a POU body.</summary>
        public string Location => MethodName == null ? PouTypeName : PouTypeName + "." + MethodName;

        /// <summary>
        /// <see cref="Location"/> with "(bodyLine)" folded in when the line is
        /// known. The single definition of that rendering: the interpreter's
        /// PlcCallStackFrame.LocationWithLine delegates here rather than
        /// repeating it, so a frame printed as part of an exception message and
        /// the same frame printed as part of a test failure can't drift into
        /// slightly different shapes.
        /// </summary>
        public string LocationWithLine =>
            BodyLine == UnknownLine ? Location : Location + "(" + BodyLine + ")";
    }
}
