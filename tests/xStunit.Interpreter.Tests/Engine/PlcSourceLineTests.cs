using System;
using System.Collections.Generic;
using System.Linq;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // TcXunit-p3t.4: PlcSourceLocationException.Line, the composition of
    // p3t.1 (who faulted), p3t.2 (which line of the body) and p3t.3 (where the
    // body starts in the file). These go through TcPouParser rather than
    // hand-built PouAst/MethodAst on purpose - a hand-built AST's BodyStartLine
    // defaults to 1, which would make an off-by-BodyStartLine bug invisible.
    public class PlcSourceLineTests
    {
        [Fact]
        public void RunSuite_FaultInMethodBody_ReportsTheTcPouFileLineAndTheBodyRelativeLine()
        {
            // The faulting statement is body line 4 of a method whose ST
            // element opens on file line 12, i.e. file line 15. In-body line 4
            // and naive 12 + 4 = 16 are both wrong, and both distinguishable.
            var suite = TcPouParser.Parse(FaultOnBodyLine4Xml);
            var engine = new Engine(new TypeRegistry(new[] { suite }));

            // TcXunit-3tx.3: the fault is inside an open TEST() bracket, so it
            // fails that test rather than the suite. The location machinery
            // this test is about is unchanged - it now arrives on the failure.
            var failure = Assert.Single(Assert.Single(engine.RunSuite("FB_LineSuite")).Failures);

            Assert.Equal(12, suite.Methods[0].BodyStartLine);
            // TcXunit-gfs: Line stays the raw .TcPOU file line (structured
            // consumers only); BodyLine is the new human-facing number and is
            // what Message now embeds.
            Assert.Equal(15, failure.Site.Line);
            Assert.Equal(4, failure.Site.BodyLine);
            Assert.Equal("FB_LineSuite.Fails", failure.Site.Location);
            Assert.StartsWith("FB_LineSuite.Fails(4): ", failure.Message);
        }

        [Fact]
        public void RunSuite_FaultInSuiteBody_ReportsThatBodysFileLine()
        {
            // A POU body reaches its frame through PouAst.BodyStartLine, a
            // different plumbing path than a METHOD body's
            // MethodAst.BodyStartLine.
            var suite = TcPouParser.Parse(SuiteBodyFaultXml);
            var engine = new Engine(new TypeRegistry(new[] { suite }));

            var ex = Assert.Throws<PlcSourceLocationException>(() => engine.RunSuite("FB_PouBodySuite"));

            Assert.Equal(9, suite.BodyStartLine);
            Assert.Equal(11, ex.Line);
            Assert.Equal(3, ex.BodyLine);
            Assert.Null(ex.MethodName);
            Assert.Equal("FB_PouBodySuite(3): " + ex.InnerException.Message, ex.Message);
        }

        [Fact]
        public void RunSuite_FaultDeepInCallChain_ReportsInnermostFramesLine()
        {
            // Suite body -> Outer (calls on its body line 2) -> Inner (faults
            // on its body line 3). Every frame has a line now, so "first writer
            // wins" has to still mean the innermost one - a caller stamping its
            // own line over the callee's would be silently plausible-looking.
            var suite = TcPouParser.Parse(NestedChainXml);
            var engine = new Engine(new TypeRegistry(new[] { suite }));

            // TcXunit-3tx.3: contained into the open TEST() bracket; the
            // innermost-wins rule this test pins is unchanged.
            var failure = Assert.Single(Assert.Single(engine.RunSuite("FB_NestedLineSuite")).Failures);

            var outer = suite.Methods[0];
            var inner = suite.Methods[1];
            Assert.Equal("Outer", outer.Name);
            Assert.Equal("Inner", inner.Name);

            Assert.Equal("Inner", failure.Site.MethodName);
            Assert.Equal(inner.BodyStartLine + 2, failure.Site.Line);
            Assert.NotEqual(outer.BodyStartLine + 1, failure.Site.Line);

            // Body-relative: Inner's fault is on its own body line 3, never
            // Outer's body line 2 (its call site) - "innermost wins" must hold
            // for BodyLine exactly as it already does for Line.
            Assert.Equal(3, failure.Site.BodyLine);
            Assert.NotEqual(2, failure.Site.BodyLine);

            // TcXunit-1am: each level of the chain keeps its OWN line, not
            // the innermost one - Outer's entry is its call site (body line
            // 2), never Inner's fault line (body line 3).
            Assert.Equal(
                new[] { "Inner", "Outer", null },
                failure.CallStack.Select(f => f.MethodName).ToArray());
            Assert.Equal(3, failure.CallStack[0].BodyLine);
            Assert.Equal(2, failure.CallStack[1].BodyLine);
            Assert.Equal(inner.BodyStartLine + 2, failure.CallStack[0].Line);
            Assert.Equal(outer.BodyStartLine + 1, failure.CallStack[1].Line);
        }

        [Fact]
        public void Frame_StatementWithNoLine_TranslatesToUnknownRatherThanBodyStartMinusOne()
        {
            // The degradation contract: a statement the parser never stamped
            // (Stmt.Line == 0, i.e. a hand-built AST) must produce "no line",
            // not the body's start line and not BodyStartLine - 1.
            var frame = new Frame(null, "FB_X", "M", bodyStartLine: 42);

            Assert.Equal(PlcSourceLocationException.UnknownLine, frame.CurrentLine);
            Assert.Equal(PlcSourceLocationException.UnknownLine, frame.CurrentFileLine);

            frame.CurrentLine = 1;
            Assert.Equal(42, frame.CurrentFileLine);
        }

        [Fact]
        public void PlcSourceLocationException_UnknownLine_RendersExactlyTheP3t1Shape()
        {
            var inner = new InvalidOperationException("boom");

            var unknown = new PlcSourceLocationException(
                "FB_X", "M", PlcSourceLocationException.UnknownLine, PlcSourceLocationException.UnknownLine, inner);
            // TcXunit-gfs: Message embeds BodyLine, not Line - a suite failing
            // several call-frames deep in a real .TcPOU file can have a raw
            // file line in the hundreds while its body-relative line stays
            // small, so the two must be distinguishable here.
            var known = new PlcSourceLocationException("FB_X", "M", 142, 7, inner);

            Assert.Equal("FB_X.M: boom", unknown.Message);
            Assert.DoesNotContain("(0)", unknown.Message);
            Assert.Equal("FB_X.M(7): boom", known.Message);
            Assert.Equal(142, known.Line);
            Assert.Equal(7, known.BodyLine);
        }

        // Line 12 is `        <ST><![CDATA[TEST('Fails');`, so the method body's
        // line 1 is `TEST('Fails');` and its line 4 is the faulting call on
        // file line 15.
        private const string FaultOnBodyLine4Xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_LineSuite"" Id=""{00000000-0000-0000-0000-0000000000c0}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_LineSuite EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[Fails();]]></ST>
    </Implementation>
    <Method Name=""Fails"" Id=""{00000000-0000-0000-0000-0000000000c1}"">
      <Declaration><![CDATA[METHOD PRIVATE Fails
]]></Declaration>
      <Implementation>
        <ST><![CDATA[TEST('Fails');

(* comment lines count too *)
ThisMethodDoesNotExist();

TEST_FINISHED();]]></ST>
      </Implementation>
    </Method>
  </POU>
