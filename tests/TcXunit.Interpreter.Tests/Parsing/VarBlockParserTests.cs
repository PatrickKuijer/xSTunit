using System.Linq;
using TcXunit.Interpreter;
using Xunit;

namespace TcXunit.Interpreter.Tests
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

        // TcXunit-71o: a GVL's VAR_GLOBAL block, with leading {attribute
        // ...} pragma lines (ignored since no section is open yet) and no
        // trailing modifier.
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

        // TcXunit-988: a STRING/WSTRING size need not be a bare integer
        // literal - a GVL-qualified constant (e.g.
        // cScratchConstants.MAX_LABEL_STRING_SIZE) is a legal IEC 61131-3 constant
        // expression there too. Previously VarLinePattern only accepted
        // \d+ inside the parens, so the whole line silently failed to
        // match and the field was dropped from the VarDecl list entirely.
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

        // TcXunit-3g7: VAR_TEMP (re-initialized-to-zero-every-call/invocation
        // locals) wasn't recognized by the section-header switch, so its
        // header line fell through to the currentSection == null guard and
        // every declaration inside the block was silently dropped.
        // TcXunit-9go: VAR_TEMP now gets its own VarSection.Temp rather than
        // aliasing to VarSection.Local - a METHOD-scoped VAR_TEMP still
        // resets every call via BindParams (which treats any non-Input/
        // InOut section the same way), but a FUNCTION_BLOCK/PROGRAM's own
        // top-level VAR_TEMP needs to be told apart from a real top-level
        // VAR field so it can be reset before every invocation instead of
        // persisting forever (Engine.ResetTopLevelTempFields).
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
