using System;
using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Interpreter.Extensibility;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // An empty positional slot in a call - F(1, , 3) or a trailing F(1, 2, ) -
    // is a supplied-nothing slot: the slot is consumed, later arguments keep
    // their positions, and the input keeps whatever it holds without a value
    // (its declared default for a FUNCTION/method, its previous value for an
    // FB, whose inputs persist between calls). Breaking this either fails the
    // parse or silently binds the wrong value to later inputs.
    // Where an empty slot cannot be honoured, it is an error that names the
    // call, never silently accepted.
    public class EmptyPositionalArgTests
    {
        private static FbInstance RunCaller(string callStatements, params PouAst[] extraPous) =>
            RunCaller(callStatements, null, extraPous);

        private static FbInstance RunCaller(string callStatements, NativeFunctionRegistry natives, params PouAst[] extraPous)
        {
            var caller = new MethodAst("Run", "METHOD Run : BOOL", callStatements);
            var fb = new PouAst(
                "FB_Widget", null,
                "VAR\n\tnResult : DINT;\n\tnSeenB : DINT;\n\tsJoined : STRING;\n\tfbLogger : FB_Logger;\n\tfbCounter : FB_Counter3;\n\tfbTon : TON;\nEND_VAR",
                "", new List<MethodAst> { caller });
            var pous = new List<PouAst> { fb };
            pous.AddRange(extraPous);
            if (!pous.Exists(p => p.Name == "FB_Logger")) pous.Add(LoggerFb());
            if (!pous.Exists(p => p.Name == "FB_Counter3")) pous.Add(Counter3Fb());
            var engine = natives == null
                ? new Engine(new TypeRegistry(pous))
                : new Engine(new TypeRegistry(pous), natives);
            var instance = engine.NewInstance("FB_Widget");
            engine.CallMethod(instance, "Run", new Expr[0], new NamedArg[0], null, null);
            return instance;
        }

        private static PouAst LoggerFb()
        {
            var log = new MethodAst(
                "Log",
                "METHOD Log : DINT\nVAR_INPUT\n\tsMsg : STRING;\n\teLevel : DINT;\n\tsExtra : STRING := 'none';\nEND_VAR",
                "IF sExtra = 'none' THEN Log := eLevel; ELSE Log := -1; END_IF");
            return new PouAst("FB_Logger", null, "FUNCTION_BLOCK FB_Logger", "", new List<MethodAst> { log });
        }

        private static PouAst Counter3Fb() => new PouAst(
            "FB_Counter3", null,
            "FUNCTION_BLOCK FB_Counter3\nVAR_INPUT\n\tnA : DINT;\n\tnB : DINT := 10;\n\tnC : DINT;\nEND_VAR",
            "",
            new List<MethodAst>());

        private static PouAst MidFunction() => new PouAst(
            "F_Mid", null,
            "FUNCTION F_Mid : DINT\nVAR_INPUT\n\tnA : DINT;\n\tnB : DINT := 10;\n\tnC : DINT;\nEND_VAR",
            "F_Mid := nA * 100 + nB * 10 + nC;",
            new List<MethodAst>());

        [Fact]
        public void FunctionCall_TrailingEmptyArg_BindsDeclaredDefault()
        {
            var function = new PouAst(
                "F_Log", null,
                "FUNCTION F_Log : DINT\nVAR_INPUT\n\tsMsg : STRING;\n\teLevel : DINT;\n\tsExtra : STRING := 'none';\nEND_VAR",
                "IF sExtra = 'none' THEN F_Log := eLevel; ELSE F_Log := -1; END_IF",
                new List<MethodAst>());

            var instance = RunCaller("nResult := F_Log('x', 7, );", function);

            Assert.Equal(7, instance.Fields["nResult"].Value);
        }

        [Fact]
        public void MethodCall_TrailingEmptyArg_BindsDeclaredDefault()
        {
            var instance = RunCaller("nResult := fbLogger.Log('x', 7, );");

            Assert.Equal(7, instance.Fields["nResult"].Value);
        }

        [Fact]
        public void FunctionCall_EmptyMiddleArg_BindsDefaultForThatInputAndLaterArgsStayInPlace()
        {
            var instance = RunCaller("nResult := F_Mid(1, , 3);", MidFunction());

            Assert.Equal(1 * 100 + 10 * 10 + 3, instance.Fields["nResult"].Value);
        }

        [Fact]
        public void FunctionCall_EmptyMiddleArgFollowedByNamedArg_NamedArgStillBinds()
        {
            var instance = RunCaller("nResult := F_Mid(1, , nC := 3);", MidFunction());

            Assert.Equal(1 * 100 + 10 * 10 + 3, instance.Fields["nResult"].Value);
        }

        [Fact]
        public void FunctionCall_EmptyArgAfterNamedArg_ConsumesNextUnnamedSlot()
        {
            var instance = RunCaller("nResult := F_Mid(nA := 1, , 3);", MidFunction());

            Assert.Equal(1 * 100 + 10 * 10 + 3, instance.Fields["nResult"].Value);
        }

        [Fact]
        public void FbCall_EmptyMiddleArg_KeepsInputsPreviousValue()
        {
            var instance = RunCaller(
                "fbCounter(1, 7, 3); fbCounter(2, , 4); nSeenB := fbCounter.nB;");

            Assert.Equal(7, instance.Fields["nSeenB"].Value);
        }

        [Fact]
        public void FbCall_EmptyMiddleArgOnFirstCall_KeepsDeclaredInitialValue()
        {
            var instance = RunCaller("fbCounter(2, , 4); nSeenB := fbCounter.nB;");

            Assert.Equal(10, instance.Fields["nSeenB"].Value);
        }

        [Fact]
        public void FbCall_TrailingEmptyArg_KeepsInputsPreviousValueAndBindsEarlierArgs()
        {
            var instance = RunCaller(
                "fbCounter(1, 2, 9); fbCounter(5, 6, ); nResult := fbCounter.nC + fbCounter.nA;");

            Assert.Equal(9 + 5, instance.Fields["nResult"].Value);
        }

        [Fact]
        public void FunctionCall_EmptyInOutSlot_ThrowsNamingTheParameter()
        {
            var function = new PouAst(
                "F_Io", null,
                "FUNCTION F_Io : DINT\nVAR_INPUT\n\tnA : DINT;\nEND_VAR\nVAR_IN_OUT\n\tnRef : DINT;\nEND_VAR",
                "F_Io := nA;",
                new List<MethodAst>());

            var error = Assert.ThrowsAny<Exception>(() => RunCaller("nResult := F_Io(1, );", function));

            Assert.Contains("nRef", error.Message);
            Assert.Contains("cannot be left empty", error.Message);
        }

        [Fact]
        public void FbCall_EmptyInOutSlot_ThrowsNamingTheParameter()
        {
            var fb = new PouAst(
                "FB_Counter3", null,
                "FUNCTION_BLOCK FB_Counter3\nVAR_INPUT\n\tnA : DINT;\nEND_VAR\nVAR_IN_OUT\n\tnRef : DINT;\nEND_VAR",
                "",
                new List<MethodAst>());

            var error = Assert.ThrowsAny<Exception>(() => RunCaller("fbCounter(1, );", fb));

            Assert.Contains("nRef", error.Message);
            Assert.Contains("cannot be left empty", error.Message);
        }

        private sealed class EchoFunction : IXstunitNativeFunction
        {
            public string Name => "F_Echo";
            public object Invoke(NativeCallContext context) => 0;
        }

        [Fact]
        public void NativeFunctionCall_EmptyArg_ThrowsNamingTheCall()
        {
            var natives = new NativeFunctionRegistry();
            natives.RegisterAll(new IXstunitNativeFunction[] { new EchoFunction() });

            var error = Assert.ThrowsAny<Exception>(() => RunCaller("nResult := F_Echo(1, , 3);", natives));

            Assert.Contains("F_Echo", error.Message);
            Assert.Contains("empty argument not supported", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("EmptyArgExpr", error.Message);
        }

        [Fact]
        public void SizeofCall_EmptyArg_ThrowsNamingTheCall()
        {
            var error = Assert.ThrowsAny<Exception>(() => RunCaller("nResult := SIZEOF(, 1);"));

            Assert.Contains("SIZEOF", error.Message);
            Assert.DoesNotContain("EmptyArgExpr", error.Message);
        }

        [Fact]
        public void ConcatCall_EmptyOptionalArg_SkipsIt()
        {
            var instance = RunCaller("sJoined := CONCAT('a', 'b', , 'd');");

            Assert.Equal("abd", instance.Fields["sJoined"].Value);
        }

        [Fact]
        public void CastCall_EmptyArg_ThrowsNamingTheCall()
        {
            var error = Assert.ThrowsAny<Exception>(() => RunCaller("nResult := INT_TO_DINT(IN := 5, );"));

            Assert.Contains("INT_TO_DINT", error.Message);
            Assert.DoesNotContain("EmptyArgExpr", error.Message);
        }

        [Fact]
        public void NativeFbCall_EmptyArg_ThrowsNamingTheCall()
        {
            var error = Assert.ThrowsAny<Exception>(() => RunCaller("fbTon(TRUE, );"));

            Assert.Contains("TON", error.Message);
            Assert.DoesNotContain("EmptyArgExpr", error.Message);
        }

        [Fact]
        public void AdrCall_EmptyArg_ThrowsNamingTheCall()
        {
            var error = Assert.ThrowsAny<Exception>(() => RunCaller("nResult := ADR(, );"));

            Assert.Contains("ADR", error.Message);
            Assert.DoesNotContain("EmptyArgExpr", error.Message);
        }

        [Fact]
        public void IsValidRefCall_EmptyArg_ThrowsNamingTheCall()
        {
            var error = Assert.ThrowsAny<Exception>(() => RunCaller("nResult := __ISVALIDREF(, );"));

            Assert.Contains("__ISVALIDREF", error.Message);
            Assert.DoesNotContain("EmptyArgExpr", error.Message);
        }

        [Fact]
        public void SuiteAssertCall_EmptyArg_ThrowsNamingTheCall()
        {
            var suite = new PouAst(
                "FB_Suite", "TcUnit.FB_TestSuite",
                "FUNCTION_BLOCK FB_Suite EXTENDS TcUnit.FB_TestSuite",
                "",
                new List<MethodAst> { new MethodAst("Run", "METHOD Run", "AssertEquals(1, , 'm');") });
            var engine = new Engine(new TypeRegistry(new[] { suite }));
            var instance = engine.NewInstance("FB_Suite");

            var error = Assert.ThrowsAny<Exception>(() =>
                engine.CallMethod(instance, "Run", new Expr[0], new NamedArg[0], null, null));

            Assert.Contains("AssertEquals", error.Message);
            Assert.DoesNotContain("EmptyArgExpr", error.Message);
        }
    }
}
