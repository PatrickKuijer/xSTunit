using System;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // Declared interface types have to survive the whole way from a real .TcIO
    // file to a VAR that dispatches through them. FB_Conveyor holds its
    // detection point as an I_DigitalInput and its drive as an I_AxisBase, and
    // both contracts live in .TcIO files beside the POUs - so if a fixture load
    // ever stops globbing .TcIO, every interface-typed VAR silently reverts to
    // the integer 0 and a dependency nobody injected reads as a passing zero
    // instead of a named fault. The unit tests for that behaviour build their
    // interface AST by hand; these are the ones that prove the pilot fixtures
    // reach it through the loader they are actually run with.
    public class ConveyorInterfaceContractTests
    {
        private const string DetectionPoint = "caseDetectionSensor";

        private static (Engine Engine, FbInstance Conveyor, Frame Frame) NewConveyor()
        {
            var engine = MiniloadFixtureEngine.Create(
                TestFixtures.MiniloadConveyorFixtureDir(), TestFixtures.MiniloadSensorFixtureDir());
            var conveyor = engine.NewInstance("FB_Conveyor");
            return (engine, conveyor, new Frame(conveyor, "FB_Conveyor"));
        }

        // The integer 0 in either slot would mean that contract's .TcIO never
        // reached the registry.
        [Theory]
        [InlineData(DetectionPoint, "I_DigitalInput")]
        [InlineData("conveyorAxis", "I_AxisBase")]
        public void UnwiredDependency_HoldsTheContractDeclaredInItsTcIoFile(string field, string contract)
        {
            var (_, conveyor, _) = NewConveyor();

            var held = Assert.IsType<UnassignedInterfaceReference>(conveyor.Fields[field].Value);
            Assert.Equal(contract, held.InterfaceTypeName);
        }

        [Fact]
        public void ReadThroughAnUnwiredDetectionPoint_FaultsNamingTheContractAndTheMember()
        {
            var (engine, _, frame) = NewConveyor();

            var ex = Assert.Throws<InvalidOperationException>(
                () => engine.Evaluate(Parser.ParseExpression(DetectionPoint + ".Active"), frame));

            Assert.Contains("I_DigitalInput", ex.Message);
            Assert.Contains("Active", ex.Message);
        }

        // The payoff the sentinel exists for: once the contract is satisfied by
        // the concrete FB_DigitalInput out of the sensor fixture, reads and
        // writes through the interface-typed VAR reach that implementation.
        // Both directions are exercised because a property SET dispatched
        // through a contract is a separate path from a GET, and only members
        // the interface itself declares are touched.
        [Fact]
        public void DetectionPointWiredToItsImplementation_DispatchesBothWaysThroughTheContract()
        {
            var (engine, conveyor, frame) = NewConveyor();
            var sensor = engine.NewInstance("FB_DigitalInput");
            conveyor.Fields[DetectionPoint].Value = sensor;

            engine.ExecuteStatements(
                Parser.ParseStatements(
                    DetectionPoint + ".Simulation := TRUE;\n" + DetectionPoint + ".SimulatedInput := TRUE;"),
                frame);
            MiniloadFixtureEngine.Step(engine, sensor);

            Assert.True((bool)engine.Evaluate(Parser.ParseExpression(DetectionPoint + ".Active"), frame));
        }
    }
}
