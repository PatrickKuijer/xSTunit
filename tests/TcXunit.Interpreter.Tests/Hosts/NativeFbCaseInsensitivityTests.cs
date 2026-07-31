using System.Collections.Generic;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-nch: NativeTimerTypes/NativeEdgeTriggerTypes (Engine.cs) and the
    // "Loopback" checks (Engine.cs/Engine.Defaults.cs) used to be ordinal
    // comparisons, so a lowercase/mixed-case native FB base type (e.g.
    // 'fbTimer : ton;') fell through to NativeSuiteHost instead of being
    // recognized as a native timer/edge-trigger/loopback stub - the same
    // class of bug TcXunit-fzm fixed for elementary type names. Fixing only
    // the "is this native?" lookups isn't enough on its own: TimerHost.Create
    // /EdgeTriggerHost.Create's switch expressions are a second, independent
    // case-sensitive site - a lowercase spelling that passes the lookup but
    // hits the still-case-sensitive switch throws NotSupportedException
    // instead of NativeSuiteHost's silent wrong-path failure. These tests
    // cover both layers via the full ST->NewInstance round trip.
    public class NativeFbCaseInsensitivityTests
    {
        private static Engine NewTimerWrapperEngine(string timerTypeName)
        {
            var pou = new PouAst(
                "FB_Wrapper",
                null,
                "VAR\n\tfbTimer : " + timerTypeName + ";\nEND_VAR",
                "",
                new List<MethodAst>());

            return new Engine(new TypeRegistry(new[] { pou }));
        }

        private static Engine NewEdgeTriggerWrapperEngine(string triggerTypeName)
        {
            var pou = new PouAst(
                "FB_Wrapper",
                null,
                "VAR\n\tfbTrig : " + triggerTypeName + ";\nEND_VAR",
                "",
                new List<MethodAst>());

            return new Engine(new TypeRegistry(new[] { pou }));
        }

        private static Engine NewLoopbackWrapperEngine(string loopbackTypeName)
        {
            var pou = new PouAst(
                "FB_Wrapper",
                null,
                "VAR\n\tfbLink : " + loopbackTypeName + ";\nEND_VAR",
                "",
                new List<MethodAst>());

            return new Engine(new TypeRegistry(new[] { pou }));
        }

        [Theory]
        [InlineData("ton")]
        [InlineData("Ton")]
        public void LowercaseOrMixedCaseTon_InstantiatesAsNativeTimerHost(string typeName)
        {
            var engine = NewTimerWrapperEngine(typeName);
            var instance = engine.NewInstance("FB_Wrapper");
            var fbTimer = (FbInstance)instance.Fields["fbTimer"].Value;

            Assert.NotNull(fbTimer.NativeTimerHost);
            Assert.Null(fbTimer.NativeSuiteHost);
            Assert.Equal(false, fbTimer.Fields["IN"].Value);
            Assert.Equal(0u, fbTimer.Fields["PT"].Value);
            Assert.Equal(false, fbTimer.Fields["Q"].Value);
            Assert.Equal(0u, fbTimer.Fields["ET"].Value);
        }

        [Theory]
        [InlineData("tof")]
        [InlineData("ToF")]
        public void LowercaseOrMixedCaseTof_InstantiatesAsNativeTimerHost(string typeName)
        {
            var engine = NewTimerWrapperEngine(typeName);
            var instance = engine.NewInstance("FB_Wrapper");
            var fbTimer = (FbInstance)instance.Fields["fbTimer"].Value;

            Assert.NotNull(fbTimer.NativeTimerHost);
            Assert.Null(fbTimer.NativeSuiteHost);
        }

        [Theory]
        [InlineData("fb_pulse")]
        [InlineData("FB_pulse")]
        public void LowercaseOrMixedCaseFbPulse_InstantiatesAsNativeTimerHost(string typeName)
        {
            var engine = NewTimerWrapperEngine(typeName);
            var instance = engine.NewInstance("FB_Wrapper");
            var fbTimer = (FbInstance)instance.Fields["fbTimer"].Value;

            Assert.NotNull(fbTimer.NativeTimerHost);
            Assert.Null(fbTimer.NativeSuiteHost);
        }

        // TcXunit-x5pt: the LTIME trio resolves through the same
        // NativeTimerTypes lookup and the same TimerHost.Create switch, so it
        // inherits both case-insensitivity layers - and seeds PT/ET at LTIME
        // width (0ul), not TIME width (0u).
        [Theory]
        [InlineData("lton")]
        [InlineData("LtOn")]
        [InlineData("ltof")]
        [InlineData("ltp")]
        public void LowercaseOrMixedCaseLtimeTimer_InstantiatesAsNativeTimerHost(string typeName)
        {
            var engine = NewTimerWrapperEngine(typeName);
            var instance = engine.NewInstance("FB_Wrapper");
            var fbTimer = (FbInstance)instance.Fields["fbTimer"].Value;

            Assert.NotNull(fbTimer.NativeTimerHost);
            Assert.Null(fbTimer.NativeSuiteHost);
            Assert.Equal(false, fbTimer.Fields["IN"].Value);
            Assert.Equal(0ul, fbTimer.Fields["PT"].Value);
            Assert.Equal(false, fbTimer.Fields["Q"].Value);
            Assert.Equal(0ul, fbTimer.Fields["ET"].Value);
        }

        [Theory]
        [InlineData("r_trig")]
        [InlineData("R_trig")]
        public void LowercaseOrMixedCaseRTrig_InstantiatesAsNativeEdgeTriggerHost(string typeName)
        {
            var engine = NewEdgeTriggerWrapperEngine(typeName);
            var instance = engine.NewInstance("FB_Wrapper");
            var fbTrig = (FbInstance)instance.Fields["fbTrig"].Value;

            Assert.NotNull(fbTrig.NativeEdgeTriggerHost);
            Assert.Null(fbTrig.NativeSuiteHost);
            Assert.Equal(false, fbTrig.Fields["CLK"].Value);
            Assert.Equal(false, fbTrig.Fields["Q"].Value);
        }

        [Theory]
        [InlineData("f_trig")]
        [InlineData("F_trig")]
        public void LowercaseOrMixedCaseFTrig_InstantiatesAsNativeEdgeTriggerHost(string typeName)
        {
            var engine = NewEdgeTriggerWrapperEngine(typeName);
            var instance = engine.NewInstance("FB_Wrapper");
            var fbTrig = (FbInstance)instance.Fields["fbTrig"].Value;

            Assert.NotNull(fbTrig.NativeEdgeTriggerHost);
            Assert.Null(fbTrig.NativeSuiteHost);
        }

        [Theory]
        [InlineData("loopback")]
        [InlineData("LoopBack")]
        public void LowercaseOrMixedCaseLoopback_InstantiatesAsNativeLoopbackHost(string typeName)
        {
            var engine = NewLoopbackWrapperEngine(typeName);
            var instance = engine.NewInstance("FB_Wrapper");
            var fbLink = (FbInstance)instance.Fields["fbLink"].Value;

            Assert.NotNull(fbLink.NativeLoopbackHost);
            Assert.Null(fbLink.NativeSuiteHost);
            Assert.Equal(true, fbLink.Fields["LinkUp"].Value);
            Assert.Equal(0L, fbLink.Fields["LastUpdateTime"].Value);
        }
    }
}
