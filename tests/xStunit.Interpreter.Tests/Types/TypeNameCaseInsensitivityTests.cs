using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // IEC 61131-3 type names are case-insensitive, so 'lreal', 'LReal' and
    // 'LREAL' must take the same path through every type-name lookup site.
    // An ordinal comparison anywhere sends the odd spelling to the
    // plain-scalar fallback instead of its real type.
    public class TypeNameCaseInsensitivityTests
    {
        [Fact]
        public void NewInstance_LowercaseLrealField_DefaultsToDoubleZero()
        {
            var pou = new PouAst(
                "FB_Gain",
                null,
                "VAR\n\tnGain : lreal;\nEND_VAR",
                "",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { pou }));
            var instance = engine.NewInstance("FB_Gain");

            Assert.IsType<double>(instance.Fields["nGain"].Value);
            Assert.Equal(0d, instance.Fields["nGain"].Value);
        }

        [Fact]
        public void NewInstance_MixedCaseLrealField_DefaultsToDoubleZero()
        {
            var pou = new PouAst(
                "FB_Gain",
                null,
                "VAR\n\tnGain : LReal;\nEND_VAR",
                "",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { pou }));
            var instance = engine.NewInstance("FB_Gain");

            Assert.IsType<double>(instance.Fields["nGain"].Value);
            Assert.Equal(0d, instance.Fields["nGain"].Value);
        }

        // A return cell that is not seeded from the declared return type gets
        // created by the first assignment instead: 'M_Read := 0.0;' makes it
        // a REAL, because bare decimal literals lex as REAL, and the later
        // LREAL assignment is then rejected as an implicit narrowing.
        [Fact]
        public void LowercaseLrealMethodReturnType_SeedsReturnCellAndReturnsTheLreal()
        {
            var read = new MethodAst(
                "M_Read",
                "METHOD PRIVATE M_Read : lreal\nVAR\n\ttfValue : LREAL;\nEND_VAR",
                "M_Read := 0.0;\ntfValue := 1.5;\nM_Read := tfValue;");

            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\nEND_VAR",
                "",
                new List<MethodAst>
                {
                    read,
                    new MethodAst("M_Run", "METHOD M_Run : LREAL", "M_Run := M_Read();"),
                });

            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Widget");

            var result = engine.CallMethod(instance, "M_Run", new Expr[0], new NamedArg[0], null, null);

            Assert.Equal(1.5d, result);
        }

        // Deliberately near-identical to the lowercase test above: on its own
        // that one cannot distinguish a case-insensitive comparison from an
        // all-lowercase special case.
        [Fact]
        public void MixedCaseLrealMethodReturnType_SeedsReturnCellAndReturnsTheLreal()
        {
            var read = new MethodAst(
                "M_Read",
                "METHOD PRIVATE M_Read : LReal\nVAR\n\ttfValue : LREAL;\nEND_VAR",
                "M_Read := 0.0;\ntfValue := 1.5;\nM_Read := tfValue;");

            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\nEND_VAR",
                "",
                new List<MethodAst>
                {
                    read,
                    new MethodAst("M_Run", "METHOD M_Run : LREAL", "M_Run := M_Read();"),
                });

            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Widget");

            var result = engine.CallMethod(instance, "M_Run", new Expr[0], new NamedArg[0], null, null);

            Assert.Equal(1.5d, result);
        }

        // The POINTER TO/REFERENCE TO prefix rule is one predicate for the same
        // reason as the rest of this file: an ordinal comparison at one of its
        // call sites and an ignore-case comparison at another let a single
        // declaration be a pointer to one part of the interpreter and an
        // unknown scalar to the next.
        [Theory]
        [InlineData("POINTER TO BYTE")]
        [InlineData("pointer to BYTE")]
        [InlineData("Pointer To BYTE")]
        [InlineData("REFERENCE TO INT")]
        [InlineData("reference to INT")]
        [InlineData("Reference To INT")]
        public void IsAddressType_AnySpellingOfThePrefix_IsRecognised(string typeName)
        {
            Assert.True(AddressTypeInfo.IsAddressType(typeName));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("INT")]
        [InlineData("POINTER")]
        [InlineData("ST_Reference")]
        public void IsAddressType_TypeWithoutThePrefix_IsNotRecognised(string typeName)
        {
            Assert.False(AddressTypeInfo.IsAddressType(typeName));
        }

        // An ALIAS DUT is the one place a lower-case POINTER TO reaches the
        // interpreter today, and it used to reach the two call sites with
        // different answers: a null default from Engine.Defaults, and a refusal
        // to size the very same variable from TypeLayout.
        [Fact]
        public void LowercasePointerAlias_SizesAsAPointerAndDefaultsToNull()
        {
            var fb = new PouAst(
                "FB_Holder",
                null,
                "VAR\n\tpData : PT_Byte;\nEND_VAR",
                "",
                new List<MethodAst>());
            var registry = new TypeRegistry(
                new[] { fb },
                aliases: new[] { new KeyValuePair<string, string>("PT_Byte", "pointer to BYTE") });

            var engine = new Engine(registry);
            var instance = engine.NewInstance("FB_Holder");
            var frame = new Frame(instance, "FB_Holder");

            Assert.Null(instance.Fields["pData"].Value);
            Assert.Equal(
                TargetPlatform.Default.AddressSize,
                engine.Evaluate(Parser.ParseExpression("SIZEOF(pData)"), frame));
        }
    }
}
