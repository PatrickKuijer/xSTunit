using System.Collections.Generic;

namespace TcXunit.Interpreter
{
    public sealed class FbInstance
    {
        public string ActualTypeName { get; }
        public Dictionary<string, Cell> Fields { get; } = new Dictionary<string, Cell>();

        // Set when ActualTypeName's ancestry reaches TcUnit.FB_TestSuite - the
        // native C# stub instance backing TEST()/AssertEquals_INT()/etc for this
        // instance (TcXunit-w5x.7's native-stub boundary).
        public TcUnitSuiteHost NativeSuiteHost { get; set; }

        // Set when ActualTypeName is a native timer type (TON/TOF/FB_Pulse) -
        // the native host backing this instance's IN/PT->Q/ET behavior
        // (TcXunit-w5x.15.7's native-stub boundary).
        public TimerHost NativeTimerHost { get; set; }

        // Set when ActualTypeName is the native Loopback type - the native
        // host backing this instance's Transmit(source, sink) call
        // (TcXunit-w5x.15.5's native-stub boundary).
        public LoopbackHost NativeLoopbackHost { get; set; }

        // Set when ActualTypeName is a native edge-trigger type (R_TRIG/
        // F_TRIG) - the native host backing this instance's CLK->Q behavior
        // (TcXunit-f6b's native-stub boundary).
        public EdgeTriggerHost NativeEdgeTriggerHost { get; set; }

        public FbInstance(string actualTypeName)
        {
            ActualTypeName = actualTypeName;
        }
    }
}
