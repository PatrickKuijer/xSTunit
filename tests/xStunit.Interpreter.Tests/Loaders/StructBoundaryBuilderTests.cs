using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    public class StructBoundaryBuilderTests
    {
        private static TypeRegistry NewRegistry(string structDecl) =>
            new TypeRegistry(System.Array.Empty<PouAst>(), new[] { StructDeclParser.Parse(structDecl) });

        [Fact]
        public void Build_NoOverrides_AllFieldsGetInRangeDefault()
        {
            var registry = NewRegistry(@"TYPE ST_Msg :
STRUCT
	count : INT;
	flag : BOOL;
	ratio : REAL;
	label : STRING;
END_STRUCT
END_TYPE");
            var builder = new StructBoundaryBuilder(registry);

            var instance = builder.Build("ST_Msg");

            Assert.Equal(0, instance.Fields["count"].Value);
            Assert.Equal(false, instance.Fields["flag"].Value);
            Assert.Equal(0f, instance.Fields["ratio"].Value);
            Assert.Equal("", instance.Fields["label"].Value);
        }

        [Fact]
        public void Build_NumericFieldOverrideMin_PushesFieldToTypeMinKeepsRestDefault()
        {
            var registry = NewRegistry(@"TYPE ST_Msg :
STRUCT
	count : INT;
	other : INT;
END_STRUCT
END_TYPE");
            var builder = new StructBoundaryBuilder(registry);

            var instance = builder.Build("ST_Msg", ("count", Boundary.Min));

            Assert.Equal(-32768, instance.Fields["count"].Value);
            Assert.Equal(0, instance.Fields["other"].Value);
        }

        [Fact]
        public void Build_NumericFieldOverrideMax_PushesFieldToTypeMax()
        {
            var registry = NewRegistry(@"TYPE ST_Msg :
STRUCT
	count : INT;
END_STRUCT
END_TYPE");
            var builder = new StructBoundaryBuilder(registry);

            var instance = builder.Build("ST_Msg", ("count", Boundary.Max));

            Assert.Equal(32767, instance.Fields["count"].Value);
        }

        [Theory]
        [InlineData("SINT", -128, 127)]
        [InlineData("USINT", 0, 255)]
        [InlineData("BYTE", 0, 255)]
        [InlineData("UINT", 0, 65535)]
        [InlineData("WORD", 0, 65535)]
        [InlineData("DINT", int.MinValue, int.MaxValue)]
        public void Build_NumericTypeOverride_UsesDocumentedIecBounds(string typeName, int min, int max)
        {
            var registry = NewRegistry($@"TYPE ST_Msg :
STRUCT
	v : {typeName};
END_STRUCT
END_TYPE");
            var builder = new StructBoundaryBuilder(registry);

            Assert.Equal(min, builder.Build("ST_Msg", ("v", Boundary.Min)).Fields["v"].Value);
            Assert.Equal(max, builder.Build("ST_Msg", ("v", Boundary.Max)).Fields["v"].Value);
        }

        [Fact]
        public void Build_StringFieldOverrideMin_IsEmptyString()
        {
            var registry = NewRegistry(@"TYPE ST_Msg :
STRUCT
	label : STRING(10);
END_STRUCT
END_TYPE");
            var builder = new StructBoundaryBuilder(registry);

            var instance = builder.Build("ST_Msg", ("label", Boundary.Min));

            Assert.Equal("", instance.Fields["label"].Value);
        }

        [Fact]
        public void Build_StringFieldOverrideMax_IsDeclaredLengthString()
        {
            var registry = NewRegistry(@"TYPE ST_Msg :
STRUCT
	label : STRING(10);
END_STRUCT
END_TYPE");
            var builder = new StructBoundaryBuilder(registry);

            var instance = builder.Build("ST_Msg", ("label", Boundary.Max));

            Assert.Equal(new string('X', 10), instance.Fields["label"].Value);
        }

        [Fact]
        public void Build_BareStringFieldOverrideMax_FallsBackToDefault80Length()
        {
            var registry = NewRegistry(@"TYPE ST_Msg :
STRUCT
	label : STRING;
END_STRUCT
END_TYPE");
            var builder = new StructBoundaryBuilder(registry);

            var instance = builder.Build("ST_Msg", ("label", Boundary.Max));

            Assert.Equal(new string('X', 80), instance.Fields["label"].Value);
        }

        // A STRING size is a constant expression like an ARRAY bound, not
        // necessarily a literal, and the builder has no Engine or Frame to
        // evaluate one with - so both have to share the same resolver.
        [Fact]
        public void Build_StringFieldSizedByGvlQualifiedConstant_MaxIsResolvedLengthString()
        {
            var structAst = StructDeclParser.Parse(@"TYPE ST_Msg :
STRUCT
	label : STRING(cScratchConstants.MAX_LABEL_STRING_SIZE);
END_STRUCT
END_TYPE");
            var gvl = new GvlAst("cScratchConstants", "VAR_GLOBAL CONSTANT\n\tMAX_LABEL_STRING_SIZE : UINT := 32;\nEND_VAR");
            var registry = new TypeRegistry(System.Array.Empty<PouAst>(), new[] { structAst }, new[] { gvl });
            var builder = new StructBoundaryBuilder(registry);

            var instance = builder.Build("ST_Msg", ("label", Boundary.Max));

            Assert.Equal(new string('X', 32), instance.Fields["label"].Value);
        }

        [Fact]
        public void Build_ArrayField_FillsElementsAtInRangeDefaultWithoutOverride()
        {
            var registry = NewRegistry(@"TYPE ST_Msg :
STRUCT
	buf : ARRAY[1..3] OF INT;
END_STRUCT
END_TYPE");
            var builder = new StructBoundaryBuilder(registry);

            var instance = builder.Build("ST_Msg");
            var buf = Assert.IsType<ArrayValue>(instance.Fields["buf"].Value);

            Assert.Equal(new object[] { 0, 0, 0 }, buf.Elements);
        }

        [Fact]
        public void Build_ArrayFieldDirectOverride_ThrowsNotSupported()
        {
            var registry = NewRegistry(@"TYPE ST_Msg :
STRUCT
	buf : ARRAY[1..3] OF INT;
END_STRUCT
END_TYPE");
            var builder = new StructBoundaryBuilder(registry);

            Assert.Throws<System.NotSupportedException>(() => builder.Build("ST_Msg", ("buf", Boundary.Max)));
        }

        [Fact]
        public void Build_NestedStructField_RecursesToItsOwnInRangeDefaults()
        {
            var inner = StructDeclParser.Parse(@"TYPE ST_Point :
STRUCT
	x : INT;
END_STRUCT
END_TYPE");
            var outerDecl = @"TYPE ST_Msg :
STRUCT
	pos : ST_Point;
END_STRUCT
END_TYPE";
            var registry = new TypeRegistry(
                System.Array.Empty<PouAst>(),
                new[] { inner, StructDeclParser.Parse(outerDecl) });
            var builder = new StructBoundaryBuilder(registry);

            var instance = builder.Build("ST_Msg");
            var pos = Assert.IsType<StructInstance>(instance.Fields["pos"].Value);

            Assert.Equal(0, pos.Fields["x"].Value);
        }

        [Fact]
        public void Build_UnknownFieldOverride_ThrowsInvalidOperation()
        {
            var registry = NewRegistry(@"TYPE ST_Msg :
STRUCT
	count : INT;
END_STRUCT
END_TYPE");
            var builder = new StructBoundaryBuilder(registry);

            Assert.Throws<System.InvalidOperationException>(() => builder.Build("ST_Msg", ("nope", Boundary.Max)));
        }

        [Fact]
        public void Build_UnknownStructTypeName_ThrowsInvalidOperation()
        {
            var registry = NewRegistry(@"TYPE ST_Msg :
STRUCT
	count : INT;
END_STRUCT
END_TYPE");
            var builder = new StructBoundaryBuilder(registry);

            Assert.Throws<System.InvalidOperationException>(() => builder.Build("ST_Unknown"));
        }
    }
}
