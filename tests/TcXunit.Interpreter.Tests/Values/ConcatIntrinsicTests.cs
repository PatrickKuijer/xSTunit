using System;
using System.Collections.Generic;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-3lt: CONCAT is a standard IEC 61131-3 string function (TwinCAT
    // extends it to a variadic STR1..STR10 form, Tc2_Standard) with no
    // intrinsic dispatch in EvaluateCall - a bare CONCAT(...) call fell
    // through to CallMethod and threw "Method 'CONCAT' not found starting
    // from type '<fb>'" (surfaced running tcxunit against a real-work POU,
    // FB_RemotePparServer).
    public class ConcatIntrinsicTests
    {
        private static (Engine Engine, FbInstance Instance, Frame Frame) NewHolder(string varBlock)
        {
            var fb = new PouAst("FB_Holder", null, varBlock, "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Holder");
            return (engine, instance, new Frame(instance, "FB_Holder"));
        }

        [Fact]
        public void Concat_TwoStringLiterals_ReturnsConcatenation()
        {
            var (engine, _, frame) = NewHolder("VAR\nEND_VAR");

            var result = engine.Evaluate(Parser.ParseExpression("CONCAT('foo', 'bar')"), frame);

            Assert.Equal("foobar", result);
        }

        [Fact]
        public void Concat_ThreeArgs_ConcatenatesInOrder()
        {
            var (engine, _, frame) = NewHolder("VAR\n\tsA : STRING := 'a';\n\tsB : STRING := 'b';\n\tsC : STRING := 'c';\nEND_VAR");

            var result = engine.Evaluate(Parser.ParseExpression("CONCAT(sA, sB, sC)"), frame);

            Assert.Equal("abc", result);
        }

        [Fact]
        public void Concat_NamedArgsOutOfOrder_ResolvesByDeclaredPosition()
        {
            var (engine, _, frame) = NewHolder("VAR\nEND_VAR");

            var result = engine.Evaluate(Parser.ParseExpression("CONCAT(STR2 := 'bar', STR1 := 'foo')"), frame);

            Assert.Equal("foobar", result);
        }

        [Fact]
        public void Concat_MissingStr2_ThrowsRequiredArgumentException()
        {
            var (engine, _, frame) = NewHolder("VAR\nEND_VAR");

            var ex = Assert.Throws<InvalidOperationException>(
                () => engine.Evaluate(Parser.ParseExpression("CONCAT('foo')"), frame));

            Assert.Contains("STR2", ex.Message);
        }

        [Fact]
        public void Concat_NonStringArg_Throws()
        {
            var (engine, _, frame) = NewHolder("VAR\nEND_VAR");

            Assert.Throws<NotSupportedException>(
                () => engine.Evaluate(Parser.ParseExpression("CONCAT('foo', 1)"), frame));
        }

        [Fact]
        public void Concat_CalledFromMethodBody_Resolves()
        {
            var caller = new MethodAst(
                "bBuild",
                "METHOD bBuild : BOOL",
                "sResult := CONCAT(sPrefix, sSuffix);");

            var fb = new PouAst(
                "FB_RemotePparServer",
                null,
                "VAR\n\tsPrefix : STRING := 'ppar_';\n\tsSuffix : STRING := 'server';\n\tsResult : STRING;\nEND_VAR",
                "",
                new List<MethodAst> { caller });

            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_RemotePparServer");

            engine.CallMethod(instance, "bBuild", new Expr[0], new NamedArg[0], null, null);

            Assert.Equal("ppar_server", instance.Fields["sResult"].Value);
        }
    }
}
