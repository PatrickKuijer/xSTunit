using System;
using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using xStunit.Runner;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // TcXunit-3tx.1: the exception-to-FailureKind mapping, isolated from the
    // CLI so each branch is pinned without needing a fixture that reproduces
    // it end to end.
    public class FailureClassifierTests
    {
        [Fact]
        public void Classify_UnsupportedConstructException_IsUnsupportedConstructAndCarriesTheConstruct()
        {
            var kind = FailureClassifier.Classify(new UnsupportedConstructException("SEL", "nope"), out var construct);

            Assert.Equal(FailureKind.UnsupportedConstruct, kind);
            Assert.Equal("SEL", construct);
        }

        // The base type is NOT enough evidence, and claiming it would be the
        // worst possible error: the engine throws plain NotSupportedException
        // for genuine defects in the code under test too ("Operator '<' is not
        // supported between Int32 and String"), and reporting one of those as
        // unsupported-construct tells an agent to stop and page a human over
        // its own bug. Throw sites opt in by type instead.
        [Fact]
        public void Classify_PlainNotSupportedException_IsPlcFaultWhenLocated()
        {
            var located = new PlcSourceLocationException(
                "FB_X", "MethodY", new NotSupportedException("Operator '<' is not supported between Int32 and String"));

            var kind = FailureClassifier.Classify(located, out var construct);

            Assert.Equal(FailureKind.PlcFault, kind);
            Assert.Null(construct);
        }

        // TcXunit-229.15: the ST front end gets its own kind. A hand-rolled
        // recursive-descent parser still cannot tell "syntax I don't implement"
        // from "syntax that is simply wrong" - parse-error is that answer said
        // out loud, instead of being smuggled in as plc-fault ("your code is
        // broken") or load-error ("nothing ran").
        [Fact]
        public void Classify_ParseException_IsParseErrorWhenLocated()
        {
            var located = new PlcSourceLocationException("FB_X", "MethodY", new ParseException("Unexpected token"));

            var kind = FailureClassifier.Classify(located, out _);

            Assert.Equal(FailureKind.ParseError, kind);
        }

        // The accident TcXunit-229.15 removes: the SAME unreadable body used to
        // classify as plc-fault when it happened to be reached through a call
        // that stamped a location, and load-error when it didn't. That split
        // described TcXunit's own call path, not the failure - so the kind can't
        // depend on it.
        [Fact]
        public void Classify_ParseException_IsParseErrorWhenUnlocatedToo()
        {
            var kind = FailureClassifier.Classify(new ParseException("Unexpected token"), out _);

            Assert.Equal(FailureKind.ParseError, kind);
        }

        // A parse-error names its offending token in `construct` - the same
        // field unsupported-construct uses, never a parse-error-only field, so
        // `kind` + `construct` is one vocabulary at every level of the JSON.
        // xstunit-fpw8: Classify reads ParseException.Token directly - it no
        // longer re-derives it by scraping the message.
        [Fact]
        public void Classify_LexerParseException_CarriesTheOffendingTokenAsTheConstruct()
        {
            var ex = new ParseException("Unexpected character '@' at position 13 in: n := 1;\nn := @ 2;", "@");

            FailureClassifier.Classify(ex, out var construct);

            Assert.Equal("@", construct);
        }

        // The parser's own messages name the token as Token.ToString()
        // ("Type:Text"), but the structured field carries the bare token text.
        [Fact]
        public void Classify_ParserParseException_CarriesTheOffendingTokenAsTheConstruct()
        {
            var ex = new ParseException("Expected Semicolon but got Identifier:FOO at token index 4", "FOO");

            FailureClassifier.Classify(ex, out var construct);

            Assert.Equal("FOO", construct);
        }

        // A ParseException with no token at all ("Expected END_IF") still
        // classifies - `construct` is nullable for every kind, and a missing
        // token must never cost the classification.
        [Fact]
        public void Classify_TokenlessParseException_IsStillParseErrorWithNoConstruct()
        {
            var kind = FailureClassifier.Classify(new ParseException("Expected END_IF"), out var construct);

            Assert.Equal(FailureKind.ParseError, kind);
            Assert.Null(construct);
        }

        // The lexer knows the body text and the offset it failed at, so it
        // derives the line itself (Lexer.LineAt) and stamps it on the
        // exception directly - no message to re-parse downstream.
        [Fact]
        public void LexerParseException_UnexpectedCharacterOnLineTwo_CarriesThatLineStructurally()
        {
            var ex = Assert.Throws<ParseException>(() => Lexer.Tokenize("n := 1;\nn := @ 2;"));

            Assert.Equal(2, ex.BodyLine);
            Assert.Equal("@", ex.Token);
        }

        // xstunit-fpw8: the case the old message-scraping could never recover.
        // The parser reports a token INDEX, not a body offset, so the message-
        // scraping fallback always degraded to UnknownLine for a parser-raised
        // failure. Token.Line is tracked by the lexer regardless, and the
        // parser's own throw sites now stamp it directly - so a parser-raised
        // ParseException carries a real line where it used to carry none.
        [Fact]
        public void ParserParseException_ExpectedTokenOnLineTwo_CarriesThatLineWhereMessageScrapingUsedToDegradeToUnknown()
        {
            var ex = Assert.Throws<ParseException>(() => Parser.ParseStatements("n := 1;\nn := 2"));

            Assert.NotEqual(PlcSourceLocationException.UnknownLine, ex.BodyLine);
            Assert.Equal(2, ex.BodyLine);
        }

        // TcXunit-g14q: parse-error is claimed by the front end's OWN type, not
        // by the FormatException base. Engine.ExecuteFor coerces its loop bounds
        // with Convert.ToInt32, so a STRING bound throws a bare FormatException
        // from deep inside a running body - a genuine defect in the code under
        // test. Matching the base type reported it as parse-error, whose
        // guidance is "STOP and escalate if it looks like valid ST": an agent
        // told to page a human over a bug it should simply have fixed, which is
        // this vocabulary's own failure mode inverted.
        [Fact]
        public void Classify_StringForLoopBound_IsPlcFaultNotParseError()
        {
            var suite = new PouAst(
                "FB_MySuite",
                "TcUnit.FB_TestSuite",
                "VAR\n\ti : INT;\n\ttotal : INT;\nEND_VAR",
                "FOR i := 1 TO 'not a number' DO\n\ttotal := total + i;\nEND_FOR",
                new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { suite }));

            var ex = Assert.Throws<PlcSourceLocationException>(() => engine.RunSuite("FB_MySuite"));

            // The throw really is a FormatException - the base type alone is no
            // evidence of a parse failure, which is the whole point.
            Assert.IsType<FormatException>(ex.InnerException);

            var kind = FailureClassifier.Classify(ex, out _);

            Assert.Equal(FailureKind.PlcFault, kind);
        }

        // The other half of the same rule: ArrayTypeInfo.Parse throws a bare
        // FormatException while Engine.BuildArrayDefault is constructing a
        // declared VAR's default value - before any body has run. That is
        // instantiation, which FailureKind.LoadError's own doc comment claims,
        // so the classifier must agree with it rather than calling it a
        // parse-error of a body that was never even reached.
        //
        // "OFINT" (no space) is the narrowest declaration that VarBlockParser
        // still accepts as an ARRAY-typed VAR - ArrayTypeInfo's own pattern
        // requires whitespace after OF - so it reaches ArrayTypeInfo.Parse and
        // fails there, which is exactly the throw site under test.
        [Fact]
        public void Classify_UnparseableArrayTypeDuringInstantiation_IsLoadErrorNotParseError()
        {
            var suite = new PouAst(
                "FB_MySuite",
                "TcUnit.FB_TestSuite",
                "VAR\n\tbad : ARRAY[1..2] OFINT;\nEND_VAR",
                "",
                new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { suite }));

            var ex = Assert.Throws<FormatException>(() => engine.RunSuite("FB_MySuite"));

            Assert.Contains("Not a valid ARRAY type declaration", ex.Message);
            Assert.IsNotType<ParseException>(ex);

            var kind = FailureClassifier.Classify(ex, out _);

            Assert.Equal(FailureKind.LoadError, kind);
        }

        [Fact]
        public void Classify_ConvergenceAssertionException_IsAssertion()
        {
            var kind = FailureClassifier.Classify(new ConvergenceAssertionException("did not converge"), out _);

            Assert.Equal(FailureKind.Assertion, kind);
        }

        // An interpreted body faulted, and nothing above claimed the failure as
        // an interpreter gap: the PLC code itself is wrong.
        [Fact]
        public void Classify_LocatedOrdinaryException_IsPlcFault()
        {
            var located = new PlcSourceLocationException(
                "FB_X", "MethodY", new InvalidOperationException("Method 'Z' not found"));

            var kind = FailureClassifier.Classify(located, out _);

            Assert.Equal(FailureKind.PlcFault, kind);
        }

        // No PLC location was ever stamped, so the failure happened before or
        // outside any interpreted ST body - loading, not running.
        [Fact]
        public void Classify_UnlocatedOrdinaryException_IsLoadError()
        {
            var kind = FailureClassifier.Classify(new InvalidOperationException("boom"), out _);

            Assert.Equal(FailureKind.LoadError, kind);
        }

        // The wrapper must not mask the kind of what it wraps: an unsupported
        // construct hit inside an interpreted body is still unsupported.
        [Fact]
        public void Classify_LocatedUnsupportedConstruct_UnwrapsToUnsupportedConstruct()
        {
            var located = new PlcSourceLocationException(
                "FB_X", "MethodY", new UnsupportedConstructException("SEL", "nope"));

            var kind = FailureClassifier.Classify(located, out var construct);

            Assert.Equal(FailureKind.UnsupportedConstruct, kind);
            Assert.Equal("SEL", construct);
        }

        [Fact]
        public void Classify_Null_IsLoadError()
        {
            var kind = FailureClassifier.Classify(null, out var construct);

            Assert.Equal(FailureKind.LoadError, kind);
            Assert.Null(construct);
        }
    }
}
