using System;
using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // ST lets a STRING be indexed directly, 0-based, reading and writing
    // character codes rather than substrings - a STRING behaves as a
    // null-terminated buffer, so index 0 of an empty string reads as the
    // terminator and writing 0 truncates. Unlike an array, the receiver is
    // a CLR string, so every indexing path needs its own handling for it.
    public class StringIndexingTests
    {
        [Fact]
        public void Evaluate_StringIndexExpr_ReadsCharAtIndex()
        {
            var fb = new PouAst("FB_Holder", null, "VAR\n\tsLabel : STRING;\nEND_VAR", "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Holder");
            var frame = new Frame(instance, "FB_Holder");

            engine.ExecuteStatements(Parser.ParseStatements("sLabel := 'AB';"), frame);

            var result = engine.Evaluate(Parser.ParseExpression("sLabel[1]"), frame);

            Assert.Equal((int)'B', result);
        }

        [Fact]
        public void Evaluate_StringIndexExpr_EmptyStringAtZero_ReadsAsTerminator()
        {
            var fb = new PouAst("FB_Holder", null, "VAR\n\tsLabel : STRING;\nEND_VAR", "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Holder");
            var frame = new Frame(instance, "FB_Holder");

            var result = engine.Evaluate(Parser.ParseExpression("sLabel[0] = 0"), frame);

            Assert.Equal(true, result);
        }

        [Fact]
        public void Evaluate_StringIndexExpr_OutOfBoundsThrows()
        {
            var fb = new PouAst("FB_Holder", null, "VAR\n\tsLabel : STRING;\nEND_VAR", "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Holder");
            var frame = new Frame(instance, "FB_Holder");

            engine.ExecuteStatements(Parser.ParseStatements("sLabel := 'AB';"), frame);

            Assert.Throws<IndexOutOfRangeException>(() =>
                engine.Evaluate(Parser.ParseExpression("sLabel[5]"), frame));
        }

        [Fact]
        public void ExecuteStatements_StringIndexAssignment_ReplacesCharInPlace()
        {
            var fb = new PouAst("FB_Holder", null, "VAR\n\tsLabel : STRING;\nEND_VAR", "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Holder");
            var frame = new Frame(instance, "FB_Holder");

            engine.ExecuteStatements(Parser.ParseStatements("sLabel := 'AAA';\nsLabel[1] := 66;"), frame);

            Assert.Equal("ABA", instance.Fields["sLabel"].Value);
        }

        [Fact]
        public void ExecuteStatements_StringIndexAssignment_ZeroTruncatesString()
        {
            var fb = new PouAst("FB_Holder", null, "VAR\n\tsLabel : STRING;\nEND_VAR", "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Holder");
            var frame = new Frame(instance, "FB_Holder");

            engine.ExecuteStatements(Parser.ParseStatements("sLabel := 'ABC';\nsLabel[1] := 0;"), frame);

            Assert.Equal("A", instance.Fields["sLabel"].Value);
        }

        [Fact]
        public void ExecuteStatements_StringIndexAssignment_AppendsAtEnd()
        {
            var fb = new PouAst("FB_Holder", null, "VAR\n\tsLabel : STRING;\nEND_VAR", "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Holder");
            var frame = new Frame(instance, "FB_Holder");

            engine.ExecuteStatements(Parser.ParseStatements("sLabel := 'AB';\nsLabel[2] := 67;"), frame);

            Assert.Equal("ABC", instance.Fields["sLabel"].Value);
        }

        [Fact]
        public void ExecuteStatements_StringIndexAssignment_OutOfBoundsThrows()
        {
            var fb = new PouAst("FB_Holder", null, "VAR\n\tsLabel : STRING;\nEND_VAR", "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Holder");
            var frame = new Frame(instance, "FB_Holder");

            engine.ExecuteStatements(Parser.ParseStatements("sLabel := 'AB';"), frame);

            Assert.Throws<IndexOutOfRangeException>(() =>
                engine.ExecuteStatements(Parser.ParseStatements("sLabel[5] := 88;"), frame));
        }

        // Array indexing shares the evaluation path with string indexing and
        // must stay unaffected by it.
        [Fact]
        public void ExecuteStatements_ArrayIndexAssignment_StillWorksAlongsideStringIndexing()
        {
            var fb = new PouAst("FB_Holder", null, "VAR\n\tbuf : ARRAY[1..3] OF INT;\nEND_VAR", "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Holder");
            var frame = new Frame(instance, "FB_Holder");

            engine.ExecuteStatements(Parser.ParseStatements("buf[2] := 99;"), frame);

            var buf = (ArrayValue)instance.Fields["buf"].Value;
            Assert.Equal(new object[] { 0, 99, 0 }, buf.Elements);
        }

        // A string index also has to work as an lvalue target - the path
        // ADR() and REF= resolve through - not only as a direct read or
        // assignment.
        [Fact]
        public void Evaluate_AdrOfStringIndex_ReadsByteThroughPointerDeref()
        {
            var fb = new PouAst("FB_Holder", null, "VAR\n\tsLabel : STRING;\nEND_VAR", "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Holder");
            var frame = new Frame(instance, "FB_Holder");

            engine.ExecuteStatements(Parser.ParseStatements("sLabel := 'AB';"), frame);

            var result = engine.Evaluate(Parser.ParseExpression("ADR(sLabel[1])^"), frame);

            Assert.Equal((int)'B', result);
        }

        [Fact]
        public void ExecuteStatements_RefAssignToStringIndex_WriteThroughAliasMutatesString()
        {
            var fb = new PouAst(
                "FB_Holder",
                null,
                "VAR\n\tsLabel : STRING;\n\trefByte : REFERENCE TO BYTE;\nEND_VAR",
                "",
                new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Holder");
            var frame = new Frame(instance, "FB_Holder");

            engine.ExecuteStatements(Parser.ParseStatements("sLabel := 'AAA';\nrefByte REF= sLabel[1];"), frame);
            engine.ExecuteStatements(Parser.ParseStatements("refByte := 66;"), frame);

            Assert.Equal("ABA", instance.Fields["sLabel"].Value);
        }
    }
}
