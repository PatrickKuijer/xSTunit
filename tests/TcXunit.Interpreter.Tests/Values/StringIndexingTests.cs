using System;
using System.Collections.Generic;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-3jr: TwinCAT ST lets a STRING be indexed directly (s[n], 0-
    // based) to read/write individual bytes - most commonly "IF s[0] = 0
    // THEN" to test for an empty string, since STRING is a null-terminated
    // byte buffer internally. IndexExpr evaluation used to assume every
    // indexed receiver was an ArrayValue and threw InvalidCastException the
    // moment it was handed a STRING (a System.String) instead.
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

            // The idiom straight out of TcXunit-3jr's repro: an empty
            // string's first byte reads as the null terminator, not a crash.
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

        // Existing ArrayValue indexing must keep working unchanged now that
        // IndexExpr evaluation branches on the receiver's runtime type.
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

        // The original TcXunit-3jr crash traced through the third
        // unconditional-cast site named in the bead - ResolveCellForLValue
        // (Engine.Cells.cs), the ADR()/REF=/Transmit() target resolver, not
        // just plain read/assignment - since real FB_init chains commonly
        // wire things up via REF= rather than direct index reads/writes.
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
