using System;

namespace xStunit.Interpreter.Extensibility
{
    /// <summary>
    /// What a plugin is allowed to know about the time: the interpreter's simulated
    /// clock, never the machine's.
    /// </summary>
    /// <remarks>
    /// A library function or block that reads wall time is non-deterministic by
    /// construction - the same suite passes on one machine and fails on another, and no
    /// test can assert on the result at all. Every time-shaped plugin therefore reads
    /// this instead, and a suite that advances the clock sees exactly the advance it
    /// asked for.
    /// <para>
    /// Deliberately vendor-neutral: elapsed nanoseconds and absolute UTC. A library's own
    /// epoch and unit - TwinCAT's 100 ns intervals since 1601, say - are that library's
    /// knowledge and belong in the plugin that implements it, not here.
    /// </para>
    /// </remarks>
    public readonly struct SimulatedTime
    {
        /// <param name="elapsedNs">Simulated nanoseconds since the run began.</param>
        /// <param name="utcNow">Absolute simulated time now.</param>
        /// <param name="taskStartUtc">Absolute simulated time as of the start of the current PLC cycle.</param>
        public SimulatedTime(long elapsedNs, DateTime utcNow, DateTime taskStartUtc)
        {
            ElapsedNs = elapsedNs;
            UtcNow = utcNow;
            TaskStartUtc = taskStartUtc;
        }

        /// <summary>
        /// Simulated nanoseconds since the run began - the same running total the
        /// TON/LTON family reads, so a plugin measuring a timeout measures it against
        /// the same clock the timers do.
        /// </summary>
        public long ElapsedNs { get; }

        /// <summary>
        /// Absolute simulated time now: the clock's configured origin plus
        /// <see cref="ElapsedNs"/>.
        /// </summary>
        /// <remarks>
        /// <c>default(SimulatedTime)</c> leaves this at <see cref="DateTime.MinValue"/>,
        /// which is what a context built outside the interpreter (a host or test
        /// invoking a plugin directly) carries.
        /// </remarks>
        public DateTime UtcNow { get; }

        /// <summary>
        /// Absolute simulated time as of the start of the current PLC cycle.
        /// </summary>
        /// <remarks>
        /// Equal to <see cref="UtcNow"/> unless the body has advanced the clock part-way
        /// through its own cycle. That gap is the whole difference between a "what time
        /// is it" and a "when did this cycle start" library function, and two reads in
        /// one cycle must agree on the latter.
        /// </remarks>
        public DateTime TaskStartUtc { get; }
    }
}
