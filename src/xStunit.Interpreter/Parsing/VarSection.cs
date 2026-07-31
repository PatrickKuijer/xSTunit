namespace xStunit.Interpreter
{
    public enum VarSection
    {
        Local,
        Input,
        Output,
        InOut,
        Global,

        // VAR_TEMP declared directly in a FUNCTION_BLOCK/PROGRAM's own
        // top-level declaration block. A METHOD/ACTION's VAR_TEMP stays
        // mapped to Local, because BindParams already rebuilds those fresh in
        // a new Frame per call; only the top-level case needs its own section
        // so Engine.ResetTopLevelTempFields can clear it every invocation
        // instead of persisting it like a VAR field.
        Temp,
    }
}
