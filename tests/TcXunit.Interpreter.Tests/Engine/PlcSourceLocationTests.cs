using System;
using System.Collections.Generic;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-p3t.1: when an ST body throws, the only context that used to
    // reach CliRunner was the suite name plus a .NET stack trace of
    // interpreter internals - nothing said which PLC POU/method was
    // executing. Engine now stamps the innermost interpreted body onto the
    // in-flight exception and wraps it once, at the suite boundary, in a
    // PlcSourceLocationException that keeps the original as InnerException.
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
        public void RunSuite_ThrowDeepInCallChain_PreservesOriginalExceptionTypeAndMessage()
        {
            var engine = NewNestedChainEngine();

            var ex = Assert.Throws<PlcSourceLocationException>(() => engine.RunSuite("FB_MySuite"));

            var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);
            Assert.Equal("Method 'ThisMethodDoesNotExist' not found starting from type 'FB_Deep'", inner.Message);
            // The "(1)" is TcXunit-p3t.4: these hand-built MethodAsts take
            // BodyStartLine's default of 1, and Level3's body is a single line,
            // so the file line and the in-body line coincide at 1. What this
            // test pins is unchanged - the inner message survives verbatim
            // after the location.
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
        }

        [Fact]
        public void RunSuite_ThrowInsideBareInvokedFbBody_ReportsThatFbNotTheSuite()
        {
            // A bare FB invocation (sfbThing();) runs the callee's own top-level
            // body in its own frame - the reported POU must be the callee, not
            // the suite that invoked it.
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

        // TcXunit-n65: MethodAst.ImplementationText is parsed lazily, the
        // first time TypeRegistry.GetStatements sees that exact body text -
        // and every call site shaped like
        // `ExecuteBody(_registry.GetStatements(...), frame)` used to resolve
        // that argument *before* ExecuteBody's own try/catch was entered. So
        // when the callee's body itself contains an unparseable construct,
        // the lazy parse throws outside the callee's frame and gets caught
        // (and location-stamped) by whichever caller's ExecuteBody is still
        // on the CLR stack - here, the suite body that merely called
        // guard.M_Check(); one line. The fix must make the reported location
        // name FB_Widget.M_Check (the body that actually fails to parse),
        // never FB_MySuite (the caller).
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
                    // '?' isn't a recognized character anywhere in the v1
                    // lexer (see Lexer.Tokenize's default switch case), so
                    // Lexer.Tokenize throws FormatException the first time
                    // this method's body is lazily parsed via GetStatements.
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
            Assert.IsType<FormatException>(ex.InnerException);
        }

        [Fact]
        public void CallMethod_Throw_IsNotWrapped_SoDirectInterpreterCallersKeepTheirExceptionTypes()
        {
            // Wrapping happens once, at the suite boundary - not at every
            // CallMethod level. Engine.CallMethod is public and driven directly
            // by tests and by future embedders that switch on the concrete
            // exception type, so its contract must stay unchanged.
            var engine = NewNestedChainEngine();
            var instance = engine.NewInstance("FB_Deep");

            var ex = Assert.Throws<InvalidOperationException>(
                () => engine.CallMethod(instance, "Level1", new Expr[0], new NamedArg[0], null, null));

            Assert.Equal("Method 'ThisMethodDoesNotExist' not found starting from type 'FB_Deep'", ex.Message);
        }

        // AssertConverges/AssertConvergesAndLatches throw rather than record a
        // TcUnit-style failure (Engine.Convergence.cs), so they travel the same
        // path a genuine interpreter fault does.
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

            // Same failure reached through RunSuite keeps the convergence
            // diagnosis verbatim, just prefixed with where it came from.
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
            // Was "Line defaults to unknown" while p3t.4's seam was unfilled;
            // p3t.4 fills it with fileLine = MethodAst.BodyStartLine +
            // node.Line - 1. A hand-built MethodAst has no .TcPOU file, so
            // BodyStartLine keeps its documented default of 1 and the formula
            // degrades to the identity - the body IS the file. Line stays 0
            // only when the statement itself has no line (see
            // PlcSourceLineTests).
            var engine = NewNestedChainEngine();

            var ex = Assert.Throws<PlcSourceLocationException>(() => engine.RunSuite("FB_MySuite"));

            Assert.Equal(1, ex.Line);
            Assert.Equal(0, PlcSourceLocationException.UnknownLine);
        }
    }
}
