using System.IO;
using Xunit;

namespace TcXunit.Parser.Tests
{
    // Exercises the parser against the real TcXunit-w5x.8 fixture project, not
    // embedded XML strings, so structural drift in the actual fixture files
    // gets caught here instead of only in hand-picked snippets.
    public class FixtureParsingTests
    {
        private const string FixturePouDir =
            @"C:\Git\p_twincat_test_project\TestSolution\TestSolution\PLC1\POUs";

        [Fact]
        public void Parse_FB_Counter_ReadsBaseCounterStructure()
        {
            var ast = ParseFixture("FB_Counter.TcPOU");

            Assert.Equal("FB_Counter", ast.Name);
            Assert.Null(ast.BaseTypeName);
            Assert.Equal(new[] { "FB_init", "Increment", "Decrement", "GetValue" }, MethodNames(ast));
        }

        [Fact]
        public void Parse_FB_ClampedCounter_ReadsExtendsAndOverriddenMethods()
        {
            var ast = ParseFixture("FB_ClampedCounter.TcPOU");

            Assert.Equal("FB_ClampedCounter", ast.Name);
            Assert.Equal("FB_Counter", ast.BaseTypeName);
            Assert.Equal(new[] { "FB_init", "Increment" }, MethodNames(ast));
        }

        [Fact]
        public void Parse_FB_CounterTests_ReadsTcUnitExtendsAndCaseMethods()
        {
            var ast = ParseFixture("FB_CounterTests.TcPOU");

            Assert.Equal("FB_CounterTests", ast.Name);
            Assert.Equal("TcUnit.FB_TestSuite", ast.BaseTypeName);
            Assert.Equal(
                new[]
                {
                    "CounterStartsAtZero",
                    "IncrementAddsDelta",
                    "DecrementClampsAtZero",
                    "ClampedCounterIncrementRespectsCeiling",
                },
                MethodNames(ast));
        }

        private static PouAst ParseFixture(string fileName)
        {
            var xml = File.ReadAllText(Path.Combine(FixturePouDir, fileName));
            return TcPouParser.Parse(xml);
        }

        private static string[] MethodNames(PouAst ast)
        {
            var names = new string[ast.Methods.Count];
            for (var i = 0; i < ast.Methods.Count; i++)
                names[i] = ast.Methods[i].Name;
            return names;
        }
    }
}
