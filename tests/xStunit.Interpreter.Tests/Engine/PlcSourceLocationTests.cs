using System;
using System.Collections.Generic;
using System.Linq;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // A fault inside an interpreted ST body is stamped with the innermost PLC
    // POU/method and wrapped exactly once, at the suite boundary, in a
    // PlcSourceLocationException that keeps the original as InnerException.
    // Without that, a caller sees only the suite name and a .NET stack trace of
    // interpreter internals, with nothing naming the PLC code that failed.
    public class PlcSourceLocationTests
    {
        // Suite -> FB_Deep.Level1 -> Level2 -> Level3 -> (missing method).
        // Three interpreted frames deep so "innermost wins" is actually
        // exercised rather than accidentally satisfied by a one-frame chain.
        private static Engine NewNestedChainEngine(string suiteBody = "deep.Level1();")
        {
            var deep = new PouAst(
                "FB_Deep",
                null,
                "",
                "",
                new List<MethodAst>
                {
                    new MethodAst("Level1", "METHOD PUBLIC Level1", "Level2();"),
                    new MethodAst("Level2", "METHOD PUBLIC Level2", "Level3();"),
                    new MethodAst("Level3", "METHOD PUBLIC Level3", "ThisMethodDoesNotExist();"),
                });

            var suite = new PouAst(
                "FB_MySuite",
                "TcUnit.FB_TestSuite",
                "VAR\n\tdeep : FB_Deep;\nEND_VAR",
                suiteBody,
                new List<MethodAst>());

            return new Engine(new TypeRegistry(new[] { deep, suite }));
        }

        [Fact]
        public void RunSuite_ThrowDeepInCallChain_ReportsInnermostPouAndMethod()
        {
            var engine = NewNestedChainEngine();

            var ex = Assert.Throws<PlcSourceLocationException>(() => engine.RunSuite("FB_MySuite"));

            Assert.Equal("FB_Deep", ex.PouTypeName);
            Assert.Equal("Level3", ex.MethodName);
            Assert.Equal("FB_Deep.Level3", ex.Location);
        }

        [Fact]
        public void RunSuite_ThrowDeepInCallChain_CallStackCapturesEveryLevelInnermostFirst()
        {
            var engine = NewNestedChainEngine();

            var ex = Assert.Throws<PlcSourceLocationException>(() => engine.RunSuite("FB_MySuite"));

            Assert.Equal(
                new[] { "FB_Deep.Level3", "FB_Deep.Level2", "FB_Deep.Level1", "FB_MySuite" },
                ex.CallStack.Select(f => f.Location).ToArray());

            // The flat single-frame properties describe the innermost fault
            // alone, so they must never drift from CallStack[0].
            Assert.Equal(ex.PouTypeName, ex.CallStack[0].PouTypeName);
            Assert.Equal(ex.MethodName, ex.CallStack[0].MethodName);
            Assert.Equal(ex.Line, ex.CallStack[0].Line);
            Assert.Equal(ex.BodyLine, ex.CallStack[0].BodyLine);

            // A top-level POU body frame carries no method name.
            Assert.Null(ex.CallStack[3].MethodName);
        }

        [Fact]
        public void RunSuite_ThrowDeepInCallChain_PreservesOriginalExceptionTypeAndMessage()
        {
            var engine = NewNestedChainEngine();

            var ex = Assert.Throws<PlcSourceLocationException>(() => engine.RunSuite("FB_MySuite"));

            var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);
            Assert.Contains("'ThisMethodDoesNotExist' not found", inner.Message);
            // The "(1)" is the body-relative line: hand-built MethodAsts take
            // BodyStartLine's default of 1 and Level3's body is one line, so it
            // coincides with the file line. The inner message must survive
            // verbatim after the location prefix.
            Assert.Equal("FB_Deep.Level3(1): " + inner.Message, ex.Message);
        }

        [Fact]
        public void RunSuite_ThrowDirectlyInSuiteBody_ReportsPouWithNoMethod()
        {
            var suite = new PouAst(
                "FB_MySuite",
                "TcUnit.FB_TestSuite",
                "",
                "ThisMethodDoesNotExist();",
                new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { suite }));

            var ex = Assert.Throws<PlcSourceLocationException>(() => engine.RunSuite("FB_MySuite"));

            Assert.Equal("FB_MySuite", ex.PouTypeName);
            Assert.Null(ex.MethodName);
            Assert.Equal("FB_MySuite", ex.Location);
            Assert.Equal(new[] { "FB_MySuite" }, ex.CallStack.Select(f => f.Location).ToArray());
        }

        [Fact]
        public void RunSuite_ThrowInsideBareInvokedFbBody_ReportsThatFbNotTheSuite()
        {
            // A bare FB invocation runs the callee's own top-level body in its
            // own frame, the same as a method call does.
            var thing = new PouAst("FB_Thing", null, "", "ThisMethodDoesNotExist();", new List<MethodAst>());
            var suite = new PouAst(
                "FB_MySuite",
                "TcUnit.FB_TestSuite",
                "VAR\n\tsfbThing : FB_Thing;\nEND_VAR",
                "sfbThing();",
                new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { thing, suite }));

            var ex = Assert.Throws<PlcSourceLocationException>(() => engine.RunSuite("FB_MySuite"));

            Assert.Equal("FB_Thing", ex.PouTypeName);
            Assert.Null(ex.MethodName);
        }

        // A method body is parsed lazily, on first execution, so a parse
        // failure surfaces while some caller's frame is the one on the CLR
        // stack. The reported location must still name the body that failed to
        // parse; blaming the caller that merely invoked it is the regression.
        [Fact]
        public void RunSuite_CalleeBodyFailsToParse_ReportsCalleeNotCaller()
        {
            var widget = new PouAst(
                "FB_Widget",
                null,
                "",
                "",
                new List<MethodAst>
                {
                    // '?' is not a character the lexer recognizes, so this body
                    // throws ParseException the first time it is lazily parsed.
                    new MethodAst("M_Check", "METHOD PUBLIC M_Check", "x := 1 ? 2;"),
                });

            var suite = new PouAst(
                "FB_MySuite",
                "TcUnit.FB_TestSuite",
                "VAR\n\tguard : FB_Widget;\nEND_VAR",
                "guard.M_Check();",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { widget, suite }));

            var ex = Assert.Throws<PlcSourceLocationException>(() => engine.RunSuite("FB_MySuite"));

            Assert.Equal("FB_Widget", ex.PouTypeName);
            Assert.Equal("M_Check", ex.MethodName);
            Assert.Equal("FB_Widget.M_Check", ex.Location);
            Assert.IsType<ParseException>(ex.InnerException);
        }

        // A callee's local VAR defaults are constructed before its body starts
        // executing, so a fault there has no statement to hang off. It must
        // still be attributed to the callee's frame rather than falling back to
        // the caller's call site.
        [Fact]
        public void RunSuite_CalleeLocalVarDefaultConstructionFails_ReportsCalleeNotSuite()
        {
            var widget = new PouAst(
                "FB_Widget",
                null,
                "VAR\n\tnValue : INT := UNDEFINED_CONSTANT;\nEND_VAR",
                "",
                new List<MethodAst>());

            var suite = new PouAst(
                "FB_MySuite",
                "TcUnit.FB_TestSuite",
                "",
                "M_SomeTest();",
                new List<MethodAst>
                {
                    new MethodAst(
                        "M_SomeTest",
                        "METHOD PRIVATE M_SomeTest\nVAR\n\tsfbWidget : FB_Widget;\nEND_VAR",
                        "TEST('M_SomeTest');\nAssertTrue(TRUE, 'never reached');\nTEST_FINISHED();"),
                });

            var engine = new Engine(new TypeRegistry(new[] { widget, suite }));

            var ex = Assert.Throws<PlcSourceLocationException>(() => engine.RunSuite("FB_MySuite"));

            Assert.Equal("FB_MySuite", ex.PouTypeName);
            Assert.Equal("M_SomeTest", ex.MethodName);
            Assert.Equal("FB_MySuite.M_SomeTest", ex.Location);
            Assert.Contains("Unknown variable 'UNDEFINED_CONSTANT'", ex.Message);

            // The callee's frame belongs on the stack even though its body
            // never began executing.
            Assert.Equal(
                new[] { "FB_MySuite.M_SomeTest", "FB_MySuite" },
                ex.CallStack.Select(f => f.Location).ToArray());
        }

        [Fact]
        public void CallMethod_Throw_IsNotWrapped_SoDirectInterpreterCallersKeepTheirExceptionTypes()
        {
            // Wrapping happens once, at the suite boundary, never at every
            // CallMethod level: CallMethod is public and its callers switch on
            // the concrete exception type.
            var engine = NewNestedChainEngine();
            var instance = engine.NewInstance("FB_Deep");

            var ex = Assert.Throws<InvalidOperationException>(
                () => engine.CallMethod(instance, "Level1", new Expr[0], new NamedArg[0], null, null));

            Assert.Contains("'ThisMethodDoesNotExist' not found", ex.Message);
        }

        // AssertConverges/AssertConvergesAndLatches throw rather than record a
        // TcUnit-style failure, so they travel the same wrapping path a genuine
        // interpreter fault does.
        private static Engine NewConvergenceEngine(string suiteBody)
        {
            var master = new PouAst("FB_Master", null, "VAR\n\tValue : INT;\nEND_VAR", "", new List<MethodAst>());
            var proxy = new PouAst("FB_ProxyRamp", null, "VAR\n\tValue : INT;\nEND_VAR", "Value := Value + 1;", new List<MethodAst>());
            var suite = new PouAst(
                "FB_MySuite",
                "TcUnit.FB_TestSuite",
                "VAR\n\tmaster : FB_Master;\n\tproxy : FB_ProxyRamp;\nEND_VAR",
                suiteBody,
                new List<MethodAst>
                {
                    new MethodAst(
                        "CheckConvergence",
                        "METHOD PUBLIC CheckConvergence",
                        "master.Value := 5;\nAssertConverges(master, proxy, ['Value'], 2);"),
                });
            return new Engine(new TypeRegistry(new[] { master, proxy, suite }));
        }

        [Fact]
        public void RunSuite_ConvergenceAssertionFailure_IsWrappedButPreservedAsInnerException()
        {
            var directEngine = NewConvergenceEngine("");
            var direct = Assert.Throws<ConvergenceAssertionException>(
                () => directEngine.CallMethod(
                    directEngine.NewInstance("FB_MySuite"), "CheckConvergence", new Expr[0], new NamedArg[0], null, null));

            var wrapped = Assert.Throws<PlcSourceLocationException>(
                () => NewConvergenceEngine("CheckConvergence();").RunSuite("FB_MySuite"));

            Assert.Equal("FB_MySuite.CheckConvergence", wrapped.Location);
            Assert.IsType<ConvergenceAssertionException>(wrapped.InnerException);
            Assert.Equal(direct.Message, wrapped.InnerException.Message);
            Assert.Contains(direct.Message, wrapped.Message);
        }

        [Fact]
        public void RunSuite_ReturnAndExitSignals_AreNeverWrapped()
        {
            // RETURN/EXIT unwind through the same body boundary as a genuine
            // fault; wrapping them would break loop/return semantics outright.
            var suite = new PouAst(
                "FB_MySuite",
                "TcUnit.FB_TestSuite",
                "VAR\n\ti : INT;\n\ttotal : INT;\nEND_VAR",
                "TEST('LoopAndReturn');\n" +
                "FOR i := 1 TO 10 DO\n" +
                "\tIF i > 3 THEN\n\t\tEXIT;\n\tEND_IF\n" +
                "\ttotal := total + i;\n" +
                "END_FOR\n" +
                "AssertEquals_INT(Expected := 6, Actual := total, Message := 'exit stops at 3');\n" +
                "TEST_FINISHED();\n" +
                "RETURN;",
                new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { suite }));

            var results = engine.RunSuite("FB_MySuite");

            Assert.True(Assert.Single(results).Passed, results[0].ToString());
        }

        [Fact]
        public void PlcSourceLocationException_HandBuiltAst_ReportsTheInBodyLineAsIs()
        {
            // Line is the file line, BodyLine the body-relative one:
            // BodyStartLine + node.Line - 1 versus node.Line. A hand-built
            // MethodAst has no source file, so BodyStartLine keeps its default
            // of 1 and the two coincide. Line falls back to UnknownLine only
            // when the statement itself carries no line (see PlcSourceLineTests).
            var engine = NewNestedChainEngine();

            var ex = Assert.Throws<PlcSourceLocationException>(() => engine.RunSuite("FB_MySuite"));

            Assert.Equal(1, ex.Line);
            Assert.Equal(1, ex.BodyLine);
            Assert.Equal(0, PlcSourceLocationException.UnknownLine);
        }
    }
}
