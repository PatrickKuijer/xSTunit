using System;
using TcXunit.Interpreter;
using TcXunit.Runner;
using Xunit;

namespace TcXunit.Interpreter.Tests
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

        // Same reasoning for the ST front end: a hand-rolled recursive-descent
        // parser cannot tell "syntax I don't implement" from "syntax that is
        // simply wrong", so FormatException claims neither.
        [Fact]
        public void Classify_FormatException_IsPlcFaultWhenLocated()
        {
            var located = new PlcSourceLocationException("FB_X", "MethodY", new FormatException("Unexpected token"));

            var kind = FailureClassifier.Classify(located, out _);

            Assert.Equal(FailureKind.PlcFault, kind);
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
