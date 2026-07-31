using System;
using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using xStunit.Runner;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    public class FailureClassifierTests
    {
        [Fact]
        public void Classify_UnsupportedConstructException_IsUnsupportedConstructAndCarriesTheConstruct()
        {
            var kind = FailureClassifier.Classify(new UnsupportedConstructException("SEL", "nope"), out var construct);

            Assert.Equal(FailureKind.UnsupportedConstruct, kind);
            Assert.Equal("SEL", construct);
        }

        // NotSupportedException is not evidence of an interpreter gap: the
        // engine raises it for genuine defects in the code under test too
        // ("Operator '<' is not supported between Int32 and String").
        // Reporting one of those as unsupported-construct tells the reader to
        // stop and escalate over their own bug, so throw sites must opt in by
        // raising UnsupportedConstructException instead.
        [Fact]
        public void Classify_PlainNotSupportedException_IsPlcFaultWhenLocated()
        {
            var located = new PlcSourceLocationException(
                "FB_X", "MethodY", new NotSupportedException("Operator '<' is not supported between Int32 and String"));

            var kind = FailureClassifier.Classify(located, out var construct);

            Assert.Equal(FailureKind.PlcFault, kind);
            Assert.Null(construct);
        }

        // The ST front end gets a kind of its own because it cannot tell
        // "syntax I don't implement" from "syntax that is simply wrong".
        // parse-error says exactly that, rather than smuggling the ambiguity
        // in as plc-fault ("your code is broken") or load-error ("nothing
        // ran"), either of which would misdirect whoever reads the report.
        [Fact]
        public void Classify_ParseException_IsParseErrorWhenLocated()
        {
            var located = new PlcSourceLocationException("FB_X", "MethodY", new ParseException("Unexpected token"));

            var kind = FailureClassifier.Classify(located, out _);

            Assert.Equal(FailureKind.ParseError, kind);
        }

        // One unreadable body must not report as two different kinds depending
        // on whether the call path happened to stamp a location on the way in:
        // that would describe the runner's internals, not the failure.
        [Fact]
        public void Classify_ParseException_IsParseErrorWhenUnlocatedToo()
        {
            var kind = FailureClassifier.Classify(new ParseException("Unexpected token"), out _);

            Assert.Equal(FailureKind.ParseError, kind);
        }

        // A parse-error names its offending token in the same `construct`
        // field unsupported-construct uses, so `kind` + `construct` stays one
        // vocabulary across every kind. The value comes from
        // ParseException.Token, never from scraping the message text.
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

        // `construct` is nullable for every kind, so a ParseException that
        // never captured a token still classifies rather than degrading.
        [Fact]
        public void Classify_TokenlessParseException_IsStillParseErrorWithNoConstruct()
        {
            var kind = FailureClassifier.Classify(new ParseException("Expected END_IF"), out var construct);

            Assert.Equal(FailureKind.ParseError, kind);
            Assert.Null(construct);
        }

        // The lexer holds the body text and the offset it failed at, so it
        // resolves the line itself and stamps it on the exception - nothing
        // downstream has to re-parse a message to recover it.
        [Fact]
        public void LexerParseException_UnexpectedCharacterOnLineTwo_CarriesThatLineStructurally()
        {
            var ex = Assert.Throws<ParseException>(() => Lexer.Tokenize("n := 1;\nn := @ 2;"));

            Assert.Equal(2, ex.BodyLine);
            Assert.Equal("@", ex.Token);
        }

        // A parser message reports a token INDEX, which is not a character
        // offset and cannot be turned into one, so anything reading the line
        // out of the message can only report UnknownLine here. The line has to
        // come from Token.Line, which the parser stamps at its throw sites.
        [Fact]
        public void ParserParseException_ExpectedTokenOnLineTwo_CarriesThatLineWhereMessageScrapingUsedToDegradeToUnknown()
        {
            var ex = Assert.Throws<ParseException>(() => Parser.ParseStatements("n := 1;\nn := 2"));

            Assert.NotEqual(PlcSourceLocationException.UnknownLine, ex.BodyLine);
            Assert.Equal(2, ex.BodyLine);
        }

        // parse-error is claimed by ParseException itself, never by its
        // FormatException base. Engine.ExecuteFor coerces loop bounds with
        // Convert.ToInt32, so a STRING bound throws a bare FormatException
        // from deep inside a body that was already running - a defect in the
        // code under test. Matching on the base type would label that
        // parse-error, whose guidance is to stop and escalate if the ST looks
        // valid: an escalation over a bug the reader should simply fix.
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

            // The throw really is a FormatException, which is what makes the
            // classification above a decision rather than a coincidence.
            Assert.IsType<FormatException>(ex.InnerException);

            var kind = FailureClassifier.Classify(ex, out _);

            Assert.Equal(FailureKind.PlcFault, kind);
        }

        // The other half of the same rule: this FormatException is raised
        // while a declared VAR's default value is being built, before any body
        // has run. That is instantiation, which LoadError covers - calling it
        // a parse-error would blame a body that was never reached.
        //
        // "OFINT" (no space) is the narrowest declaration VarBlockParser still
        // accepts as an ARRAY-typed VAR while ArrayTypeInfo's own pattern
        // rejects it, so it reaches exactly the throw site under test.
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
