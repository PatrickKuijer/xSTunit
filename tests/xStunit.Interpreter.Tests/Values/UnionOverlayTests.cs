using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // A UNION is overlaid storage, not a struct whose fields happen to share an
    // offset. Every test here would go green on a union whose members were
    // independent Cells only by accident, so each one names a value written
    // through one member and read back through another.
    public class UnionOverlayTests
    {
        // The layout-checklist union: three members of three different widths,
        // so a value that survives the overlay cannot be confused with the
        // first member's, the last member's, or the widest member's own
        // storage.
        private const string OverlaidScalars = @"TYPE U_OverlaidScalars :
UNION
	asWord : WORD;
	asBytes : ARRAY[0..3] OF BYTE;
	asLong : LWORD;
END_UNION
END_TYPE";

        private static (Engine Engine, FbInstance Instance, Frame Frame) NewHolder(
            string varBlock, params string[] declarations)
        {
            var types = new List<StructAst>();
            foreach (var declaration in declarations)
                types.Add(StructDeclParser.Parse(declaration));

            var fb = new PouAst("FB_Holder", null, varBlock, "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }, types));
            var instance = engine.NewInstance("FB_Holder");
            return (engine, instance, new Frame(instance, "FB_Holder"));
        }

        // 16#1234 written through the WORD member has to be the low half of the
        // LWORD member, not a value only asWord can see.
        [Fact]
        public void Assign_NarrowMember_IsVisibleThroughTheWiderMember()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tu : U_OverlaidScalars;\nEND_VAR", OverlaidScalars);

            engine.ExecuteStatements(Parser.ParseStatements("u.asWord := 16#1234;"), frame);

            var u = (StructInstance)instance.Fields["u"].Value;
            Assert.Equal(0x1234UL, u.Fields["asLong"].Value);
        }

        // The other direction, and the one that pins truncation: the WORD
        // member sees only the low two bytes of what the LWORD member wrote,
        // rather than the whole value narrowed by a numeric conversion.
        [Fact]
        public void Assign_WideMember_IsVisibleThroughTheNarrowerMemberTruncated()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tu : U_OverlaidScalars;\nEND_VAR", OverlaidScalars);

            engine.ExecuteStatements(Parser.ParseStatements("u.asLong := 16#7BCD1234;"), frame);

            var u = (StructInstance)instance.Fields["u"].Value;
            Assert.Equal(0x1234, u.Fields["asWord"].Value);
        }

        // Type punning through a byte array is the reason PLC code declares a
        // union at all, and it reaches the overlay by mutating the member's
        // value in place rather than by assigning the member.
        [Fact]
        public void Assign_ByteArrayElement_IsVisibleThroughTheScalarMembers()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tu : U_OverlaidScalars;\nEND_VAR", OverlaidScalars);

            engine.ExecuteStatements(
                Parser.ParseStatements("u.asBytes[0] := 16#34;\nu.asBytes[1] := 16#12;"), frame);

            var u = (StructInstance)instance.Fields["u"].Value;
            Assert.Equal(0x1234, u.Fields["asWord"].Value);
        }

        // And the reverse: a scalar write has to be readable byte by byte,
        // little-endian, which is what makes a union a byte-swapping tool.
        [Fact]
        public void Assign_ScalarMember_IsVisibleThroughTheByteArrayMember()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tu : U_OverlaidScalars;\nEND_VAR", OverlaidScalars);

            engine.ExecuteStatements(Parser.ParseStatements("u.asWord := 16#1234;"), frame);

            var u = (StructInstance)instance.Fields["u"].Value;
            var bytes = (ArrayValue)u.Fields["asBytes"].Value;
            Assert.Equal(new object[] { 0x34, 0x12, 0, 0 }, bytes.Elements);
        }

        // A union nested inside a struct is still overlaid storage: the
        // enclosing struct's field placement is what changes, never the union's
        // own.
        [Fact]
        public void Assign_UnionMemberInsideAStruct_IsVisibleThroughTheOtherMember()
        {
            const string holder = @"TYPE ST_UnionHolder :
STRUCT
	lead : BYTE;
	overlay : U_OverlaidScalars;
END_STRUCT
END_TYPE";
            var (engine, instance, frame) = NewHolder(
                "VAR\n\ts : ST_UnionHolder;\nEND_VAR", OverlaidScalars, holder);

            engine.ExecuteStatements(Parser.ParseStatements("s.overlay.asWord := 16#1234;"), frame);

            var s = (StructInstance)instance.Fields["s"].Value;
            var overlay = (StructInstance)s.Fields["overlay"].Value;
            Assert.Equal(0x1234UL, overlay.Fields["asLong"].Value);
        }

        // A declared initial value on one member initialises the whole overlay,
        // because there is only one storage to initialise.
        [Fact]
        public void Default_MemberInitialValue_IsVisibleThroughTheOtherMembers()
        {
            const string initialised = @"TYPE U_Initialised :
UNION
	asWord : WORD := 16#1234;
	asLong : LWORD;
END_UNION
END_TYPE";
            var (_, instance, _) = NewHolder("VAR\n\tu : U_Initialised;\nEND_VAR", initialised);

            var u = (StructInstance)instance.Fields["u"].Value;

            Assert.Equal(0x1234UL, u.Fields["asLong"].Value);
        }

        // The bytes that leave a union have to be its storage, and the failure
        // here is silent: a pack that loops the members writes each one's own
        // value at offset 0, so the last member declared wins and the copy
        // carries a value nobody wrote.
        [Fact]
        public void Memcpy_OutOfAUnion_WritesTheOverlaidBytesNotTheLastMember()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tu : U_OverlaidScalars;\n\tout : ARRAY[0..7] OF BYTE;\nEND_VAR", OverlaidScalars);

            engine.ExecuteStatements(Parser.ParseStatements("u.asWord := 16#1234;"), frame);
            engine.Evaluate(Parser.ParseExpression("MEMCPY(ADR(out), ADR(u), 8)"), frame);

            var output = (ArrayValue)instance.Fields["out"].Value;
            Assert.Equal(new object[] { 0x34, 0x12, 0, 0, 0, 0, 0, 0 }, output.Elements);
        }

        // The inverse: bytes copied into a union have to be readable through
        // every member, so the aliasing has to survive the round trip out of
        // the byte model and back.
        [Fact]
        public void Memcpy_IntoAUnion_IsVisibleThroughEveryMember()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tu : U_OverlaidScalars;\n\tsrc : ARRAY[0..7] OF BYTE := [16#34, 16#12, 0, 0, 0, 0, 0, 0];\nEND_VAR",
                OverlaidScalars);

            engine.Evaluate(Parser.ParseExpression("MEMCPY(ADR(u), ADR(src), 8)"), frame);

            var u = (StructInstance)instance.Fields["u"].Value;
            Assert.Equal(0x1234, u.Fields["asWord"].Value);
            Assert.Equal(0x1234UL, u.Fields["asLong"].Value);
        }

        // A struct's members are not overlaid, so the same shape of test has to
        // come out the other way. An overlay mechanism that reached every
        // composite would pass every test above and fail only here.
        [Fact]
        public void Assign_StructField_IsNotVisibleThroughItsSibling()
        {
            const string pair = @"TYPE ST_Pair :
STRUCT
	first : WORD;
	second : WORD;
END_STRUCT
END_TYPE";
            var (engine, instance, frame) = NewHolder("VAR\n\ts : ST_Pair;\nEND_VAR", pair);

            engine.ExecuteStatements(Parser.ParseStatements("s.first := 16#1234;"), frame);

            var s = (StructInstance)instance.Fields["s"].Value;
            Assert.Equal(0, s.Fields["second"].Value);
        }
    }
}
