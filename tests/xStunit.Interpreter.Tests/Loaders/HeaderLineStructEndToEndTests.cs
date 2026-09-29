using System.Collections.Generic;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // A struct declared as "TYPE X : STRUCT" must be usable by a body, not
    // merely recognised: the field read has to see the declared default,
    // which it cannot if the field list was dropped on the way through.
    public class HeaderLineStructEndToEndTests
    {
        private static bool BodyReadsField(string declaration, string typeName, string fieldRead)
        {
            var suite = new PouAst(
                "FB_MySuite",
                "TcUnit.FB_TestSuite",
                $"VAR\n\tv : {typeName};\nEND_VAR",
                "TEST('t');\n" +
                $"AssertTrue({fieldRead}, 'field is readable');\n" +
                "TEST_FINISHED();",
                new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { suite }, new[] { StructDeclParser.Parse(declaration) }));

            return Assert.Single(engine.RunSuite("FB_MySuite")).Passed;
        }

        [Theory]
        [InlineData("TYPE ST_Point : STRUCT\n\tnX : DINT := 42;\nEND_STRUCT\nEND_TYPE")]
        [InlineData("TYPE ST_Point : struct\n\tnX : DINT := 42;\nend_struct\nend_type")]
        [InlineData("TYPE ST_Point :\nSTRUCT\n\tnX : DINT := 42;\nEND_STRUCT\nEND_TYPE")]
        public void RunSuite_BodyReadsFieldOfStructDeclaredWithBodyKeyword_SeesTheField(string declaration)
        {
            Assert.True(BodyReadsField(declaration, "ST_Point", "v.nX = 42"));
        }

        [Fact]
        public void RunSuite_BodyReadsFieldOfUnionOnTypeHeaderLine_SeesTheField()
        {
            const string declaration = "TYPE U_Word : UNION\n\tnW : WORD := 5;\nEND_UNION\nEND_TYPE";

            Assert.True(BodyReadsField(declaration, "U_Word", "v.nW = 5"));
        }

        [Fact]
        public void RunSuite_BodyReadsFieldOfStructWhosePragmaSharesTheHeaderLine_SeesTheField()
        {
            const string declaration = "{attribute 'pack_mode' := '1'} TYPE ST_Point : STRUCT\n\tnX : DINT := 42;\nEND_STRUCT\nEND_TYPE";

            Assert.True(BodyReadsField(declaration, "ST_Point", "v.nX = 42"));
        }
    }
}