</TcPlcObject>";

        // Line 9 opens the POU body; the fault is on its body line 3 == file
        // line 11.
        private const string SuiteBodyFaultXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_PouBodySuite"" Id=""{00000000-0000-0000-0000-0000000000c2}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_PouBodySuite EXTENDS TcUnit.FB_TestSuite
VAR
	n : INT;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[n := 1;
n := n + 1;
ThisMethodDoesNotExist();]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

        private const string NestedChainXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_NestedLineSuite"" Id=""{00000000-0000-0000-0000-0000000000c3}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_NestedLineSuite EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[Outer();]]></ST>
    </Implementation>
    <Method Name=""Outer"" Id=""{00000000-0000-0000-0000-0000000000c4}"">
      <Declaration><![CDATA[METHOD PRIVATE Outer
]]></Declaration>
      <Implementation>
        <ST><![CDATA[TEST('Outer');
Inner();
TEST_FINISHED();]]></ST>
      </Implementation>
    </Method>
    <Method Name=""Inner"" Id=""{00000000-0000-0000-0000-0000000000c5}"">
      <Declaration><![CDATA[METHOD PRIVATE Inner
]]></Declaration>
      <Implementation>
        <ST><![CDATA[(* padding so Inner's fault line differs from Outer's *)
(* padding *)
ThisMethodDoesNotExist();]]></ST>
      </Implementation>
    </Method>
  </POU>
</TcPlcObject>";
    }
}
