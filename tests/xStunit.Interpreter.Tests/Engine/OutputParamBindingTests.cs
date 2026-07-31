using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // TcXunit-wmh: 'Name => expr' call-arg syntax (TcXunit-mym.5) parses and
    // tags NamedArg.IsOutput, but BindParams only ever consulted namedArgs
    // for Input/InOut params - the callee's VAR_OUTPUT value was never
    // written back into the caller-side lvalue. Covers the write-back path
    // added in Engine.Invocation.CallMethod.
    public class OutputParamBindingTests
    {
        [Fact]
        public void CallMethod_ArrowOutputArg_WritesCalleeOutputBackToCallerVariable()
        {
            var producer = new MethodAst(
                "Produce",
                "METHOD Produce\nVAR_OUTPUT\n\tResult : INT;\nEND_VAR",
                "Result := 42;");

            var caller = new MethodAst(
                "Caller",
                "METHOD Caller",
                "Produce(Result => resultVar);");

            var pou = new PouAst(
                "FB_OutputArgFixture",
                null,
                "VAR\n\tresultVar : INT;\nEND_VAR",
                "",
                new List<MethodAst> { producer, caller });

            var engine = new Engine(new TypeRegistry(new[] { pou }));
            var instance = engine.NewInstance("FB_OutputArgFixture");

            engine.CallMethod(instance, "Caller", new Expr[0], new NamedArg[0], null, null);

            Assert.Equal(42, instance.Fields["resultVar"].Value);
        }
    }
}
