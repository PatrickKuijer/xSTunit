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
    }
}
