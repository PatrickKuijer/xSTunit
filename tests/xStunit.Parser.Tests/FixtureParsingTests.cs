using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace xStunit.Parser.Tests
{
    // Parses the vendored fixture project on disk rather than embedded XML, so
    // drift between the parser and the files it is aimed at surfaces here and
    // not only in hand-picked snippets.
    public class FixtureParsingTests
    {
        private static readonly string FixturePouDir = TestFixtures.FbCounterFixtureDir();

        [Fact]
        public void Parse_FB_Counter_ReadsBaseCounterStructure()
        {
            var ast = ParseFixture("FB_Counter.TcPOU");

            Assert.Equal("FB_Counter", ast.Name);
            Assert.Null(ast.BaseTypeName);
            Assert.Equal(new[] { "Decrement", "FB_init", "GetValue", "Increment" }, MethodNames(ast));
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

        [Fact]
        public void Parse_FB_Counter_RecordsBodyStartLinesMatchingTheFileOnDisk()
        {
            var ast = ParseFixture("FB_Counter.TcPOU");
            var fileLines = File.ReadAllLines(Path.Combine(FixturePouDir, "FB_Counter.TcPOU"));

            // Expectations come from re-reading the file rather than hard-coded
            // line numbers, which would drift on every edit to the fixture.
            AssertBodyLinesLandOnFile(fileLines, ast.BodyStartLine, ast.ImplementationText);
            foreach (var method in ast.Methods)
                AssertBodyLinesLandOnFile(fileLines, method.BodyStartLine, method.ImplementationText);

            // Distinct offsets are what rule out every scope defaulting to one
            // shared line number.
            Assert.Equal(ast.Methods.Count, new HashSet<int>(ast.Methods.Select(m => m.BodyStartLine)).Count);
        }

        // Matching is by suffix, not equality: the first body line shares its
        // file line with the `<ST><![CDATA[` prologue, and the last one is
        // followed by `]]></ST>`.
        private static void AssertBodyLinesLandOnFile(string[] fileLines, int bodyStartLine, string implementationText)
        {
            var bodyLines = implementationText.Split('\n');
            for (var i = 0; i < bodyLines.Length; i++)
            {
                var fileLine = fileLines[bodyStartLine + i - 1].TrimEnd('\r');
                var bodyLine = bodyLines[i].TrimEnd('\r');
                var isLast = i == bodyLines.Length - 1;
                var expectedTail = isLast ? bodyLine + "]]></ST>" : bodyLine;
                Assert.EndsWith(expectedTail, fileLine);
            }
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
