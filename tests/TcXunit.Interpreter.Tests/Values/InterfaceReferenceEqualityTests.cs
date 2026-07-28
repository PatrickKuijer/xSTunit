using System.Collections.Generic;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-dba: the standard IEC 61131-3 "is the interface assigned?"
    // null-check idiom 'IF (iipHandler <> 0) AND iipHandler.bDoWork(...) THEN'
    // must work without throwing. An interface-typed field has no dedicated
    // Pointer/null representation (TcPouParser never parses <Itf> POUs, so
    // an unassigned interface field's DefaultValue lookups all miss and it
    // falls through to plain int 0); once assigned to a concrete FB
    // (itf := concreteFb), the field holds that FB's FbInstance directly.
    public class InterfaceReferenceEqualityTests
    {
        private static (Engine Engine, FbInstance Instance, Frame Frame) NewHolder(string varBlock)
        {
            var fbOther = new PouAst("FB_Other", null, "VAR\nEND_VAR", "", new List<MethodAst>());
            var fbOther2 = new PouAst("FB_Other2", null, "VAR\nEND_VAR", "", new List<MethodAst>());
            var fb = new PouAst("FB_Holder", null, varBlock, "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb, fbOther, fbOther2 }));
            var instance = engine.NewInstance("FB_Holder");
            return (engine, instance, new Frame(instance, "FB_Holder"));
        }

        [Fact]
        public void UnassignedInterface_ComparedToZero_IsEqual()
        {
            var (engine, _, frame) = NewHolder("VAR\n\titf : ITF_Fake;\nEND_VAR");

            Assert.True((bool)engine.Evaluate(Parser.ParseExpression("itf = 0"), frame));
            Assert.False((bool)engine.Evaluate(Parser.ParseExpression("itf <> 0"), frame));
        }

        [Fact]
        public void AssignedInterface_ComparedToZero_IsNotEqual()
        {
            var (engine, instance, frame) = NewHolder("VAR\n\titf : ITF_Fake;\nEND_VAR");
            instance.Fields["itf"].Value = engine.NewInstance("FB_Other");

            Assert.False((bool)engine.Evaluate(Parser.ParseExpression("itf = 0"), frame));
            Assert.True((bool)engine.Evaluate(Parser.ParseExpression("itf <> 0"), frame));
        }

        [Fact]
        public void AssignedInterface_ComparedToSameInstance_IsEqual()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\titf1 : ITF_Fake;\n\titf2 : ITF_Fake;\nEND_VAR");
            var shared = engine.NewInstance("FB_Other");
            instance.Fields["itf1"].Value = shared;
            instance.Fields["itf2"].Value = shared;

            Assert.True((bool)engine.Evaluate(Parser.ParseExpression("itf1 = itf2"), frame));
        }

        [Fact]
        public void AssignedInterface_ComparedToDifferentInstance_IsNotEqual()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\titf1 : ITF_Fake;\n\titf2 : ITF_Fake;\nEND_VAR");
            instance.Fields["itf1"].Value = engine.NewInstance("FB_Other");
            instance.Fields["itf2"].Value = engine.NewInstance("FB_Other2");

            Assert.True((bool)engine.Evaluate(Parser.ParseExpression("itf1 <> itf2"), frame));
        }
    }
}
