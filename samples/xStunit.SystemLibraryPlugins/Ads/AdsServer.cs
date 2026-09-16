using System;
using System.Collections.Generic;

namespace xStunit.SystemLibraryPlugins.Ads
{
    // ADS return codes. Unlike the file family's nErrId values, these are the
    // vendor's real numbers: ADS return codes are published, where the
    // file-access command-specific codes are not.
    internal static class AdsError
    {
        public const long InvalidIndexGroup = 0x702;   // 1794
        public const long ClientSyncTimeout = 0x745;   // 1861
    }

    // How the scripted server answers one (index group, index offset).
    internal enum ScriptedOutcome
    {
        Data,
        Fail,
        Timeout,
    }

    internal sealed class ScriptedVariable
    {
        public ScriptedOutcome Outcome { get; set; } = ScriptedOutcome.Data;

        public long ErrorId { get; set; }

        public byte[] Data { get; set; } = Array.Empty<byte>();
    }

    // The ADS device the ADSREAD/ADSWRITE/ADSRDWRT/ADSRDSTATE blocks talk to: a
    // loopback the suite scripts, never a real ADS router.
    //
    // Same reasoning as the virtual filesystem, and then some. A test that
    // reached a real router would need one to exist, would depend on what some
    // other device happened to hold, and could not provoke the interesting
    // cases at all - a timeout, a device that answers the wrong length, an
    // error code on one variable but not another. Those are precisely the paths
    // production ADS code gets wrong and never exercises, so they are the ones
    // worth being able to script.
    //
    // Static for the same reason the other sinks are: four separate block
    // declarations must see one device, and nothing travels between them.
    internal static class AdsServer
    {
        private static readonly Dictionary<(long Group, long Offset), ScriptedVariable> Variables =
            new Dictionary<(long, long), ScriptedVariable>();

        // ADSRDSTATE names no variable, so its scripted failure and timeout are
        // addressed at group 0, offset 0. A convention rather than a natural
        // key, but one mechanism scripting every block beats a second one
        // existing solely for this block.
        public static readonly (long Group, long Offset) StateRequestKey = (0, 0);

        public static int AdsState { get; private set; }

        public static int DeviceState { get; private set; }

        // How many writes the device has served, so a test can assert that a
        // POU wrote once rather than every cycle - the classic ADS defect, and
        // invisible in the data alone because the value written is the same
        // each time.
        public static int WriteCount { get; private set; }

        public static void Clear()
        {
            Variables.Clear();
            AdsState = 0;
            DeviceState = 0;
            WriteCount = 0;
        }

        public static void SetState(int adsState, int deviceState)
        {
            AdsState = adsState;
            DeviceState = deviceState;
        }

        public static void SetData(long group, long offset, byte[] data) =>
            At(group, offset, create: true).Data = data;

        public static void Fail(long group, long offset, long errorId)
        {
            var variable = At(group, offset, create: true);
            variable.Outcome = ScriptedOutcome.Fail;
            variable.ErrorId = errorId;
        }

        public static void Timeout(long group, long offset) =>
            At(group, offset, create: true).Outcome = ScriptedOutcome.Timeout;

        public static ScriptedOutcome OutcomeFor(long group, long offset) =>
            At(group, offset, create: false)?.Outcome ?? ScriptedOutcome.Data;

        public static long ErrorFor(long group, long offset) =>
            At(group, offset, create: false)?.ErrorId ?? 0;

        // An unscripted variable is a read of something the device does not
        // have, which is an invalid index group rather than an empty answer: a
        // POU reading an address nobody published is a defect, and silently
        // handing it zeroes would let the test pass.
        public static byte[] Read(long group, long offset)
        {
            var variable = At(group, offset, create: false);
            if (variable == null)
            {
                throw new CommandFailedException(
                    AdsError.InvalidIndexGroup,
                    $"no ADS variable scripted at index group 16#{group:X}, offset 16#{offset:X}");
            }

            return variable.Data;
        }

        public static void Write(long group, long offset, byte[] data)
        {
            At(group, offset, create: true).Data = data;
            WriteCount++;
        }

        private static ScriptedVariable At(long group, long offset, bool create)
        {
            var key = (group, offset);
            if (Variables.TryGetValue(key, out var variable))
                return variable;

            if (!create)
                return null;

            variable = new ScriptedVariable();
            Variables[key] = variable;
            return variable;
        }
    }
}
