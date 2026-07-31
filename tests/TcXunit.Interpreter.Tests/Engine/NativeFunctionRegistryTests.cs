using System;
using System.Collections.Generic;
using System.Linq;
using TcXunit.Interpreter;
using TcXunit.Interpreter.Extensibility;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-6k2: host-registered stand-ins for compiled-only TwinCAT library
    // functions (Tc2_Utilities' F_CheckSum16 and friends), which have no
    // .TcPOU anywhere to parse and so could never resolve through the
    // TcXunit-9su global-FUNCTION fallback.
    public class NativeFunctionRegistryTests
    {
        // Minimal test double: named function, computed from its args.
        private sealed class StubFunction : ITcXunitNativeFunction
        {
            private readonly Func<NativeCallContext, object> _body;

            public StubFunction(string name, Func<NativeCallContext, object> body)
            {
                Name = name;
                _body = body;
            }

            public string Name { get; }

            public object Invoke(NativeCallContext context) => _body(context);
        }

        private static NativeFunctionRegistry RegistryWith(params ITcXunitNativeFunction[] functions)
        {
            var registry = new NativeFunctionRegistry();
            registry.RegisterAll(functions);
            return registry;
        }

        [Fact]
        public void CallMethod_UnresolvedCallDispatchesToRegisteredNativeFunction()
        {
            var caller = new MethodAst(
                "bDoWork",
                "METHOD bDoWork : BOOL",
                "nResult := F_Triple(nValue);");

            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\n\tnValue : INT := 14;\n\tnResult : INT;\nEND_VAR",
                "",
                new List<MethodAst> { caller });

            var engine = new Engine(
                new TypeRegistry(new[] { fb }),
                RegistryWith(new StubFunction("F_Triple", ctx => ctx.RequireInt32("nIn", 0) * 3)));

            var instance = engine.NewInstance("FB_Widget");
            engine.CallMethod(instance, "bDoWork", new Expr[0], new NamedArg[0], null, null);

            Assert.Equal(42, instance.Fields["nResult"].Value);
        }

        [Fact]
        public void CallMethod_NativeFunctionLookupIsCaseInsensitive()
        {
            // IEC 61131-3 identifiers are case-insensitive and real PLC source
            // is inconsistent about casing, so a plugin registered as
            // "F_Triple" must answer a call written "f_TRIPLE".
            var caller = new MethodAst("bDoWork", "METHOD bDoWork : BOOL", "nResult := f_TRIPLE(nValue);");
            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\n\tnValue : INT := 14;\n\tnResult : INT;\nEND_VAR",
                "",
                new List<MethodAst> { caller });

            var engine = new Engine(
                new TypeRegistry(new[] { fb }),
                RegistryWith(new StubFunction("F_Triple", ctx => ctx.RequireInt32("nIn", 0) * 3)));

            var instance = engine.NewInstance("FB_Widget");
            engine.CallMethod(instance, "bDoWork", new Expr[0], new NamedArg[0], null, null);

            Assert.Equal(42, instance.Fields["nResult"].Value);
        }

        [Fact]
        public void CallMethod_RealGlobalFunctionPouWinsOverSameNamedNativeFunction()
        {
            // The precedence guarantee the whole design rests on: a plugin can
            // only fill a hole, never shadow source that actually exists. If
            // this inverts, a stale plugin silently replaces the user's own
            // POU and every suite still passes - against the wrong code.
            var realFunction = new PouAst(
                "F_Triple",
                null,
                "FUNCTION F_Triple : INT\nVAR_INPUT\n\tnIn : INT;\nEND_VAR",
                "F_Triple := nIn * 3;",
                new List<MethodAst>());

            var caller = new MethodAst("bDoWork", "METHOD bDoWork : BOOL", "nResult := F_Triple(nValue);");
            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\n\tnValue : INT := 14;\n\tnResult : INT;\nEND_VAR",
                "",
                new List<MethodAst> { caller });

            var engine = new Engine(
                new TypeRegistry(new[] { fb, realFunction }),
                RegistryWith(new StubFunction("F_Triple", _ => 999)));

            var instance = engine.NewInstance("FB_Widget");
            engine.CallMethod(instance, "bDoWork", new Expr[0], new NamedArg[0], null, null);

            Assert.Equal(42, instance.Fields["nResult"].Value);
        }

        [Fact]
        public void CallMethod_NativeFunctionReceivesNamedArgumentsByName()
        {
            var caller = new MethodAst(
                "bDoWork",
                "METHOD bDoWork : BOOL",
                "nResult := F_Sum(nB := 40, nA := 2);");

            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\n\tnResult : INT;\nEND_VAR",
                "",
                new List<MethodAst> { caller });

            var engine = new Engine(
                new TypeRegistry(new[] { fb }),
                RegistryWith(new StubFunction(
                    "F_Sum",
                    ctx => ctx.RequireInt32("nA", 0) + ctx.RequireInt32("nB", 1))));

            var instance = engine.NewInstance("FB_Widget");
            engine.CallMethod(instance, "bDoWork", new Expr[0], new NamedArg[0], null, null);

            // 2 + 40 regardless of the reversed source order: resolution is by
            // name, not by the slot the argument happened to occupy.
            Assert.Equal(42, instance.Fields["nResult"].Value);
        }

        [Fact]
        public void CallMethod_NativeFunctionReadsBytesBehindAPointerArgument()
        {
            // The shape most real library functions need (checksum/CRC/
            // serializer): walk a byte buffer reached through ADR().
            var caller = new MethodAst(
                "bDoWork",
                "METHOD bDoWork : BOOL",
                "aBuf[0] := 1;\naBuf[1] := 2;\naBuf[2] := 3;\naBuf[3] := 4;\n" +
                "nResult := F_SumBytes(ADR(aBuf), SIZEOF(aBuf));");

            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\n\taBuf : ARRAY[0..3] OF BYTE;\n\tnResult : INT;\nEND_VAR",
                "",
                new List<MethodAst> { caller });

            var engine = new Engine(
                new TypeRegistry(new[] { fb }),
                RegistryWith(new StubFunction("F_SumBytes", ctx =>
                {
                    var size = ctx.RequireInt32("nSize", 1);
                    return ctx.RequireBytes("pData", 0, size).Sum(b => (int)b);
                })));

            var instance = engine.NewInstance("FB_Widget");
            engine.CallMethod(instance, "bDoWork", new Expr[0], new NamedArg[0], null, null);

            Assert.Equal(10, instance.Fields["nResult"].Value);
        }

        [Fact]
        public void CallMethod_NativeFunctionReadsBytesBehindAPointerIntoAnArrayOfStructs()
        {
            // TcXunit-4jt: ADR(arrayOfStruct)/SIZEOF(arrayOfStruct) must
            // bounds-check the requested byte count against the array's true
            // byte size (elementCount * elementSize), not its raw element
            // count. uPair (USINT + INT, 2-byte aligned) is 4 bytes, so a
            // 2-element array is 8 bytes - previously ReadPointerBytes
            // bounds-checked SIZEOF(aPairs) (correctly 8) against
            // Elements.Length (wrongly 2, the element count), always failing.
            var structAst = new StructAst("uPair", new[]
            {
                new VarDecl("a", "USINT", null, VarSection.Local),
                new VarDecl("b", "INT", null, VarSection.Local),
            });

            var caller = new MethodAst(
                "bDoWork",
                "METHOD bDoWork : BOOL",
                "aPairs[0].a := 7;\naPairs[0].b := 258;\naPairs[1].a := 9;\naPairs[1].b := 1;\n" +
                "nResult := F_SumBytes(ADR(aPairs), SIZEOF(aPairs));");

            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\n\taPairs : ARRAY[0..1] OF uPair;\n\tnResult : INT;\nEND_VAR",
                "",
                new List<MethodAst> { caller });

            var engine = new Engine(
                new TypeRegistry(new[] { fb }, new[] { structAst }),
                RegistryWith(new StubFunction("F_SumBytes", ctx =>
                {
                    var size = ctx.RequireInt32("nSize", 1);
                    return ctx.RequireBytes("pData", 0, size).Sum(b => (int)b);
                })));

            var instance = engine.NewInstance("FB_Widget");
            engine.CallMethod(instance, "bDoWork", new Expr[0], new NamedArg[0], null, null);

            // Bytes: [0]=7, [1]=pad(0), [2..3]=258 LE (2,1), [4]=9, [5]=pad(0), [6..7]=1 LE (1,0)
            // Sum = 7 + 0 + 2 + 1 + 9 + 0 + 1 + 0 = 20
            Assert.Equal(20, instance.Fields["nResult"].Value);
        }

        [Fact]
        public void CallMethod_NativeFunctionReadingPastTheBufferEndFailsWithASizeMessage()
        {
            // A real PLC would read adjacent memory here; the interpreter has
            // none to read, so it must refuse in terms of the size argument at
            // the call site rather than throw IndexOutOfRange.
            var caller = new MethodAst(
                "bDoWork",
                "METHOD bDoWork : BOOL",
                "nResult := F_SumBytes(ADR(aBuf), 99);");

            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\n\taBuf : ARRAY[0..3] OF BYTE;\n\tnResult : INT;\nEND_VAR",
                "",
                new List<MethodAst> { caller });

            var engine = new Engine(
                new TypeRegistry(new[] { fb }),
                RegistryWith(new StubFunction("F_SumBytes", ctx =>
                    ctx.RequireBytes("pData", 0, ctx.RequireInt32("nSize", 1)).Sum(b => (int)b))));

            var instance = engine.NewInstance("FB_Widget");

            var ex = Assert.ThrowsAny<ArgumentOutOfRangeException>(() =>
                engine.CallMethod(instance, "bDoWork", new Expr[0], new NamedArg[0], null, null));

            Assert.Contains("99 byte(s)", ex.Message);
            Assert.Contains("4 byte(s) available", ex.Message);
        }

        [Fact]
        public void CallMethod_NativeFunctionResolvesFromInsideAGlobalFunctionBody()
        {
            // The TcXunit-kii shape: a global FUNCTION's frame has no FB
            // instance, which is exactly where a thin library wrapper lives.
            var wrapper = new PouAst(
                "F_ComputeChecksum",
                null,
                "FUNCTION F_ComputeChecksum : INT\nVAR_INPUT\n\tnIn : INT;\nEND_VAR",
                "F_ComputeChecksum := F_Triple(nIn);",
                new List<MethodAst>());

            var caller = new MethodAst("bDoWork", "METHOD bDoWork : BOOL", "nResult := F_ComputeChecksum(nValue);");
            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\n\tnValue : INT := 14;\n\tnResult : INT;\nEND_VAR",
                "",
                new List<MethodAst> { caller });

            var engine = new Engine(
                new TypeRegistry(new[] { fb, wrapper }),
                RegistryWith(new StubFunction("F_Triple", ctx => ctx.RequireInt32("nIn", 0) * 3)));

            var instance = engine.NewInstance("FB_Widget");
            engine.CallMethod(instance, "bDoWork", new Expr[0], new NamedArg[0], null, null);

            Assert.Equal(42, instance.Fields["nResult"].Value);
        }

        [Fact]
        public void Register_DuplicateNameThrowsNamingBothSources()
        {
            var registry = new NativeFunctionRegistry();
            registry.Register(new StubFunction("F_Triple", _ => 1), "first.dll");

            var ex = Assert.Throws<InvalidOperationException>(() =>
                registry.Register(new StubFunction("F_Triple", _ => 2), "second.dll"));

            Assert.Contains("first.dll", ex.Message);
            Assert.Contains("second.dll", ex.Message);
        }

        [Fact]
        public void Register_DuplicateNameIsDetectedAcrossCasing()
        {
            var registry = new NativeFunctionRegistry();
            registry.Register(new StubFunction("F_Triple", _ => 1), "first.dll");

            Assert.Throws<InvalidOperationException>(() =>
                registry.Register(new StubFunction("f_triple", _ => 2), "second.dll"));
        }

        [Fact]
        public void EngineWithoutARegistry_KeepsReportingUnresolvedCallsAsErrors()
        {
            // The additive guarantee: an Engine built the old one-argument way
            // behaves exactly as it always did.
            var caller = new MethodAst("bDoWork", "METHOD bDoWork : BOOL", "nResult := F_Triple(nValue);");
            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\n\tnValue : INT := 14;\n\tnResult : INT;\nEND_VAR",
                "",
                new List<MethodAst> { caller });

            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Widget");

            Assert.ThrowsAny<Exception>(() =>
                engine.CallMethod(instance, "bDoWork", new Expr[0], new NamedArg[0], null, null));
        }
    }
}
