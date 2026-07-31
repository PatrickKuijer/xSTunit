using System.Collections.Generic;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-w5x.15.8 / T5 design: fault vocabulary on Loopback -
    // Drop/Restore/Freeze/SetDelay/Duplicate/Corrupt, dispatched through the
    // same native-method boundary as Transmit. One active fault mode at a
    // time; LinkUp/LastUpdateTime are the observable contract for watchdog-
    // style staleness-vs-drop tests.
    public class LoopbackFaultTests
    {
        private static Engine NewWrapperEngine()
        {
            var payload = new PouAst(
                "FB_Payload",
                null,
                "VAR\n\tBuffer : INT;\nEND_VAR",
                "",
                new List<MethodAst>());

            var wrapper = new PouAst(
                "FB_Wrapper",
                null,
                "VAR\n\tfbLink : Loopback;\n\ttxFb : FB_Payload;\n\trxFb : FB_Payload;\nEND_VAR",
                "",
                new List<MethodAst>());

            return new Engine(new TypeRegistry(new[] { payload, wrapper }));
        }

        private static FbInstance Link(FbInstance wrapper) => (FbInstance)wrapper.Fields["fbLink"].Value;

        private static void Transmit(Engine engine, FbInstance wrapper) =>
            engine.CallMethod(
                Link(wrapper),
                "Transmit",
                new Expr[0],
                new[]
                {
                    new NamedArg("source", new FieldAccessExpr(new IdentifierExpr("txFb"), "Buffer")),
                    new NamedArg("sink", new FieldAccessExpr(new IdentifierExpr("rxFb"), "Buffer")),
                },
                new Frame(wrapper, "FB_Wrapper"),
                null);

        private static void Fault(Engine engine, FbInstance wrapper, string methodName, params Expr[] args) =>
            engine.CallMethod(Link(wrapper), methodName, args, new NamedArg[0], new Frame(wrapper, "FB_Wrapper"), null);

        private static bool LinkUp(FbInstance wrapper) => (bool)Link(wrapper).Fields["LinkUp"].Value;
        private static long LastUpdateTime(FbInstance wrapper) => (long)Link(wrapper).Fields["LastUpdateTime"].Value;

        [Fact]
        public void LinkUp_DefaultsTrue_AndLastUpdateTimeDefaultsZero()
        {
            var engine = NewWrapperEngine();
            var wrapper = engine.NewInstance("FB_Wrapper");

            Assert.True(LinkUp(wrapper));
            Assert.Equal(0L, LastUpdateTime(wrapper));
        }

        [Fact]
        public void Drop_SetsLinkDown_AndTransmitBecomesNoOp()
        {
            var engine = NewWrapperEngine();
            var wrapper = engine.NewInstance("FB_Wrapper");
            var txFb = (FbInstance)wrapper.Fields["txFb"].Value;
            var rxFb = (FbInstance)wrapper.Fields["rxFb"].Value;

            Fault(engine, wrapper, "Drop");
            txFb.Fields["Buffer"].Value = 7;
            engine.Clock.AdvanceMs(100);
            Transmit(engine, wrapper);

            Assert.False(LinkUp(wrapper));
            Assert.Equal(0, rxFb.Fields["Buffer"].Value);
            Assert.Equal(0L, LastUpdateTime(wrapper));
        }

        [Fact]
        public void Restore_ClearsDrop_AndTransmitResumesCopying()
        {
            var engine = NewWrapperEngine();
            var wrapper = engine.NewInstance("FB_Wrapper");
            var txFb = (FbInstance)wrapper.Fields["txFb"].Value;
            var rxFb = (FbInstance)wrapper.Fields["rxFb"].Value;

            Fault(engine, wrapper, "Drop");
            Fault(engine, wrapper, "Restore");
            txFb.Fields["Buffer"].Value = 9;
            engine.Clock.AdvanceMs(50);
            Transmit(engine, wrapper);

            Assert.True(LinkUp(wrapper));
            Assert.Equal(9, rxFb.Fields["Buffer"].Value);
            Assert.Equal(50L, LastUpdateTime(wrapper));
        }

        [Fact]
        public void Freeze_KeepsLinkUp_ButSinkAndLastUpdateTimeDoNotAdvance()
        {
            var engine = NewWrapperEngine();
            var wrapper = engine.NewInstance("FB_Wrapper");
            var txFb = (FbInstance)wrapper.Fields["txFb"].Value;
            var rxFb = (FbInstance)wrapper.Fields["rxFb"].Value;

            Fault(engine, wrapper, "Freeze");
            txFb.Fields["Buffer"].Value = 5;
            engine.Clock.AdvanceMs(200);
            Transmit(engine, wrapper);

            Assert.True(LinkUp(wrapper));
            Assert.Equal(0, rxFb.Fields["Buffer"].Value);
            Assert.Equal(0L, LastUpdateTime(wrapper));
        }

        [Fact]
        public void Freeze_VsDrop_StalenessDistinguishableViaLinkUp()
        {
            // Watchdog-style demo: both faults leave the sink un-updated, but
            // only Drop reports the link itself as down. A watchdog reading
            // LinkUp distinguishes "stale but connected" from "hard drop".
            var engine = NewWrapperEngine();
            var frozen = engine.NewInstance("FB_Wrapper");
            var dropped = engine.NewInstance("FB_Wrapper");

            Fault(engine, frozen, "Freeze");
            Fault(engine, dropped, "Drop");

            Assert.True(LinkUp(frozen));
            Assert.False(LinkUp(dropped));
        }

        [Fact]
        public void SetDelay_HoldsSinkUntilQueueReachesDepth_ThenDeliversOldestValue()
        {
            var engine = NewWrapperEngine();
            var wrapper = engine.NewInstance("FB_Wrapper");
            var txFb = (FbInstance)wrapper.Fields["txFb"].Value;
            var rxFb = (FbInstance)wrapper.Fields["rxFb"].Value;

            Fault(engine, wrapper, "SetDelay", new IntLiteralExpr(3));

            txFb.Fields["Buffer"].Value = 1;
            Transmit(engine, wrapper);
            Assert.Equal(0, rxFb.Fields["Buffer"].Value);

            txFb.Fields["Buffer"].Value = 2;
            Transmit(engine, wrapper);
            Assert.Equal(0, rxFb.Fields["Buffer"].Value);

            txFb.Fields["Buffer"].Value = 3;
            engine.Clock.AdvanceMs(30);
            Transmit(engine, wrapper);

            Assert.Equal(1, rxFb.Fields["Buffer"].Value); // oldest queued value delivered
            Assert.Equal(30L, LastUpdateTime(wrapper));

            // Delay window is spent - normal copy semantics resume.
            txFb.Fields["Buffer"].Value = 4;
            Transmit(engine, wrapper);
            Assert.Equal(4, rxFb.Fields["Buffer"].Value);
        }

        [Fact]
        public void Duplicate_ResendsLastTransmittedValue_OneShot()
        {
            var engine = NewWrapperEngine();
            var wrapper = engine.NewInstance("FB_Wrapper");
            var txFb = (FbInstance)wrapper.Fields["txFb"].Value;
            var rxFb = (FbInstance)wrapper.Fields["rxFb"].Value;

            txFb.Fields["Buffer"].Value = 11;
            Transmit(engine, wrapper);

            Fault(engine, wrapper, "Duplicate");
            txFb.Fields["Buffer"].Value = 99; // ignored for the duplicated call
            Transmit(engine, wrapper);
            Assert.Equal(11, rxFb.Fields["Buffer"].Value);

            // One-shot - next call copies normally again.
            Transmit(engine, wrapper);
            Assert.Equal(99, rxFb.Fields["Buffer"].Value);
        }

        [Fact]
        public void Corrupt_SubstitutesSuppliedValue_OneShot()
        {
            var engine = NewWrapperEngine();
            var wrapper = engine.NewInstance("FB_Wrapper");
            var txFb = (FbInstance)wrapper.Fields["txFb"].Value;
            var rxFb = (FbInstance)wrapper.Fields["rxFb"].Value;

            Fault(engine, wrapper, "Corrupt", new IntLiteralExpr(-1));
            txFb.Fields["Buffer"].Value = 3;
            Transmit(engine, wrapper);
            Assert.Equal(-1, rxFb.Fields["Buffer"].Value);

            // One-shot - next call copies normally again.
            Transmit(engine, wrapper);
            Assert.Equal(3, rxFb.Fields["Buffer"].Value);
        }

        [Fact]
        public void SettingNewFault_ClearsPreviousFaultState()
        {
            var engine = NewWrapperEngine();
            var wrapper = engine.NewInstance("FB_Wrapper");
            var txFb = (FbInstance)wrapper.Fields["txFb"].Value;
            var rxFb = (FbInstance)wrapper.Fields["rxFb"].Value;

            Fault(engine, wrapper, "SetDelay", new IntLiteralExpr(5));
            Fault(engine, wrapper, "Corrupt", new IntLiteralExpr(-7)); // supersedes the pending delay

            txFb.Fields["Buffer"].Value = 20;
            Transmit(engine, wrapper);
            Assert.Equal(-7, rxFb.Fields["Buffer"].Value);

            // Delay queue was cleared, so subsequent calls behave normally, not
            // as though 5 queued transmits are still pending.
            txFb.Fields["Buffer"].Value = 21;
            Transmit(engine, wrapper);
            Assert.Equal(21, rxFb.Fields["Buffer"].Value);
        }
    }
}
