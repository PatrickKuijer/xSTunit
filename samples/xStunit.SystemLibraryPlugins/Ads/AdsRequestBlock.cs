using System;
using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Interpreter.Extensibility;

namespace xStunit.SystemLibraryPlugins.Ads
{
    // The ADS family's share of the command handshake. CommandBlock owns the
    // trigger/busy/error machinery; this fixes the field names (the ADS blocks
    // use the legacy upper-case BUSY/ERR/ERRID rather than the file family's
    // bBusy/bError/nErrId) and adds the one thing the file blocks have no need
    // of: a command that can stay busy.
    //
    // A scripted timeout is what that is for. A real ADS request waits for a
    // device that may never answer, and the timeout path is exactly the one
    // production code gets wrong and never exercises - so it has to be
    // provokable. The block stays BUSY until the SIMULATED clock has advanced
    // past TMOUT, then reports ADS error 16#745. That makes the wait
    // deterministic and instant in wall-clock terms: the suite advances the
    // clock rather than sleeping.
    public abstract class AdsRequestBlock : CommandBlock
    {
        private long _startedAtNs;

        protected override string BusyFieldName => "BUSY";

        protected override string ErrorFieldName => "ERR";

        protected override string ErrorIdFieldName => "ERRID";

        // NETID and PORT are accepted and not acted on: the scripted device is
        // addressed by index group and offset alone, so modelling routing to a
        // target that does not exist would only invent failure modes a test
        // could not then distinguish from real ones.
        protected static IEnumerable<NativeFieldDeclaration> CommonInputs => new[]
        {
            new NativeFieldDeclaration("NETID", string.Empty, "T_MaxString"),
            new NativeFieldDeclaration("PORT", 0),
            new NativeFieldDeclaration("TMOUT", 0u),
        };

        protected static IEnumerable<NativeFieldDeclaration> CommonOutputs => new[]
        {
            new NativeFieldDeclaration("BUSY", false),
            new NativeFieldDeclaration("ERR", false),
            new NativeFieldDeclaration("ERRID", 0L),
        };

        protected override void OnStarted(NativeFunctionBlockCall call) => _startedAtNs = call.Time.ElapsedNs;

        protected sealed override bool Execute(NativeFunctionBlockCall call)
        {
            var (group, offset) = AddressOf(call);

            switch (AdsServer.OutcomeFor(group, offset))
            {
                case ScriptedOutcome.Fail:
                    throw new CommandFailedException(
                        AdsServer.ErrorFor(group, offset),
                        $"{TypeName}: scripted failure at index group 16#{group:X}, offset 16#{offset:X}");

                case ScriptedOutcome.Timeout:
                    // Still busy until the timeout is actually up, so a POU
                    // watching BUSY sees the wait it would see on a real
                    // system rather than an instant failure.
                    if (call.Time.ElapsedNs - _startedAtNs < TimeoutNs(call))
                        return false;

                    throw new CommandFailedException(
                        AdsError.ClientSyncTimeout,
                        $"{TypeName}: scripted timeout at index group 16#{group:X}, offset 16#{offset:X}");

                default:
                    Serve(call);
                    return true;
            }
        }

        // TMOUT is a TIME, which the interpreter carries as milliseconds.
        private static long TimeoutNs(NativeFunctionBlockCall call) =>
            Convert.ToInt64(call.GetField("TMOUT")) * Clock.NanosecondsPerMillisecond;

        // Which scripted variable this request addresses. ADSRDSTATE names
        // none, so it answers with the reserved state-request key.
        protected abstract (long Group, long Offset) AddressOf(NativeFunctionBlockCall call);

        protected abstract void Serve(NativeFunctionBlockCall call);

        protected static long FieldAsInt64(NativeFunctionBlockCall call, string name) =>
            Convert.ToInt64(call.GetField(name));

        // Reads the bytes behind one of the PVOID inputs, rejecting an unset
        // pointer by name rather than letting a null reach the byte reader.
        protected static byte[] BufferOf(NativeFunctionBlockCall call, string pointerField, int length)
        {
            if (!(call.GetField(pointerField) is Pointer source))
            {
                throw new CommandFailedException(
                    AdsError.InvalidIndexGroup,
                    $"{pointerField} is not set - pass ADR(buffer)");
            }

            return call.ReadBytes(source, length);
        }

        // Publishes bytes into the buffer the caller pointed at, truncated to
        // what the caller said it could hold. A device answering more than the
        // caller asked for must not write past the buffer: on a real PLC that
        // corrupts whatever follows it, and here it would fault the whole
        // suite instead of failing the one POU that got the length wrong.
        protected static void PublishInto(NativeFunctionBlockCall call, string pointerField, int capacity, byte[] data)
        {
            if (!(call.GetField(pointerField) is Pointer target))
            {
                throw new CommandFailedException(
                    AdsError.InvalidIndexGroup,
                    $"{pointerField} is not set - pass ADR(buffer)");
            }

            var length = Math.Min(capacity, data.Length);
            var trimmed = new byte[length];
            Array.Copy(data, trimmed, length);

            call.WriteBytes(target, trimmed);
        }
    }
}
