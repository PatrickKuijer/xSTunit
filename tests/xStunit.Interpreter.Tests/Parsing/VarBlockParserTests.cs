using System.Linq;
using xStunit.Interpreter;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    public class VarBlockParserTests
    {
        [Fact]
        public void Parse_PlainVarBlock_ReadsNameAndType()
        {
            const string declaration = @"FUNCTION_BLOCK FB_Counter
VAR
	value : INT;
END_VAR";

            var vars = VarBlockParser.Parse(declaration);

            var value = Assert.Single(vars);
            Assert.Equal("value", value.Name);
            Assert.Equal("INT", value.TypeName);
            Assert.Equal(VarSection.Local, value.Section);
            Assert.Null(value.DefaultValueText);
        }

        [Fact]
        public void Parse_VarInputWithDefault_ReadsDefaultAndInputSection()
        {
            const string declaration = @"METHOD PUBLIC Increment
VAR_INPUT
	delta : INT := 1;
END_VAR";

            var vars = VarBlockParser.Parse(declaration);

            var delta = Assert.Single(vars);
            Assert.Equal("delta", delta.Name);
            Assert.Equal("INT", delta.TypeName);
            Assert.Equal(VarSection.Input, delta.Section);
            Assert.Equal("1", delta.DefaultValueText);
        }

        // An unmatched declaration line is skipped silently, so a sized STRING
        // element type the pattern cannot spell does not fail loudly - the
        // variable simply never exists, and every use of it reports "Unknown
        // variable" from somewhere else entirely.
        [Theory]
        [InlineData("labels : ARRAY[0..3] OF STRING(4);", "ARRAY[0..3] OF STRING(4)")]
        [InlineData("labels : ARRAY[0..3] OF WSTRING(cConsts.MAX);", "ARRAY[0..3] OF WSTRING(cConsts.MAX)")]
        public void Parse_ArrayOfSizedString_ReadsTheWholeElementType(string line, string expectedTypeName)
        {
            var vars = VarBlockParser.Parse($"VAR\n\t{line}\nEND_VAR");

            var labels = Assert.Single(vars);
            Assert.Equal("labels", labels.Name);
            Assert.Equal(expectedTypeName, labels.TypeName);
        }

        [Fact]
        public void Parse_PointerAndReferenceTypes_ReadsFullTypeName()
        {
            const string declaration = @"METHOD PUBLIC Decrement
VAR_INPUT
	delta : INT := 1;
END_VAR
VAR
	floor : INT := 0;
	pFloor : POINTER TO INT;
END_VAR";

            var vars = VarBlockParser.Parse(declaration);

            Assert.Equal(3, vars.Count);
            Assert.Equal(VarSection.Input, vars[0].Section);
            Assert.Equal("floor", vars[1].Name);
            Assert.Equal("0", vars[1].DefaultValueText);
            Assert.Equal("pFloor", vars[2].Name);
            Assert.Equal("POINTER TO INT", vars[2].TypeName);
        }

        // IEC 61131-3 type names are case-insensitive, and every downstream
        // consumer of the declared type text already agrees on that. A line
        // this parser fails to match is skipped without a word, so if these go
        // red a directly-declared 'pointer to BYTE' produces no VarDecl at all
        // and every later use of the name reports "Unknown variable" from
        // somewhere else entirely. The spelling is passed through verbatim
        // because the declared text is what FbInstance and Cell record.
        [Theory]
        [InlineData("p : pointer to BYTE;", "pointer to BYTE")]
        [InlineData("p : Pointer To BYTE;", "Pointer To BYTE")]
        [InlineData("r : reference to INT;", "reference to INT")]
        [InlineData("r : Reference To INT;", "Reference To INT")]
        public void Parse_AddressTypeInAnyCase_ReadsFullTypeName(string line, string expectedTypeName)
        {
            var vars = VarBlockParser.Parse($"VAR\n\t{line}\nEND_VAR");

            var declared = Assert.Single(vars);
            Assert.Equal(expectedTypeName, declared.TypeName);
        }

        // The same case-insensitivity, on the composite type arms: ArrayTypeInfo
        // and StringTypeInfo both already match ARRAY/OF/STRING/WSTRING without
        // regard to case, so a spelling they would accept must not be dropped
        // one layer earlier.
        [Theory]
        [InlineData("labels : array[0..3] of INT;", "array[0..3] of INT")]
        [InlineData("labels : Array[0..3] Of string(4);", "Array[0..3] Of string(4)")]
        [InlineData("label : wstring(8);", "wstring(8)")]
        public void Parse_CompositeTypeInAnyCase_ReadsFullTypeName(string line, string expectedTypeName)
        {
            var vars = VarBlockParser.Parse($"VAR\n\t{line}\nEND_VAR");

            var declared = Assert.Single(vars);
            Assert.Equal(expectedTypeName, declared.TypeName);
        }

        [Fact]
        public void Parse_FbInitParams_IncludesStandardAndCustomInputs()
        {
            const string declaration = @"METHOD FB_init
VAR_INPUT
	bInitRetains : BOOL;
	bInCopyCode : BOOL;
	startValue : INT := 0;
END_VAR";

            var vars = VarBlockParser.Parse(declaration);

            Assert.Equal(new[] { "bInitRetains", "bInCopyCode", "startValue" }, vars.Select(v => v.Name));
            Assert.All(vars, v => Assert.Equal(VarSection.Input, v.Section));
        }

        [Fact]
        public void Parse_TrailingLineComment_IsStrippedAndDeclarationIsKept()
        {
            const string declaration = @"FUNCTION_BLOCK FB_TcpDataLoopback
VAR_OUTPUT
	obOk : BOOL; // TRUE while enabled and no table pair reported a mismatch
END_VAR";

            var vars = VarBlockParser.Parse(declaration);

            var obOk = Assert.Single(vars);
            Assert.Equal("obOk", obOk.Name);
            Assert.Equal("BOOL", obOk.TypeName);
            Assert.Equal(VarSection.Output, obOk.Section);
            Assert.Null(obOk.DefaultValueText);
        }

        [Fact]
        public void Parse_TrailingLineComment_WithDefaultValue_IsStrippedAndDefaultKept()
        {
            const string declaration = @"FUNCTION_BLOCK FB_Counter
VAR
	value : INT := 5; // seed value
END_VAR";

            var vars = VarBlockParser.Parse(declaration);

            var value = Assert.Single(vars);
            Assert.Equal("value", value.Name);
            Assert.Equal("5", value.DefaultValueText);
        }

        [Fact]
        public void Parse_StringDefaultContainingSlashSlash_IsNotTreatedAsComment()
        {
            const string declaration = @"FUNCTION_BLOCK FB_Counter
VAR
	url : STRING(80) := 'http://example.com'; // sample URL
END_VAR";

            var vars = VarBlockParser.Parse(declaration);

            var url = Assert.Single(vars);
            Assert.Equal("'http://example.com'", url.DefaultValueText);
        }

        [Fact]
        public void Parse_TrailingBlockComment_IsStrippedAndDeclarationIsKept()
        {
            const string declaration = @"FUNCTION_BLOCK FB_TcpDataLoopback
VAR_OUTPUT
	obOk : BOOL; (* TRUE while enabled *)
END_VAR";

            var vars = VarBlockParser.Parse(declaration);

            var obOk = Assert.Single(vars);
            Assert.Equal("obOk", obOk.Name);
            Assert.Equal("BOOL", obOk.TypeName);
            Assert.Equal(VarSection.Output, obOk.Section);
            Assert.Null(obOk.DefaultValueText);
        }

        [Fact]
        public void Parse_TrailingBlockComment_WithDefaultValue_IsStrippedAndDefaultKept()
        {
            const string declaration = @"FUNCTION_BLOCK FB_Counter
VAR
	value : INT := 5; (* seed value *)
END_VAR";

            var vars = VarBlockParser.Parse(declaration);

            var value = Assert.Single(vars);
            Assert.Equal("value", value.Name);
            Assert.Equal("5", value.DefaultValueText);
        }

        [Fact]
        public void Parse_StringDefaultContainingBlockCommentMarkers_IsNotTreatedAsComment()
        {
            const string declaration = @"FUNCTION_BLOCK FB_Counter
VAR
	note : STRING(80) := 'see (* details *) here'; (* trailing note *)
END_VAR";

            var vars = VarBlockParser.Parse(declaration);

            var note = Assert.Single(vars);
            Assert.Equal("'see (* details *) here'", note.DefaultValueText);
        }

        // Pragma lines appear before any section header, so they are read while
        // no section is open and must not disturb the block that follows.
        [Fact]
        public void Parse_VarGlobalBlockWithAttributePragmas_ReadsGlobalSection()
        {
            const string declaration = @"{attribute 'qualified_only'}
{attribute 'global_init_slot' := '49989'}
VAR_GLOBAL
	stWidget : uWidget;
	stPart : uPart;
END_VAR";

            var vars = VarBlockParser.Parse(declaration);

            Assert.Equal(new[] { "stWidget", "stPart" }, vars.Select(v => v.Name));
            Assert.All(vars, v => Assert.Equal(VarSection.Global, v.Section));
        }

        [Theory]
        [InlineData("VAR_GLOBAL")]
        [InlineData("VAR_GLOBAL CONSTANT")]
        [InlineData("VAR_GLOBAL RETAIN PERSISTENT")]
        public void Parse_VarGlobalWithModifiers_ReadsGlobalSection(string header)
        {
            var declaration = $@"{header}
	value : UINT := 16;
END_VAR";

            var vars = VarBlockParser.Parse(declaration);

            var value = Assert.Single(vars);
            Assert.Equal("value", value.Name);
            Assert.Equal(VarSection.Global, value.Section);
            Assert.Equal("16", value.DefaultValueText);
        }

        // A modifier the header carries must not cost the block: the whole
        // declaration list under it goes missing, and says nothing about why.
        [Theory]
        [InlineData("VAR CONSTANT", VarSection.Local)]
        [InlineData("VAR RETAIN", VarSection.Local)]
        [InlineData("VAR RETAIN PERSISTENT", VarSection.Local)]
        [InlineData("VAR_INPUT CONSTANT", VarSection.Input)]
        [InlineData("VAR_OUTPUT PERSISTENT", VarSection.Output)]
        public void Parse_VarBlockWithModifiers_ReadsTheSectionItsKeywordNames(string header, VarSection expected)
        {
            var declaration = $@"{header}
	Capacity : UINT := 1800;
END_VAR";

            var vars = VarBlockParser.Parse(declaration);

            var capacity = Assert.Single(vars);
            Assert.Equal("Capacity", capacity.Name);
            Assert.Equal(expected, capacity.Section);
            Assert.Equal("1800", capacity.DefaultValueText);
        }

        // A STRING/WSTRING size may be any IEC 61131-3 constant expression, not
        // just an integer literal. A size this parser cannot match costs more
        // than the size itself: the whole declaration line fails to match and
        // the field disappears from the VarDecl list without a word.
        [Fact]
        public void Parse_StringSizedByGvlQualifiedConstant_ReadsFullTypeName()
        {
            const string declaration = @"TYPE uWidgetRegistrationRecord :
STRUCT
	sDefaultValue : STRING(cScratchConstants.MAX_LABEL_STRING_SIZE);
END_STRUCT
END_TYPE";

            var vars = VarBlockParser.Parse(declaration);

            var value = Assert.Single(vars);
            Assert.Equal("sDefaultValue", value.Name);
            Assert.Equal("STRING(cScratchConstants.MAX_LABEL_STRING_SIZE)", value.TypeName);
        }

        // VAR_TEMP gets a section of its own rather than aliasing to Local: a
        // top-level VAR_TEMP is reset before every invocation while a real VAR
        // field persists, and nothing downstream can tell the two apart once
        // the distinction is lost here.
        [Fact]
        public void Parse_VarTempBlock_ReadsNameAndTypeAsTempSection()
        {
            const string declaration = @"METHOD PRIVATE M_UpdateConvergence
VAR_TEMP
	tnRegistered : UINT;
END_VAR";

            var vars = VarBlockParser.Parse(declaration);

            var value = Assert.Single(vars);
            Assert.Equal("tnRegistered", value.Name);
            Assert.Equal("UINT", value.TypeName);
            Assert.Equal(VarSection.Temp, value.Section);
        }

        [Fact]
        public void Parse_WStringSizedByConstArithmeticExpression_ReadsFullTypeName()
        {
            const string declaration = @"FUNCTION_BLOCK FB_Holder
VAR
	label : WSTRING(cScratchConstants.BASE_SIZE * 2);
END_VAR";

            var vars = VarBlockParser.Parse(declaration);

            var value = Assert.Single(vars);
            Assert.Equal("label", value.Name);
            Assert.Equal("WSTRING(cScratchConstants.BASE_SIZE * 2)", value.TypeName);
        }
    }
}
