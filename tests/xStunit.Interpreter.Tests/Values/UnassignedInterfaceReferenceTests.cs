using System;
using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // A VAR whose declared type is an interface loaded from a .TcIO starts as
    // a null contract, not as the integer 0. What that buys is a named fault
    // the moment anything is read or called through it: on the target the same
    // variable is a null pointer, so the dependency someone forgot to inject
    // has to be loud here rather than silently reading as zero and passing.
    //
    // The null-check idiom that fault must not cost is pinned alongside it: ST
    // spells "is this assigned?" as 'ipItf <> 0', and an unassigned reference
    // still has to answer that question rather than fault on being asked.
    public class UnassignedInterfaceReferenceTests
    {
        private const string SensorInterfaceName = "I_Sensor";

        private static InterfaceAst SensorInterface() =>
            new InterfaceAst(
                SensorInterfaceName,
                "INTERFACE I_Sensor",
                new List<MethodAst> { new MethodAst("Poll", "METHOD Poll : BOOL", string.Empty) },
                new List<PropertyAst> { new PropertyAst("Active", "PROPERTY Active : BOOL", string.Empty, null) });

        private static PouAst SensorImplementation() =>
            new PouAst(
                "FB_Sensor",
                null,
                "FUNCTION_BLOCK FB_Sensor",
                "",
                new List<MethodAst> { new MethodAst("Poll", "METHOD Poll : BOOL", "Poll := TRUE;") },
                new List<PropertyAst> { new PropertyAst("Active", "PROPERTY Active : BOOL", "Active := TRUE;", null) });

        private static (Engine Engine, FbInstance Instance, Frame Frame) NewHolder(string body = "")
        {
            var holder = new PouAst(
                "FB_Holder",
                null,
                "FUNCTION_BLOCK FB_Holder\nVAR\n\tsensor : I_Sensor;\n\treal : FB_Sensor;\n\tseen : BOOL;\nEND_VAR",
                body,
                new List<MethodAst>());

            var registry = new TypeRegistry(
                new[] { holder, SensorImplementation() },
                interfaceTypes: new[] { SensorInterface() });
            var engine = new Engine(registry);
            var instance = engine.NewInstance("FB_Holder");
            return (engine, instance, new Frame(instance, "FB_Holder"));
        }

        [Fact]
        public void UnassignedDeclaredInterface_IsNotTheIntegerZero()
        {
            var (_, instance, _) = NewHolder();

            var held = Assert.IsType<UnassignedInterfaceReference>(instance.Fields["sensor"].Value);
            Assert.Equal(SensorInterfaceName, held.InterfaceTypeName);
        }

        // The idiom closed by the earlier interface-equality fix, now over a
        // declared interface type rather than an unknown type name: losing it
        // would break every ST block that guards a call with 'IF ipItf <> 0'.
        [Fact]
        public void UnassignedDeclaredInterface_ComparedToZero_IsStillEqual()
        {
            var (engine, _, frame) = NewHolder();

            Assert.True((bool)engine.Evaluate(Parser.ParseExpression("sensor = 0"), frame));
            Assert.False((bool)engine.Evaluate(Parser.ParseExpression("sensor <> 0"), frame));
        }

        [Fact]
        public void AssignedDeclaredInterface_ComparedToZero_IsNotEqual()
        {
            var (engine, instance, frame) = NewHolder();
            instance.Fields["sensor"].Value = engine.NewInstance("FB_Sensor");

            Assert.False((bool)engine.Evaluate(Parser.ParseExpression("sensor = 0"), frame));
            Assert.True((bool)engine.Evaluate(Parser.ParseExpression("sensor <> 0"), frame));
        }

        [Fact]
        public void PropertyReadThroughUnassignedInterface_FaultsNamingTheInterfaceAndTheMember()
        {
            var (engine, _, frame) = NewHolder();

            var ex = Assert.Throws<InvalidOperationException>(
                () => engine.Evaluate(Parser.ParseExpression("sensor.Active"), frame));

            Assert.Contains(SensorInterfaceName, ex.Message);
            Assert.Contains("Active", ex.Message);
        }

        [Fact]
        public void MethodCallThroughUnassignedInterface_FaultsNamingTheInterfaceAndTheMethod()
        {
            var (engine, _, frame) = NewHolder();

            var ex = Assert.Throws<InvalidOperationException>(
                () => engine.Evaluate(Parser.ParseExpression("sensor.Poll()"), frame));

            Assert.Contains(SensorInterfaceName, ex.Message);
            Assert.Contains("Poll", ex.Message);
        }

        // The sentinel is a starting state, not a permanent property of the
        // variable: an ordinary ST assignment has to replace it, or
        // interface-typed dependency injection - the whole point of declaring
        // the VAR - would be impossible.
        [Fact]
        public void AssignmentThroughStThenReadingBack_ResolvesOnTheAssignedInstance()
        {
            var (engine, instance, _) = NewHolder("sensor := real;\nseen := sensor.Active;");

            engine.CallMethod(
                instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new List<NamedArg>(), null, null);

            Assert.True((bool)instance.Fields["seen"].Value);
        }

        // A name no .TcIO declared is not known to be an interface at all, so
        // it keeps the integer-0 default. Changing that would turn every
        // unresolved type name in a tree with skipped files into a fault.
        [Fact]
        public void UndeclaredInterfaceTypeName_StillDefaultsToTheIntegerZero()
        {
            var holder = new PouAst(
                "FB_Holder", null, "FUNCTION_BLOCK FB_Holder\nVAR\n\tsensor : I_NeverLoaded;\nEND_VAR", "",
                new List<MethodAst>());

            var instance = new Engine(new TypeRegistry(new[] { holder })).NewInstance("FB_Holder");

            Assert.Equal(0, instance.Fields["sensor"].Value);
        }

        // Reaching through a contract nobody satisfied is a defect in the code
        // under test, not a gap in the interpreter: it has to reach the reader
        // as plc-fault ("go fix your POU"), and fail only the test that did it.
        [Fact]
        public void RunSuite_ReadThroughUnassignedInterface_ReportsPlcFaultNamingTheInterface()
        {
            var suite = new PouAst(
                "FB_SensorTests",
                "TcUnit.FB_TestSuite",
                "FUNCTION_BLOCK FB_SensorTests EXTENDS TcUnit.FB_TestSuite\nVAR\n\tsensor : I_Sensor;\n\tseen : BOOL;\nEND_VAR",
                "ReadsThroughSensor();",
                new List<MethodAst>
                {
                    new MethodAst(
                        "ReadsThroughSensor",
                        "METHOD PRIVATE ReadsThroughSensor",
                        "TEST('ReadsThroughSensor');\nseen := sensor.Active;\nTEST_FINISHED();"),
                });

            var registry = new TypeRegistry(
                new[] { suite, SensorImplementation() },
                interfaceTypes: new[] { SensorInterface() });

            var failure = Assert.Single(Assert.Single(new Engine(registry).RunSuite("FB_SensorTests")).Failures);

            Assert.Equal(xStunit.Runner.FailureKind.PlcFault, failure.Kind);
            Assert.Contains(SensorInterfaceName, failure.Message);
            Assert.Contains("Active", failure.Message);
        }
    }
}
