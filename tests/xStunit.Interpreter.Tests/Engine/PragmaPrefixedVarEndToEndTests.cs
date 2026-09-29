using System.Collections.Generic;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    public class PragmaPrefixedVarEndToEndTests
    {
        // A pragma sharing a VAR line must not cost the variable: the body
        // would otherwise fail with "Unknown variable" at the use site.
        [Fact]
        public void RunSuite_BodyReadsVariableWhosePragmaSharesItsLine_SeesTheVariable()
        {
            var suite = new PouAst(
                "FB_MySuite",
                "TcUnit.FB_TestSuite",
                "VAR\n\t{attribute 'hide'} nHidden : DINT := 42;\nEND_VAR",
                "TEST('t');\nAssertTrue(nHidden = 42, 'variable is readable');\nTEST_FINISHED();",
                new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { suite }, new StructAst[0]));

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }
    }
}
