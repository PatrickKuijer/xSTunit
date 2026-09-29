namespace xStunit.Parser
{
    /// <summary>
    /// A declaration the parser read but the runtime refused, with the reason.
    /// </summary>
    public readonly struct DeclarationRejection
    {
        /// <param name="line">The declaration, as it appears in <see cref="DeclarationWarning.Lines"/>.</param>
        /// <param name="reason">Why the runtime refused it.</param>
        public DeclarationRejection(string line, string reason)
        {
            Line = line;
            Reason = reason;
        }

        public string Line { get; }

        public string Reason { get; }
    }
}
