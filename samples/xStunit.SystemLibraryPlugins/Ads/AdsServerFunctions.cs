using xStunit.Interpreter.Extensibility;

namespace xStunit.SystemLibraryPlugins.Ads
{
    // The scripting surface for the loopback ADS device.
    //
    // xStunit's OWN functions, not Tc2_System symbols. There is no vendor
    // equivalent because on a real system the device on the other end of an ADS
    // request is not the PLC's to configure - which is exactly why the
    // interesting paths in production ADS code go untested. A timeout, an error
    // code on one variable but not another, a device that answers a different
    // length than the caller asked for: all are scriptable here and none are
    // reachable against a real router.
    public sealed class AdsServerClearFunction : IXstunitNativeFunction
    {
        public string Name => "F_AdsServerClear";

        public object Invoke(NativeCallContext context)
        {
            AdsServer.Clear();
            return 0;
        }
    }

    // Text rather than bytes, for the same reason the filesystem seeder takes
    // text: a suite scripting a device response is writing a value, and routing
    // it through a BYTE array would bury the assertion.
    public sealed class AdsServerSetTextFunction : IXstunitNativeFunction
    {
        public string Name => "F_AdsServerSetText";

        public object Invoke(NativeCallContext context)
        {
            AdsServer.SetData(
                context.RequireInt64("nIdxGrp", 0),
                context.RequireInt64("nIdxOffs", 1),
                NarrowText.ToBytes(context.RequireString("sText", 2)));

            return 0;
        }
    }

    public sealed class AdsServerGetTextFunction : IXstunitNativeFunction
    {
        public string Name => "F_AdsServerGetText";

        public object Invoke(NativeCallContext context) =>
            NarrowText.FromBytes(AdsServer.Read(
                context.RequireInt64("nIdxGrp", 0),
                context.RequireInt64("nIdxOffs", 1)));
    }

    public sealed class AdsServerFailFunction : IXstunitNativeFunction
    {
        public string Name => "F_AdsServerFail";

        public object Invoke(NativeCallContext context)
        {
            AdsServer.Fail(
                context.RequireInt64("nIdxGrp", 0),
                context.RequireInt64("nIdxOffs", 1),
                context.RequireInt64("nErrId", 2));

            return 0;
        }
    }

    // A request scripted to time out stays BUSY until the SIMULATED clock has
    // advanced past TMOUT, then reports ADS error 16#745. The suite advances
    // the clock rather than waiting, so the timeout path costs no wall-clock
    // time and is exactly reproducible.
    public sealed class AdsServerTimeoutFunction : IXstunitNativeFunction
    {
        public string Name => "F_AdsServerTimeout";

        public object Invoke(NativeCallContext context)
        {
            AdsServer.Timeout(
                context.RequireInt64("nIdxGrp", 0),
                context.RequireInt64("nIdxOffs", 1));

            return 0;
        }
    }

    public sealed class AdsServerSetStateFunction : IXstunitNativeFunction
    {
        public string Name => "F_AdsServerSetState";

        public object Invoke(NativeCallContext context)
        {
            AdsServer.SetState(
                context.RequireInt32("nAdsState", 0),
                context.RequireInt32("nDevState", 1));

            return 0;
        }
    }

    // Exists so a test can assert that a POU wrote ONCE rather than every
    // cycle - the classic ADS defect, and invisible in the data alone, because
    // the value written is the same each time.
    public sealed class AdsServerWriteCountFunction : IXstunitNativeFunction
    {
        public string Name => "F_AdsServerWriteCount";

        public object Invoke(NativeCallContext context) => (long)AdsServer.WriteCount;
    }
}
