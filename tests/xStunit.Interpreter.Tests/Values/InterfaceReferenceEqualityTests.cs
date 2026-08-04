using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // A field typed by a name no .TcIO declared - ITF_Fake here - has no
    // dedicated null representation: unassigned it holds a plain int 0, and
    // once assigned it holds the concrete FbInstance itself. That is what
    // makes the ST idiom 'IF (iipHandler <> 0) AND iipHandler.bDoWork(...)
    // THEN' work here.
    //
    // A field typed by a LOADED interface starts as an
    // UnassignedInterfaceReference instead, and has to answer the same idiom
    // the same way - see UnassignedInterfaceReferenceTests.
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
