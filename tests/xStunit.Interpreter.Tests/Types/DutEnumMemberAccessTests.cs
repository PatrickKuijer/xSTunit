using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    public class DutEnumMemberAccessTests
    {
        private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> EnumMembers(
            string enumName, params (string Name, int Value)[] members)
        {
            var table = new Dictionary<string, int>();
            foreach (var member in members)
                table[member.Name] = member.Value;

            return new Dictionary<string, IReadOnlyDictionary<string, int>> { [enumName] = table };
        }

        private static Engine NewEngine(
            string implementation,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> enumMembers,
            string varBlock = "VAR\n\tresult : DINT;\nEND_VAR",
            IEnumerable<StructAst> structTypes = null)
        {
            var pou = new PouAst("FB_Wrapper", null, varBlock, implementation, new List<MethodAst>());
            return new Engine(new TypeRegistry(new[] { pou }, structTypes, enumMembers: enumMembers));
        }

        [Fact]
        public void QualifiedEnumLiteral_ScalarAssignment_ResolvesToMemberValue()
        {
            var enumMembers = EnumMembers("eWidgetValueKind", ("TypeBool", 0), ("TypeLreal", 3));
            var engine = NewEngine("result := eWidgetValueKind.TypeLreal;", enumMembers);
            var instance = engine.NewInstance("FB_Wrapper");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            Assert.Equal(3, instance.Fields["result"].Value);
        }

        [Fact]
        public void QualifiedEnumLiteral_ExplicitInitializerWithGap_ResolvesToDeclaredValue()
        {
            var enumMembers = EnumMembers("eWidgetValueKind", ("TypeBool", 5), ("TypeByte", 6), ("TypeInt", 7));
            var engine = NewEngine("result := eWidgetValueKind.TypeInt;", enumMembers);
            var instance = engine.NewInstance("FB_Wrapper");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            Assert.Equal(7, instance.Fields["result"].Value);
        }

        [Fact]
        public void QualifiedEnumLiteral_StructFieldOfArrayElement_AssignsMemberValue()
        {
            var stSendValue = StructDeclParser.Parse(@"TYPE ST_SendValue :
STRUCT
	eType : INT;
END_STRUCT
END_TYPE");
            var enumMembers = EnumMembers("eWidgetValueKind", ("TypeBool", 0), ("TypeLreal", 3));
            var engine = NewEngine(
                "aValues[1].eType := eWidgetValueKind.TypeLreal;",
                enumMembers,
                varBlock: "VAR\n\taValues : ARRAY[0..1] OF ST_SendValue;\nEND_VAR",
                structTypes: new[] { stSendValue });
            var instance = engine.NewInstance("FB_Wrapper");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            var values = (ArrayValue)instance.Fields["aValues"].Value;
            var element = (StructInstance)values.Elements[1];
            Assert.Equal(3, element.Fields["eType"].Value);
        }

        [Fact]
        public void QualifiedEnumLiteral_ComparedAgainstAssignedField_AreEqual()
        {
            var enumMembers = EnumMembers("eWidgetValueKind", ("TypeBool", 0), ("TypeLreal", 3));
            var engine = NewEngine(
                "actual := eWidgetValueKind.TypeLreal;\nresult := (actual = eWidgetValueKind.TypeLreal);",
                enumMembers,
                varBlock: "VAR\n\tactual : INT;\n\tresult : BOOL;\nEND_VAR");
            var instance = engine.NewInstance("FB_Wrapper");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            Assert.Equal(true, instance.Fields["result"].Value);
        }

        [Fact]
        public void QualifiedEnumLiteral_UnknownMember_ThrowsMatchingBuiltinEnumErrorShape()
        {
            var enumMembers = EnumMembers("eWidgetValueKind", ("TypeBool", 0), ("TypeLreal", 3));
            var engine = NewEngine("result := eWidgetValueKind.Bogus;", enumMembers);
            var instance = engine.NewInstance("FB_Wrapper");

            var ex = Assert.Throws<System.InvalidOperationException>(() =>
                engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null));

            Assert.Equal("Unknown enum member 'eWidgetValueKind.Bogus'", ex.Message);
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 2)]
        public void QualifiedEnumLiteral_AsCaseLabel_DispatchesOnMatchingArm(int opcode, int expected)
        {
            // A qualified enum label and a CASE arm both end in ':', so the
            // parser must not read 'eWidgetOpcode.Add' as an arm boundary.
            var enumMembers = EnumMembers("eWidgetOpcode", ("Add", 0), ("Remove", 1));
            var engine = NewEngine(
                $"eOpcode := {opcode};\n" +
                "CASE eOpcode OF\n" +
                "eWidgetOpcode.Add:\n" +
                "\tresult := 1;\n" +
                "eWidgetOpcode.Remove:\n" +
                "\tresult := 2;\n" +
                "END_CASE",
                enumMembers,
                varBlock: "VAR\n\teOpcode : INT;\n\tresult : DINT;\nEND_VAR");
            var instance = engine.NewInstance("FB_Wrapper");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            Assert.Equal(expected, instance.Fields["result"].Value);
        }

        [Fact]
        public void QualifiedEnumLiteral_LocalVariableSharesEnumTypeName_VariableWins()
        {
            var stValue = StructDeclParser.Parse(@"TYPE ST_Value :
STRUCT
	TypeLreal : INT;
END_STRUCT
END_TYPE");
            var enumMembers = EnumMembers("eWidgetValueKind", ("TypeBool", 0), ("TypeLreal", 3));
            var engine = NewEngine(
                "result := eWidgetValueKind.TypeLreal;",
                enumMembers,
                varBlock: "VAR\n\teWidgetValueKind : ST_Value := (TypeLreal := 42);\n\tresult : INT;\nEND_VAR",
                structTypes: new[] { stValue });
            var instance = engine.NewInstance("FB_Wrapper");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            Assert.Equal(42, instance.Fields["result"].Value);
        }
    }
}
