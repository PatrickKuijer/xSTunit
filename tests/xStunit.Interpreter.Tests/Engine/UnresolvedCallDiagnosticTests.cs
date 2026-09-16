using System;
using System.Collections.Generic;
using System.Linq;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // A global FUNCTION has no THIS, so its frame carries no instance and the
    // usual method-resolution path has nothing to dereference. An unresolved
    // call from inside one must still name the missing function - that is
    // exactly where thin wrappers around compiled-only vendor libraries live,
    // so it is the diagnostic a reader needs most.
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

            // Which exception type is immaterial; what matters is that it is
            // not the contentless NullReferenceException a null instance gives.
            Assert.IsNotType<NullReferenceException>(ex);
            Assert.Contains("F_NotAnywhere", ex.Message);
        }

        // An unqualified call inside an FB method can be either a call on THIS
        // or a bare FUNCTION, and the interpreter cannot tell which was meant
        // once neither resolves. Reporting it as a method lookup alone pointed
        // the reader at the enclosing FB - a type that has nothing to do with a
        // missing compiled-only library function - and buried the one remedy
        // that fixes it.
        [Fact]
        public void CallMethod_UnresolvedUnqualifiedCallInsideAnFbMethod_NamesBothPossibilitiesAndThePluginRemedy()
        {
            var caller = new MethodAst("bDoWork", "METHOD bDoWork : BOOL", "bDoWork := TestAndSetTheOther(bFlag);");
            var fb = new PouAst(
                "FB_AccessGuard",
                null,
                "VAR\n\tbFlag : BOOL;\nEND_VAR",
                "",
                new List<MethodAst> { caller });

            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_AccessGuard");

            var ex = Assert.Throws<InvalidOperationException>(
                () => engine.CallMethod(instance, "bDoWork", new Expr[0], new NamedArg[0], null, null));

            Assert.Contains("TestAndSetTheOther", ex.Message);
            Assert.Contains("FB_AccessGuard", ex.Message);
            Assert.Contains("native function", ex.Message);
            Assert.DoesNotContain("not found starting from type", ex.Message);
        }

        // A call through an explicit receiver is unambiguously a method lookup,
        // so it keeps the shorter diagnostic that says so.
        [Fact]
        public void CallMethod_UnresolvedQualifiedCall_StillReportsAMethodLookup()
        {
            var caller = new MethodAst("bDoWork", "METHOD bDoWork : BOOL", "inner.NoSuchMethod();");
            var inner = new PouAst("FB_Inner", null, "", "", new List<MethodAst>());
            var fb = new PouAst(
                "FB_Outer",
                null,
                "VAR\n\tinner : FB_Inner;\nEND_VAR",
                "",
                new List<MethodAst> { caller });

            var engine = new Engine(new TypeRegistry(new[] { fb, inner }));
            var instance = engine.NewInstance("FB_Outer");

            var ex = Assert.Throws<InvalidOperationException>(
                () => engine.CallMethod(instance, "bDoWork", new Expr[0], new NamedArg[0], null, null));

            Assert.Equal("Method 'NoSuchMethod' not found starting from type 'FB_Inner'", ex.Message);
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

            var failure = Assert.Single(Assert.Single(engine.RunSuite("FB_WidgetTests")).Failures);

            Assert.Contains("F_NotAnywhere", failure.Message);
            Assert.Contains(failure.CallStack, f => f.PouTypeName == "F_ComputeChecksum");
            Assert.Contains(failure.CallStack, f => f.MethodName == "ChecksumWorks");
        }
    }
}
