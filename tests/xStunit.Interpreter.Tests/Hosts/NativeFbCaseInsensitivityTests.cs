using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // IEC 61131-3 type names are case-insensitive, and recognizing a native FB
    // stub takes TWO independent case-sensitive-if-you-get-it-wrong lookups:
    // the "is this native at all?" check in Engine, and the type-name switch
    // inside each Host.Create. Getting only the first right is the dangerous
    // half-fix, so these run the full ST -> NewInstance round trip and assert
    // on the host that came back.
    //
    // The two failure modes differ, which is why both layers are covered: a
    // miss in the first lookup silently instantiates a suite host and the FB
    // just never behaves like a timer, while a miss in the switch throws
    // NotSupportedException.
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

        // The LTIME trio goes through the same two lookups as TON/TOF, but must
        // seed PT/ET at LTIME width (0ul), not TIME width (0u) - a host picked
        // by a case-insensitive match still has to be the right one.
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
