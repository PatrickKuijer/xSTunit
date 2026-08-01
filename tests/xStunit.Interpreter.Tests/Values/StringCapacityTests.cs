using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // A STRING(n) declaration reserves n characters plus a terminator, and
    // TwinCAT drops whatever a longer assignment does not fit. Without that,
    // the interpreter and the byte model disagree about the same variable: the
    // wire format has always truncated at the declared length, so a value could
    // come back from an ADR() round trip shorter than it went in.
    public class StringCapacityTests
    {
        private static (Engine Engine, FbInstance Instance, Frame Frame) NewHolder(string varBlock)
        {
            var fb = new PouAst("FB_Holder", null, varBlock, "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Holder");
            return (engine, instance, new Frame(instance, "FB_Holder"));
        }

        [Fact]
        public void ExecuteStatements_AssignLongerThanDeclaredCapacity_KeepsTheLeadingCharacters()
        {
            var (engine, instance, frame) = NewHolder("VAR\n\ts : STRING(4);\nEND_VAR");

            engine.ExecuteStatements(Parser.ParseStatements("s := 'abcdefghij';"), frame);

            Assert.Equal("abcd", instance.Fields["s"].Value);
        }

        [Fact]
        public void NewInstance_InitialValueLongerThanDeclaredCapacity_IsTruncatedAtDeclaration()
        {
            var (_, instance, _) = NewHolder("VAR\n\ts : STRING(4) := 'abcdefghij';\nEND_VAR");

            Assert.Equal("abcd", instance.Fields["s"].Value);
        }

        [Fact]
        public void ExecuteStatements_AssignWithinDeclaredCapacity_IsUnchanged()
        {
            var (engine, instance, frame) = NewHolder("VAR\n\ts : STRING(4);\nEND_VAR");

            engine.ExecuteStatements(Parser.ParseStatements("s := 'abcd';"), frame);

            Assert.Equal("abcd", instance.Fields["s"].Value);
        }

        // An unsized STRING is STRING(80) in TwinCAT, so it has a capacity like
        // any other - just a generous one.
        [Fact]
        public void ExecuteStatements_BareStringBeyondEightyCharacters_TruncatesAtEighty()
        {
            var (engine, instance, frame) = NewHolder("VAR\n\ts : STRING;\nEND_VAR");

            engine.ExecuteStatements(Parser.ParseStatements($"s := '{new string('x', 100)}';"), frame);

            Assert.Equal(80, ((string)instance.Fields["s"].Value).Length);
        }

        // A WSTRING's declared size is a character count too, so it truncates on
        // the same boundary despite occupying twice the bytes.
        [Fact]
        public void ExecuteStatements_WStringLongerThanDeclaredCapacity_TruncatesByCharacters()
        {
            var (engine, instance, frame) = NewHolder("VAR\n\ts : WSTRING(3);\nEND_VAR");

            engine.ExecuteStatements(Parser.ParseStatements("s := \"abcdef\";"), frame);

            Assert.Equal("abc", instance.Fields["s"].Value);
        }

        [Fact]
        public void ExecuteStatements_StructStringField_TruncatesAtTheFieldsDeclaredCapacity()
        {
            var structType = StructDeclParser.Parse(@"TYPE ST_Msg :
STRUCT
	name : STRING(3);
END_STRUCT
END_TYPE");
            var fb = new PouAst("FB_Holder", null, "VAR\n\tm : ST_Msg;\nEND_VAR", "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }, new[] { structType }));
            var instance = engine.NewInstance("FB_Holder");
            var frame = new Frame(instance, "FB_Holder");

            engine.ExecuteStatements(Parser.ParseStatements("m.name := 'abcdef';"), frame);

            var m = (StructInstance)instance.Fields["m"].Value;
            Assert.Equal("abc", m.Fields["name"].Value);
        }

        // The whole reason capacity has to live on the Cell: the byte model
        // already truncated at the declared length, so before this the same
        // variable had one length in the interpreter and another after a round
        // trip through ADR().
        [Fact]
        public void Memcpy_RoundTripOfAnOverlongAssignment_MatchesTheInterpretersOwnValue()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\ts : STRING(3);\n\tbuf : ARRAY[0..3] OF BYTE;\nEND_VAR");

            engine.ExecuteStatements(Parser.ParseStatements("s := 'abcdef';"), frame);
            var beforeRoundTrip = instance.Fields["s"].Value;

            engine.Evaluate(Parser.ParseExpression("MEMCPY(ADR(buf), ADR(s), 4)"), frame);
            engine.ExecuteStatements(Parser.ParseStatements("s := '';"), frame);
            engine.Evaluate(Parser.ParseExpression("MEMCPY(ADR(s), ADR(buf), 4)"), frame);

            Assert.Equal(beforeRoundTrip, instance.Fields["s"].Value);
        }

        // s[n] bounds-checks against the current content, so appending one past
        // the last character is in range even at capacity - the capacity is
        // what stops the string growing, applied when the rebuilt value lands
        // back on the parent Cell.
        [Fact]
        public void ExecuteStatements_StringIndexAppendAtCapacity_DoesNotGrowTheString()
        {
            var (engine, instance, frame) = NewHolder("VAR\n\ts : STRING(2) := 'ab';\nEND_VAR");

            engine.ExecuteStatements(Parser.ParseStatements("s[2] := 67;"), frame);

            Assert.Equal("ab", instance.Fields["s"].Value);
        }

        // Capacity is a STRING concern only: nothing else may start losing
        // values through the same setter.
        [Fact]
        public void ExecuteStatements_NonStringVariable_IsNotClamped()
        {
            var (engine, instance, frame) = NewHolder("VAR\n\tn : DINT;\nEND_VAR");

            engine.ExecuteStatements(Parser.ParseStatements("n := 123456;"), frame);

            Assert.Equal(123456, instance.Fields["n"].Value);
        }
    }
}
