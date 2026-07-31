using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // WSTRING is represented exactly as STRING is - a CLR string - and its
    // double-quoted literal lexes to the same token as STRING's
    // single-quoted one.
    public class WStringTypeTests
    {
        private static Engine NewSuiteEngine(string varBlock, string implementationText)
        {
            var fb = new PouAst("FB_Holder", null, varBlock, implementationText, new List<MethodAst>());
            return new Engine(new TypeRegistry(new[] { fb }));
        }

        [Fact]
        public void DefaultValue_BareWStringVar_DefaultsToEmptyString()
        {
            var engine = NewSuiteEngine("VAR\n\ts : WSTRING;\nEND_VAR", "");

            var instance = engine.NewInstance("FB_Holder");

            Assert.Equal("", instance.Fields["s"].Value);
        }

        [Fact]
        public void DefaultValue_SizedWStringVar_DefaultsToEmptyString()
        {
            var engine = NewSuiteEngine("VAR\n\ts : WSTRING(10);\nEND_VAR", "");

            var instance = engine.NewInstance("FB_Holder");

            Assert.Equal("", instance.Fields["s"].Value);
        }

        [Fact]
        public void ExecuteStatements_AssignWStringLiteral_SetsCellValue()
        {
            var engine = NewSuiteEngine("VAR\n\ts : WSTRING;\nEND_VAR", "s := \"wide value\";");
            var instance = engine.NewInstance("FB_Holder");
            var frame = new Frame(instance, "FB_Holder");

            engine.ExecuteStatements(Parser.ParseStatements("s := \"wide value\";"), frame);

            Assert.Equal("wide value", instance.Fields["s"].Value);
        }
    }
}
