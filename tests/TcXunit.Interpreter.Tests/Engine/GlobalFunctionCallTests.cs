using System.Collections.Generic;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-9su: an unqualified call inside a FUNCTION_BLOCK METHOD naming a
    // plain global FUNCTION (no receiver, no ancestor method) used to throw
    // "Method '<name>' not found starting from type '<fb>'" - the ancestry
    // walk in Engine.Invocation.CallMethod only ever searched the FB's own
    // Method table, with no fallback to a top-level FUNCTION POU of the same
    // name. Covers the CallGlobalFunction fallback added to close that gap.
    public class GlobalFunctionCallTests
    {
        [Fact]
        public void CallMethod_UnqualifiedGlobalFunctionCallFromMethodBody_Resolves()
        {
            var function = new PouAst(
                "F_Double",
                null,
                "FUNCTION F_Double : INT\nVAR_INPUT\n\tnIn : INT;\nEND_VAR",
                "F_Double := nIn * 2;",
                new List<MethodAst>());

            var caller = new MethodAst(
                "bAdd",
                "METHOD bAdd : BOOL",
                "nResult := F_Double(nValue);");

            var fb = new PouAst(
                "FB_RemotePparClient",
                null,
                "VAR\n\tnValue : INT := 21;\n\tnResult : INT;\nEND_VAR",
                "",
                new List<MethodAst> { caller });

            var engine = new Engine(new TypeRegistry(new[] { fb, function }));
            var instance = engine.NewInstance("FB_RemotePparClient");

            engine.CallMethod(instance, "bAdd", new Expr[0], new NamedArg[0], null, null);

            Assert.Equal(42, instance.Fields["nResult"].Value);
        }

        [Fact]
        public void CallMethod_GlobalFunctionDeclarationLeadsWithBlockComment_StillResolves()
        {
            // TcXunit-9k6: GlobalFunctionDeclarationPattern anchored with ^\s*
            // to the very start of DeclarationText, so a file-header purpose
            // comment (this codebase's standard convention) before the
            // FUNCTION keyword made the match fail even though the registry
            // lookup found the right PouAst.
            var function = new PouAst(
                "F_Double",
                null,
                "(*\nPurpose comment describing F_Double.\n*)\nFUNCTION F_Double : INT\nVAR_INPUT\n\tnIn : INT;\nEND_VAR",
                "F_Double := nIn * 2;",
                new List<MethodAst>());

            var caller = new MethodAst(
                "bAdd",
                "METHOD bAdd : BOOL",
                "nResult := F_Double(nValue);");

            var fb = new PouAst(
                "FB_RemotePparClient",
                null,
                "VAR\n\tnValue : INT := 21;\n\tnResult : INT;\nEND_VAR",
                "",
                new List<MethodAst> { caller });

            var engine = new Engine(new TypeRegistry(new[] { fb, function }));
            var instance = engine.NewInstance("FB_RemotePparClient");

            engine.CallMethod(instance, "bAdd", new Expr[0], new NamedArg[0], null, null);

            Assert.Equal(42, instance.Fields["nResult"].Value);
        }
    }
}
