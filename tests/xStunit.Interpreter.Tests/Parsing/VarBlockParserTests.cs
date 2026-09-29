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

        // Section keywords are IEC 61131-3 keywords, so TwinCAT accepts them in
        // any case. A header or closer read only in upper case drops the
        // declarations under it, or - for a lower-case closer - leaves the
        // section open so the next header's lines land in the wrong section.
        [Theory]
        [InlineData("var", "end_var", VarSection.Local)]
        [InlineData("var_input", "end_var", VarSection.Input)]
        [InlineData("Var_Output", "End_Var", VarSection.Output)]
        [InlineData("var_in_out", "END_VAR", VarSection.InOut)]
        [InlineData("var_temp", "end_var", VarSection.Temp)]
        [InlineData("Var_Inst", "end_var", VarSection.MethodInstance)]
        [InlineData("var_global constant", "end_var", VarSection.Global)]
        [InlineData("var_input Retain persistent", "end_var", VarSection.Input)]
        [InlineData("struct", "end_struct", VarSection.Local)]
        [InlineData("Union", "End_Union", VarSection.Local)]
        public void Parse_SectionKeywordsInAnyCase_OpenAndCloseTheirSection(string header, string closer, VarSection expected)
        {
            var declaration = $"{header}\n\tnValue : INT;\n{closer}\n\tnOutside : INT;";

            var vars = VarBlockParser.Parse(declaration);

            var value = Assert.Single(vars);
            Assert.Equal("nValue", value.Name);
            Assert.Equal(expected, value.Section);
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

        // VAR_INST must open a section of its own: left unrecognised, every
        // declaration under it is dropped and each use reports an unknown
        // variable, and aliased to Local it would be rebuilt on every call
        // instead of persisting in the instance.
        [Fact]
        public void Parse_VarInstBlock_ReadsNameTypeAndDefaultAsMethodInstanceSection()
        {
            const string declaration = @"METHOD Tick : INT
VAR_INST
	nCount : INT := 10;
END_VAR";

            var vars = VarBlockParser.Parse(declaration);

            var value = Assert.Single(vars);
            Assert.Equal("nCount", value.Name);
            Assert.Equal("INT", value.TypeName);
            Assert.Equal("10", value.DefaultValueText);
            Assert.Equal(VarSection.MethodInstance, value.Section);
        }

        // A motion-control layer is written against library-qualified types,
        // and the registries already see through a qualifier to the bare name.
        // If these go red the whole line fails to match and the variable never
        // exists, so every later use reports "Unknown variable" from somewhere
        // else entirely. The qualifier is passed through verbatim because the
        // declared spelling is what FbInstance and Cell record.
        [Theory]
        [InlineData("fbWriteParam : MotionLib.MC_WriteParameter;", "fbWriteParam", "MotionLib.MC_WriteParameter")]
        [InlineData("fbGroup : KinematicsLib.Group.Handle;", "fbGroup", "KinematicsLib.Group.Handle")]
        [InlineData("axis : motionlib.AXIS_REF;", "axis", "motionlib.AXIS_REF")]
        public void Parse_LibraryQualifiedType_ReadsFullTypeName(
            string line, string expectedName, string expectedTypeName)
        {
            var vars = VarBlockParser.Parse($"VAR\n\t{line}\nEND_VAR");

            var declared = Assert.Single(vars);
            Assert.Equal(expectedName, declared.Name);
            Assert.Equal(expectedTypeName, declared.TypeName);
        }

        // The composite arms have to admit the qualifier too: a qualified type
        // is just as legal as an ARRAY element type or a pointee as it is on
        // its own, and ArrayTypeInfo and AddressTypeInfo both already accept
        // one. Matching only the bare form here would drop those lines while
        // the layers that consume the text would have handled them.
        [Theory]
        [InlineData("axes : ARRAY[0..3] OF MotionLib.AXIS_REF;", "ARRAY[0..3] OF MotionLib.AXIS_REF")]
        [InlineData("pAxis : POINTER TO MotionLib.AXIS_REF;", "POINTER TO MotionLib.AXIS_REF")]
        [InlineData("rAxis : REFERENCE TO MotionLib.AXIS_REF;", "REFERENCE TO MotionLib.AXIS_REF")]
        public void Parse_LibraryQualifiedCompositeType_ReadsFullTypeName(string line, string expectedTypeName)
        {
            var vars = VarBlockParser.Parse($"VAR\n\t{line}\nEND_VAR");

            var declared = Assert.Single(vars);
            Assert.Equal(expectedTypeName, declared.TypeName);
        }

        // The sized-string alternative is listed before the bare-name one so
        // that a STRING sized by a dotted constant is read as a sized string
        // rather than having its qualifier eaten by a name alternative. Pinned
        // because admitting dotted names is exactly the change that could
        // reorder the two and turn STRING(cGvl.MAX) into a plain type name.
        [Theory]
        [InlineData("label : STRING(cScratchConstants.MAX);", "STRING(cScratchConstants.MAX)")]
        [InlineData("labels : ARRAY[0..3] OF WSTRING(cScratchConstants.MAX);", "ARRAY[0..3] OF WSTRING(cScratchConstants.MAX)")]
        public void Parse_SizedStringWithDottedSize_ReadsSizedStringNotPlainType(
            string line, string expectedTypeName)
        {
            var vars = VarBlockParser.Parse($"VAR\n\t{line}\nEND_VAR");

            var declared = Assert.Single(vars);
            Assert.Equal(expectedTypeName, declared.TypeName);
        }

        // A qualified type with an initializer must still split type from
        // default at the ":=", not swallow it: the dotted type group is
        // greedier than the bare-name one it replaces.
        [Fact]
        public void Parse_LibraryQualifiedTypeWithDefault_SplitsTypeFromDefault()
        {
            var vars = VarBlockParser.Parse("VAR\n\tmode : MotionLib.E_Mode := MotionLib.E_Mode.Idle;\nEND_VAR");

            var declared = Assert.Single(vars);
            Assert.Equal("MotionLib.E_Mode", declared.TypeName);
            Assert.Equal("MotionLib.E_Mode.Idle", declared.DefaultValueText);
        }

        // POINTER TO / REFERENCE TO an ARRAY, and REFERENCE TO a sized
        // STRING, are real shapes (a step-timer FB's history buffer, a
        // FUNCTION taking a whole array by reference) - not just the plain
        // pointee/target the address-type alternatives used to require.
        [Theory]
        [InlineData("ipHistory : POINTER TO ARRAY[0..20] OF INT;", "POINTER TO ARRAY[0..20] OF INT")]
        [InlineData("iaParts : REFERENCE TO ARRAY[1..MAX_UNITS_PER_WIDGET] OF ITF_Part;", "REFERENCE TO ARRAY[1..MAX_UNITS_PER_WIDGET] OF ITF_Part")]
        [InlineData("iaSettings : REFERENCE TO ARRAY[1..cScratchConstants.MAX_GADGET_SETTINGS] OF uGadgetSettingValue;", "REFERENCE TO ARRAY[1..cScratchConstants.MAX_GADGET_SETTINGS] OF uGadgetSettingValue")]
        [InlineData("isUtc : REFERENCE TO STRING(32);", "REFERENCE TO STRING(32)")]
        [InlineData("pLabel : POINTER TO STRING(32);", "POINTER TO STRING(32)")]
        public void Parse_AddressToArrayOrSizedString_ReadsFullTypeName(string line, string expectedTypeName)
        {
            var vars = VarBlockParser.Parse($"VAR\n\t{line}\nEND_VAR");

            var declared = Assert.Single(vars);
            Assert.Equal(expectedTypeName, declared.TypeName);
        }

        // A line the pattern cannot match has to be reported, since nothing
        // else will say the variable is gone. Reporting is only worth anything
        // if it separates a declaration that was lost from a line that was
        // never a declaration: the lines matching nothing are overwhelmingly
        // pragmas and comment bodies, and a report drowning in those is one
        // nobody reads.
        [Theory]
        [InlineData("i, j : INT;")]
        [InlineData("DI_KeyPresent AT %I* : BOOL;")]
        [InlineData("state : (INIT, STARTING, WAIT_CONTAINER);")]
        public void Parse_LineItCannotSpell_ReportsItAsUnreadable(string line)
        {
            VarBlockParser.Parse($"VAR\n\t{line}\nEND_VAR", out var unreadable);

            var reported = Assert.Single(unreadable);
            Assert.Equal(line, reported);
        }

        [Fact]
        public void Parse_LinesItCanSpell_ReportNothingAsUnreadable()
        {
            const string declaration = @"FUNCTION_BLOCK FB_Counter
VAR
	value : INT := 5; // seed
	pFloor : POINTER TO INT;
	label : STRING(cConsts.MAX);
END_VAR";

            var vars = VarBlockParser.Parse(declaration, out var unreadable);

            Assert.Equal(3, vars.Count);
            Assert.Empty(unreadable);
        }

        // A pragma is not a declaration, so it is not a lost variable. They
        // outnumber real losses several times over, so reporting them would
        // bury the losses that matter.
        [Theory]
        [InlineData("{attribute 'hide'}")]
        [InlineData("{attribute 'symbol' := 'readwrite'}")]
        [InlineData("{IF defined (VariantCamming)}")]
        [InlineData("{END_IF}")]
        public void Parse_PragmaInsideAnOpenSection_IsNotReportedAsUnreadable(string pragma)
        {
            VarBlockParser.Parse($"VAR\n\t{pragma}\n\tvalue : INT;\nEND_VAR", out var unreadable);

            Assert.Empty(unreadable);
        }

        // A block comment may span several lines, and each line reaches the
        // parser on its own. Its body and closing line are comment text, not
        // lost variables, so neither may be reported as one.
        [Fact]
        public void Parse_MultiLineBlockComment_IsNotReportedAsUnreadable()
        {
            const string declaration = @"FUNCTION_BLOCK FB_Sync
VAR
	(* the transport's real ack-arrival edge. No default -- iAckMessage
	   lives in a receive buffer, so an ungated call replays the pre-drop
	   ack every scan. *)
	obAcked : BOOL;
END_VAR";

            var vars = VarBlockParser.Parse(declaration, out var unreadable);

            var acked = Assert.Single(vars);
            Assert.Equal("obAcked", acked.Name);
            Assert.Empty(unreadable);
        }

        // A declaration sharing a line with the end of a block comment is
        // still a declaration.
        [Fact]
        public void Parse_DeclarationAfterBlockCommentCloses_IsStillRead()
        {
            const string declaration = @"VAR
	(* note
	   continues *) value : INT;
END_VAR";

            var vars = VarBlockParser.Parse(declaration, out var unreadable);

            var value = Assert.Single(vars);
            Assert.Equal("value", value.Name);
            Assert.Equal("INT", value.TypeName);
            Assert.Empty(unreadable);
        }

        // A block comment opening outside any section must not swallow the
        // section header that follows it.
        [Fact]
        public void Parse_MultiLineBlockCommentBeforeSectionHeader_DoesNotSwallowTheBlock()
        {
            const string declaration = @"FUNCTION_BLOCK FB_Sync
(* leading
   note *)
VAR
	value : INT;
END_VAR";

            var vars = VarBlockParser.Parse(declaration, out var unreadable);

            Assert.Equal("value", Assert.Single(vars).Name);
            Assert.Empty(unreadable);
        }

        // Nothing outside an open section is a declaration, so an unmatched
        // line there is not a lost variable either.
        [Fact]
        public void Parse_UnmatchedLineOutsideAnySection_IsNotReportedAsUnreadable()
        {
            const string declaration = @"FUNCTION_BLOCK FB_Counter EXTENDS FB_Base
VAR
	value : INT;
END_VAR";

            VarBlockParser.Parse(declaration, out var unreadable);

            Assert.Empty(unreadable);
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

        // TwinCAT accepts STRUCT/UNION on the TYPE header line as well as on a
        // line of its own. If the header-line spelling opened no field section,
        // every field would vanish without a word.
        [Theory]
        [InlineData("TYPE ST_Point : STRUCT")]
        [InlineData("TYPE ST_Point : UNION")]
        [InlineData("TYPE ST_Point : struct")]
        [InlineData("type ST_Point : Union")]
        [InlineData("TYPE ST_Point:STRUCT")]
        [InlineData("TYPE ST_Point EXTENDS ST_Base : STRUCT")]
        [InlineData("TYPE ST_Point : STRUCT // the point")]
        public void Parse_StructOrUnionOnTypeHeaderLine_ReadsFields(string header)
        {
            var declaration = $"{header}\n\tx : REAL;\n\ty : REAL;\nEND_STRUCT\nEND_TYPE";

            var vars = VarBlockParser.Parse(declaration);

            Assert.Equal(new[] { "x", "y" }, vars.Select(v => v.Name));
            Assert.All(vars, v => Assert.Equal(VarSection.Local, v.Section));
        }

        // The header-line form must close at END_STRUCT like the multi-line
        // form, or a declaration after the TYPE would be read as a field.
        [Fact]
        public void Parse_StructOnTypeHeaderLine_StopsReadingAtEndStruct()
        {
            const string declaration = "TYPE ST_Point : STRUCT\n\tx : REAL;\nEND_STRUCT\nEND_TYPE\n\tnOutside : INT;";

            var value = Assert.Single(VarBlockParser.Parse(declaration));

            Assert.Equal("x", value.Name);
        }

        // Only a STRUCT/UNION body opens a field section from a TYPE header.
        // An ENUM or alias header must not, or the lines after it would be
        // misread as fields.
        [Fact]
        public void Parse_TypeHeaderWithoutStructBody_OpensNoSection()
        {
            const string declaration = "TYPE E_Mode : (Idle, Run);\n\tnOutside : INT;\nEND_TYPE";

            Assert.Empty(VarBlockParser.Parse(declaration));
        }

        // A header that is a whole-word STRUCT/UNION only. An alias to a type
        // whose name merely starts with those letters must not open a section.
        [Theory]
        [InlineData("TYPE T_Alias : STRUCTURED_T;")]
        [InlineData("TYPE T_Alias : UNIONIZED_T;")]
        public void Parse_TypeHeaderNamingTypeThatStartsWithBodyKeyword_OpensNoSection(string header)
        {
            var vars = VarBlockParser.Parse($"{header}\n\tnOutside : INT;\nEND_TYPE");

            Assert.Empty(vars);
        }

        // DutDeclarationPreamble.Strip lets StructDeclParser.DeclaredBody see
        // through a pragma sharing the header's line; the field reader has to
        // agree, or the type is classified a struct and then loses every field.
        [Fact]
        public void Parse_PragmaOnTheTypeHeaderLine_ReadsFields()
        {
            const string declaration = "{attribute 'pack_mode' := '1'} TYPE ST_Point : STRUCT\n\tx : INT;\nEND_STRUCT\nEND_TYPE";

            var value = Assert.Single(VarBlockParser.Parse(declaration));

            Assert.Equal("x", value.Name);
        }

        // A line dropped here leaves no trace until the name is used, so the
        // FB_init argument list must survive as declaration data.
        [Theory]
        [InlineData("fbSetting : FB_GadgetSetting(1.5, 'Speed');", "FB_GadgetSetting", "1.5, 'Speed'")]
        [InlineData("fbNamed : FB_GadgetSetting(ifDefault := 2.5, isLabel := 'Force');", "FB_GadgetSetting", "ifDefault := 2.5, isLabel := 'Force'")]
        [InlineData("fbLib : Lib.FB_Gadget(1);", "Lib.FB_Gadget", "1")]
        [InlineData("fbNested : FB_Gadget(F((1+2)), ')');", "FB_Gadget", "F((1+2)), ')'")]
        [InlineData("fbEmpty : FB_Gadget();", "FB_Gadget", "")]
        [InlineData("fbWide : FB_Gadget(1.0, \"a)b\");", "FB_Gadget", "1.0, \"a)b\"")]
        public void Parse_FbInstanceWithInitArguments_KeepsTypeAndArgumentText(string line, string type, string args)
        {
            var vars = VarBlockParser.Parse("VAR\n\t" + line + "\nEND_VAR", out var unreadable);

            var setting = Assert.Single(vars);
            Assert.Equal(type, setting.TypeName);
            Assert.Equal(args, setting.InitArgumentsText);
            Assert.Empty(unreadable);
        }

        // The argument list must end at its own closing parenthesis, or the
        // struct-literal initialiser after it would be parsed as part of it.
        [Fact]
        public void Parse_InitArgumentsFollowedByInitializer_StopsAtMatchingParenthesis()
        {
            var vars = VarBlockParser.Parse("VAR\n\tfb : FB_X(1) := (fValue := 3.0);\nEND_VAR", out var unreadable);

            var fb = Assert.Single(vars);
            Assert.Equal("1", fb.InitArgumentsText);
            Assert.Equal("(fValue := 3.0)", fb.DefaultValueText);
            Assert.Empty(unreadable);
        }

        // Parentheses belonging to other types must not be read as FB_init
        // arguments: the line stays unreadable, as before init arguments
        // existed, rather than being accepted with wrong meaning.
        [Theory]
        [InlineData("aGadgets : ARRAY[0..1] OF FB_X(1.5,'a');")]
        [InlineData("s : STRING((4+1));")]
        [InlineData("p : POINTER TO FB_X(1);")]
        [InlineData("r : REFERENCE TO FB_X(1);")]
        public void Parse_ParenthesesOnNonInstanceType_StaysUnreadable(string line)
        {
            var vars = VarBlockParser.Parse("VAR\n\t" + line + "\nEND_VAR", out var unreadable);

            Assert.Empty(vars);
            Assert.Equal(new[] { line }, unreadable);
        }

        [Fact]
        public void Parse_SizedString_IsNotTakenForInitArguments()
        {
            var vars = VarBlockParser.Parse("VAR\n\ts : STRING(80);\nEND_VAR", out var unreadable);

            var s = Assert.Single(vars);
            Assert.Equal("STRING(80)", s.TypeName);
            Assert.Null(s.InitArgumentsText);
            Assert.Empty(unreadable);
        }
    }
}
