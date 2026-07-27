using System.Collections.Generic;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-gd2.4: WSTRING had no interpreter representation at all - not
    // even a trivial STRING mirror. StringTypeInfo.IsStringType/ParseLength
    // now recognize WSTRING alongside STRING (same default 80-char length,
    // since the interpreter has no narrower wide-char representation than
    // C# string), and the Lexer tokenizes "..." (WSTRING's literal
    // delimiter) into the same StringLiteral token '...' already produces
    // for STRING.
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
