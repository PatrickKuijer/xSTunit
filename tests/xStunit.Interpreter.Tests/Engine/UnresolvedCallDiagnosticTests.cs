using System;
using System.Collections.Generic;
using System.Linq;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // TcXunit-kii: an unresolved call inside a global FUNCTION body threw a
    // bare NullReferenceException instead of a diagnostic naming the missing
    // function.
    //
    // CallGlobalFunction runs a FUNCTION body with Frame(null, ...) - a
    // FUNCTION has no THIS - so EvaluateCall's fall-through reached
    // CallMethod with a null instance, and CallMethod's first statement
    // dereferenced it. The clear "not found" message a few lines further down
    // was unreachable from that path, which mattered most exactly where such
    // calls live: thin wrappers around compiled-only vendor library functions.
    public class UnresolvedCallDiagnosticTests
    {
        [Fact]
        public void CallMethod_UnresolvedCallInsideGlobalFunctionBody_ReportsTheMissingFunction()
        {
            var wrapper = new PouAst(
                "F_ComputeChecksum",
                null,
                "FUNCTION F_ComputeChecksum : INT\nVAR_INPUT\n\tnIn : INT;\nEND_VAR",
                "F_ComputeChecksum := F_NotAnywhere(nIn);",
                new List<MethodAst>());

            var caller = new MethodAst("bDoWork", "METHOD bDoWork : BOOL", "nResult := F_ComputeChecksum(nValue);");
            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\n\tnValue : INT := 14;\n\tnResult : INT;\nEND_VAR",
                "",
                new List<MethodAst> { caller });

            var engine = new Engine(new TypeRegistry(new[] { fb, wrapper }));
            var instance = engine.NewInstance("FB_Widget");

            var ex = Assert.ThrowsAny<Exception>(() =>
                engine.CallMethod(instance, "bDoWork", new Expr[0], new NamedArg[0], null, null));

            // The regression itself: anything at all, as long as it isn't the
            // contentless NRE.
            Assert.IsNotType<NullReferenceException>(ex);
            Assert.Contains("F_NotAnywhere", ex.Message);
        }

        [Fact]
        public void RunSuite_UnresolvedCallInsideGlobalFunctionBody_KeepsTheFullCallChain()
        {
            // The fault must still be attributed through both frames - the
            // wrapper FUNCTION and the suite method that called it - so the
            // reported location points at the real culprit rather than the
            // outermost entry point.
            var wrapper = new PouAst(
                "F_ComputeChecksum",
                null,
                "FUNCTION F_ComputeChecksum : INT\nVAR_INPUT\n\tnIn : INT;\nEND_VAR",
                "F_ComputeChecksum := F_NotAnywhere(nIn);",
                new List<MethodAst>());

            var testCase = new MethodAst(
                "ChecksumWorks",
                "METHOD PRIVATE ChecksumWorks",
                "TEST('ChecksumWorks');\nnResult := F_ComputeChecksum(1);\nTEST_FINISHED();");

            var suite = new PouAst(
                "FB_WidgetTests",
                "TcUnit.FB_TestSuite",
                "FUNCTION_BLOCK FB_WidgetTests EXTENDS TcUnit.FB_TestSuite\nVAR\n\tnResult : INT;\nEND_VAR",
                "ChecksumWorks();",
                new List<MethodAst> { testCase });

            var engine = new Engine(new TypeRegistry(new[] { suite, wrapper }));

            // TcXunit-3tx.3: contained into the open TEST() bracket, so the
            // located message and its call chain ride on that test's failure -
            // the chain is what this test pins, and it is unchanged.
            var failure = Assert.Single(Assert.Single(engine.RunSuite("FB_WidgetTests")).Failures);

            Assert.Contains("F_NotAnywhere", failure.Message);
            Assert.Contains(failure.CallStack, f => f.PouTypeName == "F_ComputeChecksum");
            Assert.Contains(failure.CallStack, f => f.MethodName == "ChecksumWorks");
        }
    }
}
