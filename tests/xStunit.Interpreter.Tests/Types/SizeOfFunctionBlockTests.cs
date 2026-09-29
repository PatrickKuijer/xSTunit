using System;
using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // A FUNCTION_BLOCK is sized as its instance data: the hidden vtable
    // pointer first, then every instance member in declaration order, base
    // type first, naturally aligned. See wiki/10-function-block-layout.md.
    public class SizeOfFunctionBlockTests
    {
        private static PouAst Fb(string name, string declarations, string baseTypeName = null) =>
            new PouAst(name, baseTypeName, declarations, "", new List<MethodAst>());

        private static object Evaluate(string expression, TargetPlatform target, params PouAst[] pous)
        {
            var holder = Fb("FB_Holder", "VAR\n\tinstance : FB_Sized;\nEND_VAR");
            var all = new List<PouAst>(pous) { holder };
            var engine = new Engine(new TypeRegistry(all), (Extensibility.NativeFunctionRegistry)null, target);
            var instance = engine.NewInstance("FB_Holder");
            return engine.Evaluate(Parser.ParseExpression(expression), new Frame(instance, "FB_Holder"));
        }

        private static PouAst Sized(string body, string baseTypeName = null) => Fb("FB_Sized", body, baseTypeName);

        [Theory]
        [InlineData("x64", 24)]
        [InlineData("x86", 24)]
        public void SizeOf_FunctionBlockTypeName_CountsVtablePointerThenAlignedMembers(string targetName, int expected)
        {
            Assert.True(TargetPlatform.TryParse(targetName, out var target));
            var sized = Sized("VAR_INPUT\n\tfValue : LREAL;\n\tbEnabled : BOOL;\nEND_VAR");

            Assert.Equal(expected, Evaluate("SIZEOF(FB_Sized)", target, sized));
        }

        [Theory]
        [InlineData("x64", 16)]
        [InlineData("x86", 8)]
        public void SizeOf_FunctionBlockVtablePointer_FollowsTheTargetAddressWidth(string targetName, int expected)
        {
            Assert.True(TargetPlatform.TryParse(targetName, out var target));
            var sized = Sized("VAR\n\tn : DINT;\nEND_VAR");

            Assert.Equal(expected, Evaluate("SIZEOF(FB_Sized)", target, sized));
        }

        [Fact]
        public void SizeOf_FunctionBlockTypeName_EqualsSizeOfItsInstance()
        {
            var sized = Sized("VAR_INPUT\n\tfValue : LREAL;\nEND_VAR\nVAR\n\tb : BYTE;\nEND_VAR");

            Assert.Equal(
                Evaluate("SIZEOF(FB_Sized)", TargetPlatform.X64, sized),
                Evaluate("SIZEOF(instance)", TargetPlatform.X64, sized));
        }

        [Fact]
        public void SizeOf_FunctionBlock_CountsInheritedMembersOnceWithASingleVtablePointer()
        {
            var baseFb = Fb("FB_Base", "VAR\n\tn : DINT;\nEND_VAR");
            var derived = Sized("VAR\n\tb : BYTE;\nEND_VAR", "FB_Base");

            Assert.Equal(8 + 4 + 1 + 3, Evaluate("SIZEOF(FB_Sized)", TargetPlatform.X64, baseFb, derived));
        }

        [Fact]
        public void SizeOf_FunctionBlock_CountsConstantsButNotTemporaries()
        {
            var sized = Sized(
                "VAR\n\tn : DINT;\nEND_VAR\n" +
                "VAR CONSTANT\n\tLIMIT : LREAL := 1.0;\nEND_VAR\n" +
                "VAR_TEMP\n\tscratch : LREAL;\nEND_VAR");

            Assert.Equal(8 + 8 + 8, Evaluate("SIZEOF(FB_Sized)", TargetPlatform.X64, sized));
        }

        [Fact]
        public void SizeOf_FunctionBlock_CountsAnInputDeclaredConstant()
        {
            var sized = Sized("VAR_INPUT CONSTANT\n\tLIMIT : LREAL := 1.0;\nEND_VAR");

            Assert.Equal(8 + 8, Evaluate("SIZEOF(FB_Sized)", TargetPlatform.X64, sized));
        }

        [Theory]
        [InlineData("x64", 16, 24)]
        [InlineData("x86", 8, 16)]
        public void SizeOf_FunctionBlockImplementingAnInterface_PlacesTheFirstMemberBehindAnInterfacePointer(
            string targetName, int expectedFirstMemberOffset, int expectedSize)
        {
            Assert.True(TargetPlatform.TryParse(targetName, out var target));
            var sized = new PouAst(
                "FB_Sized", null, "VAR\n\tn : LREAL;\nEND_VAR", "", new List<MethodAst>(),
                implementedInterfaces: new[] { "I_Logger" });
            var layout = new TypeLayout(new TypeRegistry(new[] { sized }), target);

            var placement = Assert.Single(layout.FunctionBlockFields(sized));

            Assert.Equal(expectedFirstMemberOffset, placement.Offset);
            Assert.Equal(expectedSize, layout.SizeOf("FB_Sized").Size);
        }

        [Fact]
        public void SizeOf_MemberlessFunctionBlockImplementingAnInterface_IsTheTwoHeaderPointers()
        {
            var sized = new PouAst(
                "FB_Sized", null, "", "", new List<MethodAst>(), implementedInterfaces: new[] { "I_Logger" });
            var layout = new TypeLayout(new TypeRegistry(new[] { sized }), TargetPlatform.X64);

            Assert.Equal(16, layout.SizeOf("FB_Sized").Size);
        }

        [Fact]
        public void SizeOf_FunctionBlockImplementingSeveralInterfaces_IsRefusedAsUnverified()
        {
            var sized = new PouAst(
                "FB_Sized", null, "VAR\n\tn : DINT;\nEND_VAR", "", new List<MethodAst>(),
                implementedInterfaces: new[] { "I_A", "I_B" });
            var layout = new TypeLayout(new TypeRegistry(new[] { sized }), TargetPlatform.X64);

            var ex = Assert.Throws<NotSupportedException>(() => layout.SizeOf("FB_Sized"));

            Assert.Contains("FB_Sized", ex.Message);
            Assert.Contains("interfaces", ex.Message);
        }

        [Fact]
        public void SizeOf_FunctionBlockExtendingAnUnregisteredType_NamesTheMissingBase()
        {
            var derived = Sized("VAR\n\tb : BYTE;\nEND_VAR", "TcUnit.FB_TestSuite");
            var layout = new TypeLayout(new TypeRegistry(new[] { derived }), TargetPlatform.X64);

            var ex = Assert.Throws<NotSupportedException>(() => layout.SizeOf("FB_Sized"));

            Assert.Contains("FB_Sized", ex.Message);
            Assert.Contains("TcUnit.FB_TestSuite", ex.Message);
        }

        [Fact]
        public void SizeOf_FunctionBlocksExtendingEachOther_IsRefusedRatherThanSized()
        {
            var a = Fb("FB_A", "VAR\n\tn : DINT;\nEND_VAR", "FB_B");
            var b = Fb("FB_B", "VAR\n\tn : DINT;\nEND_VAR", "FB_A");
            var layout = new TypeLayout(new TypeRegistry(new[] { a, b }), TargetPlatform.X64);

            var ex = Assert.Throws<NotSupportedException>(() => layout.SizeOf("FB_A"));

            Assert.Contains("EXTENDS", ex.Message);
        }

        [Fact]
        public void SizeOf_FunctionBlockWithANativeFunctionBlockMember_NamesTheContainerAndTheGap()
        {
            var sized = Sized("VAR\n\ttimer : TON;\nEND_VAR");
            var layout = new TypeLayout(new TypeRegistry(new[] { sized }), TargetPlatform.X64);

            var ex = Assert.Throws<NotSupportedException>(() => layout.SizeOf("FB_Sized"));

            Assert.Contains("FB_Sized", ex.Message);
            Assert.Contains("timer", ex.Message);
            Assert.Contains("native", ex.Message);
        }

        [Fact]
        public void SizeOf_FunctionBlockWithAnInterfaceMember_IsRefused()
        {
            var sized = Sized("VAR\n\tlogger : I_Logger;\nEND_VAR");
            var itf = new InterfaceAst("I_Logger", "INTERFACE I_Logger", null, null);
            var layout = new TypeLayout(new TypeRegistry(new[] { sized }, interfaceTypes: new[] { itf }), TargetPlatform.X64);

            var ex = Assert.Throws<NotSupportedException>(() => layout.SizeOf("FB_Sized"));

            Assert.Contains("I_Logger", ex.Message);
            Assert.Contains("interface", ex.Message);
        }

        [Theory]
        [InlineData(PouKind.Program)]
        [InlineData(PouKind.Function)]
        public void SizeOf_ProgramOrFunctionName_IsRefusedAsNotAFunctionBlock(PouKind kind)
        {
            var pou = new PouAst(
                "Prg_Main", null, "VAR\n\tn : DINT;\nEND_VAR", "", new List<MethodAst>(), kind: kind);
            var layout = new TypeLayout(new TypeRegistry(new[] { pou }), TargetPlatform.X64);

            var ex = Assert.Throws<NotSupportedException>(() => layout.SizeOf("Prg_Main"));

            Assert.Contains("FUNCTION_BLOCK", ex.Message);
        }

        [Fact]
        public void SizeOf_FunctionBlockWhoseMethodDeclaresVarInst_IsRefusedNamingTheMethod()
        {
            var method = new MethodAst("Run", "METHOD Run\nVAR_INST\n\tticks : DINT;\nEND_VAR", "");
            var sized = new PouAst(
                "FB_Sized", null, "VAR\n\tn : DINT;\nEND_VAR", "", new List<MethodAst> { method });
            var layout = new TypeLayout(new TypeRegistry(new[] { sized }), TargetPlatform.X64);

            var ex = Assert.Throws<NotSupportedException>(() => layout.SizeOf("FB_Sized"));

            Assert.Contains("FB_Sized", ex.Message);
            Assert.Contains("Run", ex.Message);
            Assert.Contains("VAR_INST", ex.Message);
        }

        [Fact]
        public void SizeOf_FunctionBlockInheritingAMethodWithVarInst_IsRefused()
        {
            var method = new MethodAst("Run", "METHOD Run\nVAR_INST\n\tticks : DINT;\nEND_VAR", "");
            var baseFb = new PouAst("FB_Base", null, "VAR\nEND_VAR", "", new List<MethodAst> { method });
            var derived = Sized("VAR\n\tn : DINT;\nEND_VAR", "FB_Base");
            var layout = new TypeLayout(new TypeRegistry(new[] { baseFb, derived }), TargetPlatform.X64);

            var ex = Assert.Throws<NotSupportedException>(() => layout.SizeOf("FB_Sized"));

            Assert.Contains("FB_Base", ex.Message);
            Assert.Contains("Run", ex.Message);
        }

        [Fact]
        public void SizeOf_NativeMemberBuriedInANestedFunctionBlock_SaysNativeExactlyOnce()
        {
            var inner = Fb("FB_Inner", "VAR\n\ttimer : TON;\nEND_VAR");
            var outer = Sized("VAR\n\tinner : FB_Inner;\nEND_VAR");
            var layout = new TypeLayout(new TypeRegistry(new[] { inner, outer }), TargetPlatform.X64);

            var ex = Assert.Throws<NotSupportedException>(() => layout.SizeOf("FB_Sized"));

            Assert.Contains("FB_Inner", ex.Message);
            Assert.Contains("FB_Sized", ex.Message);
            Assert.Equal(1, CountOccurrences(ex.Message, "native library function blocks are not modelled"));
        }

        [Fact]
        public void SizeOf_MemberThatContainsItsOwner_DoesNotBlameNativeFunctionBlocks()
        {
            var a = Fb("FB_A", "VAR\n\tb : FB_B;\nEND_VAR");
            var b = Fb("FB_B", "VAR\n\ta : FB_A;\nEND_VAR");
            var layout = new TypeLayout(new TypeRegistry(new[] { a, b }), TargetPlatform.X64);

            var ex = Assert.Throws<NotSupportedException>(() => layout.SizeOf("FB_A"));

            Assert.Contains("contains itself", ex.Message);
            Assert.DoesNotContain("native", ex.Message);
        }

        [Fact]
        public void SizeOf_MemberWhoseBaseTypeIsUnloaded_SaysNativeExactlyOnce()
        {
            var inner = Fb("FB_Inner", "VAR\n\tn : DINT;\nEND_VAR", "TcUnit.FB_TestSuite");
            var outer = Sized("VAR\n\tinner : FB_Inner;\nEND_VAR");
            var layout = new TypeLayout(new TypeRegistry(new[] { inner, outer }), TargetPlatform.X64);

            var ex = Assert.Throws<NotSupportedException>(() => layout.SizeOf("FB_Sized"));

            Assert.Contains("TcUnit.FB_TestSuite", ex.Message);
            Assert.Equal(1, CountOccurrences(ex.Message, "native library function blocks are not modelled"));
        }

        private static int CountOccurrences(string text, string fragment)
        {
            var count = 0;
            for (var at = text.IndexOf(fragment, StringComparison.Ordinal); at >= 0;
                at = text.IndexOf(fragment, at + fragment.Length, StringComparison.Ordinal))
                count++;
            return count;
        }

        [Fact]
        public void SizeOf_FunctionBlock_CountsInOutAsAnAddress()
        {
            var sized = Sized("VAR_IN_OUT\n\tbig : ARRAY[0..99] OF LREAL;\nEND_VAR");

            Assert.Equal(16, Evaluate("SIZEOF(FB_Sized)", TargetPlatform.X64, sized));
        }

        [Fact]
        public void SizeOf_FunctionBlock_IncludesANestedFunctionBlockMember()
        {
            var inner = Fb("FB_Inner", "VAR\n\tn : DINT;\nEND_VAR");
            var sized = Sized("VAR\n\tb : BYTE;\n\tinner : FB_Inner;\nEND_VAR");

            Assert.Equal(8 + 8 + 16, Evaluate("SIZEOF(FB_Sized)", TargetPlatform.X64, inner, sized));
        }

        [Fact]
        public void SizeOf_FunctionBlockInItsOwnConstantInitializer_ResolvesWithoutRecursing()
        {
            var sized = Sized(
                "VAR_INPUT\n\tfValue : LREAL;\n\tbEnabled : BOOL;\nEND_VAR\n" +
                "VAR CONSTANT\n\tSETTING_SIZE : UDINT := SIZEOF(FB_Sized);\nEND_VAR");

            Assert.Equal(24L, Evaluate("instance.SETTING_SIZE", TargetPlatform.X64, sized));
        }

        [Fact]
        public void SizeOf_FunctionBlockContainingItself_IsRefusedRatherThanRecursedForever()
        {
            var sized = Sized("VAR\n\tagain : FB_Sized;\nEND_VAR");
            var registry = new TypeRegistry(new[] { sized });
            var layout = new TypeLayout(registry, TargetPlatform.X64);

            Assert.Throws<NotSupportedException>(() => layout.SizeOf("FB_Sized"));
        }
    }
}
