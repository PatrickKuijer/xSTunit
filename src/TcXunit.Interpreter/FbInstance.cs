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

        public FbInstance(string actualTypeName)
        {
            ActualTypeName = actualTypeName;
        }
    }
}
