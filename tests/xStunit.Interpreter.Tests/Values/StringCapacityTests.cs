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
        private static (Engine Engine, FbInstance Instance, Frame Frame) NewHolder(
            string varBlock,
            IEnumerable<GvlAst> gvls = null,
            IEnumerable<KeyValuePair<string, string>> aliases = null)
        {
            var fb = new PouAst("FB_Holder", null, varBlock, "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }, gvls: gvls, aliases: aliases));
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

        // An ALIAS DUT is the declared type as far as the variable is concerned,
        // so the capacity behind it binds exactly as an inline STRING(n) would.
        // The byte model already resolves the alias when it packs, so anything
        // less puts the interpreter and an ADR() round trip back into
        // disagreement.
        [Fact]
        public void ExecuteStatements_AliasTypedString_TruncatesAtTheAliasedCapacity()
        {
            var aliases = new[] { new KeyValuePair<string, string>("T_Label", "STRING(4)") };
            var (engine, instance, frame) = NewHolder("VAR\n\ts : T_Label;\nEND_VAR", aliases: aliases);

            engine.ExecuteStatements(Parser.ParseStatements("s := 'abcdefghij';"), frame);

            Assert.Equal("abcd", instance.Fields["s"].Value);
        }

        // A STRING size is an IEC constant expression, not just a literal, and
        // SIZEOF/PackValue already evaluate one. Truncation has to agree with
        // them or the same declaration means two different capacities.
        [Fact]
        public void ExecuteStatements_ConstantSizedString_TruncatesAtTheResolvedCapacity()
        {
            var gvl = new GvlAst("cScratchConstants", "VAR_GLOBAL CONSTANT\n\tMAX_LABEL_SIZE : UINT := 4;\nEND_VAR");
            var (engine, instance, frame) = NewHolder(
                "VAR\n\ts : STRING(cScratchConstants.MAX_LABEL_SIZE);\nEND_VAR", gvls: new[] { gvl });

            engine.ExecuteStatements(Parser.ParseStatements("s := 'abcdefghij';"), frame);

            Assert.Equal("abcd", instance.Fields["s"].Value);
        }

        // An array element is a declaration of the element type, and PackValue
        // truncates each element on the wire, so an element assignment that kept
        // its whole value would reproduce the interpreter-vs-byte-model
        // disagreement one level down.
        [Fact]
        public void ExecuteStatements_ArrayOfStringElement_TruncatesAtTheElementCapacity()
        {
            var (engine, instance, frame) = NewHolder("VAR\n\tlabels : ARRAY[0..3] OF STRING(4);\nEND_VAR");

            engine.ExecuteStatements(Parser.ParseStatements("labels[1] := 'abcdefghij';"), frame);

            var labels = (ArrayValue)instance.Fields["labels"].Value;
            Assert.Equal("abcd", labels.Elements[1]);
        }

        [Fact]
        public void ExecuteStatements_ArrayOfConstantSizedStringElement_TruncatesAtTheResolvedCapacity()
        {
            var gvl = new GvlAst("cScratchConstants", "VAR_GLOBAL CONSTANT\n\tMAX_LABEL_SIZE : UINT := 4;\nEND_VAR");
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tlabels : ARRAY[0..3] OF STRING(cScratchConstants.MAX_LABEL_SIZE);\nEND_VAR",
                gvls: new[] { gvl });

            engine.ExecuteStatements(Parser.ParseStatements("labels[0] := 'abcdefghij';"), frame);

            var labels = (ArrayValue)instance.Fields["labels"].Value;
            Assert.Equal("abcd", labels.Elements[0]);
        }

        // An array literal initializer is a write into the same elements, so it
        // truncates on the same boundary an assignment would.
        [Fact]
        public void NewInstance_ArrayOfStringInitializer_TruncatesEachElementAtDeclaration()
        {
            var (_, instance, _) = NewHolder(
                "VAR\n\tlabels : ARRAY[0..1] OF STRING(4) := ['abcdefghij', 'klmnopqrst'];\nEND_VAR");

            var labels = (ArrayValue)instance.Fields["labels"].Value;
            Assert.Equal(new object[] { "abcd", "klmn" }, labels.Elements);
        }

        // A VAR_INPUT of STRUCT/ARRAY type is deep-copied into the callee, and
        // the copy is a declaration of the same type: dropping the capacity on
        // the way in would let the copy outgrow what its source could hold.
        [Fact]
        public void CloneValue_ArrayOfString_KeepsTheElementCapacity()
        {
            var (_, instance, _) = NewHolder("VAR\n\tlabels : ARRAY[0..1] OF STRING(4);\nEND_VAR");

            var clone = (ArrayValue)CellCloner.CloneValue(instance.Fields["labels"].Value);
            clone.SetElement(0, "abcdefghij");

            Assert.Equal("abcd", clone.Elements[0]);
        }

        [Fact]
        public void ExecuteStatements_ArrayOfAliasTypedStringElement_TruncatesAtTheAliasedCapacity()
        {
            var aliases = new[] { new KeyValuePair<string, string>("T_Label", "STRING(4)") };
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tlabels : ARRAY[0..3] OF T_Label;\nEND_VAR", aliases: aliases);

            engine.ExecuteStatements(Parser.ParseStatements("labels[2] := 'abcdefghij';"), frame);

            var labels = (ArrayValue)instance.Fields["labels"].Value;
            Assert.Equal("abcd", labels.Elements[2]);
        }

        // A method's local STRING is as much a declaration as a field is, and it
        // is the form a fixture writing into a scratch buffer hits first.
        [Fact]
        public void CallMethod_LocalAliasTypedString_TruncatesAtTheAliasedCapacity()
        {
            var aliases = new[] { new KeyValuePair<string, string>("T_Label", "STRING(4)") };
            var method = new MethodAst(
                "Shorten",
                "METHOD PUBLIC Shorten : STRING\nVAR\n\tlocal : T_Label;\nEND_VAR",
                "local := 'abcdefghij';\nShorten := local;");
            var fb = new PouAst("FB_Holder", null, "VAR\nEND_VAR", "", new List<MethodAst> { method });
            var engine = new Engine(new TypeRegistry(new[] { fb }, aliases: aliases));
            var instance = engine.NewInstance("FB_Holder");

            var result = engine.CallMethod(instance, "Shorten", new Expr[0], new NamedArg[0], null, null);

            Assert.Equal("abcd", result);
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
