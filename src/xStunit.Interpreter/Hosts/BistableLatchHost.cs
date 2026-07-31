using System;
using System.Collections.Generic;

namespace xStunit.Interpreter
{
    // Stands in for the Tc2_Standard bistable latches RS and SR. Like the edge
    // triggers, no clock is involved; Q1 is published into the instance's own
    // Cell field.
    //
    // The input field names differ per latch: RS is (SET, RESET1), SR is
    // (SET1, RESET). That asymmetry is the IEC 61131-3 / Tc2_Standard
    // signature, not a typo, so the names live on the host and both the field
    // seeding (Engine.NativeHost.cs) and the bare-invoke argument binding
    // (Engine.Invocation.cs) read them from here rather than re-spelling them.
    //
    // The host holds NO state of its own: a latch's only memory is its own
    // previous Q1, which already persists across cycles in the instance's Q1
    // Cell. Two calls in one cycle therefore behave exactly like two cycles.
    public abstract class BistableLatchHost
    {
        // Matched case-insensitively, because the lookup in Engine.NativeHost
        // that decides to call this at all is case-insensitive too - a
        // lowercase or mixed-case spelling that got past that lookup must not
        // then throw here.
        public static BistableLatchHost Create(string typeName) => typeName?.ToUpperInvariant() switch
        {
            "RS" => new ResetDominantLatchHost(),
            "SR" => new SetDominantLatchHost(),
            _ => throw new NotSupportedException($"Unknown native bistable latch type '{typeName}'"),
        };

        // The latch's VAR_INPUTs in IEC declaration order, set input first and
        // reset input second - the positional-argument order for a bare
        // invocation like fbLatch(TRUE, FALSE).
        public abstract IReadOnlyList<string> PositionalInputNames { get; }

        // The one VAR_OUTPUT, and unlike the inputs it is spelled the same by
        // both RS and SR.
        public const string OutputName = "Q1";

        public void Update(FbInstance instance)
        {
            var set = (bool)instance.Fields[PositionalInputNames[0]].Value;
            var reset = (bool)instance.Fields[PositionalInputNames[1]].Value;
            var q1 = (bool)instance.Fields[OutputName].Value;

            instance.Fields[OutputName].Value = Latch(set, reset, q1);
        }

        protected abstract bool Latch(bool set, bool reset, bool q1);
    }

    // RS(SET, RESET1) -> Q1, reset-dominant: with both inputs TRUE the reset
    // wins. Q1 := NOT RESET1 AND (Q1 OR SET).
    internal sealed class ResetDominantLatchHost : BistableLatchHost
    {
        private static readonly string[] InputNames = { "SET", "RESET1" };

        public override IReadOnlyList<string> PositionalInputNames => InputNames;

        protected override bool Latch(bool set, bool reset, bool q1) => !reset && (q1 || set);
    }

    // SR(SET1, RESET) -> Q1, set-dominant: with both inputs TRUE the set wins.
    // Q1 := (NOT RESET AND Q1) OR SET1.
    internal sealed class SetDominantLatchHost : BistableLatchHost
    {
        private static readonly string[] InputNames = { "SET1", "RESET" };

        public override IReadOnlyList<string> PositionalInputNames => InputNames;

        protected override bool Latch(bool set, bool reset, bool q1) => (!reset && q1) || set;
    }
}
