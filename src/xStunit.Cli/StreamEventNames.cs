namespace xStunit.Cli
{
    // The wire values of the `event` discriminator every --stream line carries,
    // spelled once here because a typo at an emitting site is not a compile
    // error: the consumer just stops recognising the line and drops it. The
    // consuming side holds its own copy of these strings
    // (src/xStunit.Vsix/TestRunner/XstunitModels.cs) - separate assemblies, and
    // the strings themselves are the contract between them, so neither side may
    // change one to suit itself.
    internal static class StreamEventNames
    {
        public const string Discovery = "discovery";
        public const string SuiteStart = "suite-start";
        public const string SuiteResult = "suite-result";
        public const string Summary = "summary";
        public const string Error = "error";
    }
}
