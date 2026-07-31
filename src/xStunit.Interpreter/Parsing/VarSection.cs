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
        // top-level declaration block (as opposed to inside a METHOD/ACTION
        // body, which stays mapped to Local - BindParams already rebuilds
        // those fresh in a new Frame every CallMethod call). Distinguished
        // from Local so Engine.NewInstance/IsPersistedField and the
        // top-level-body reset path (Engine.ResetTopLevelTempFields) can
        // treat it as reset-on-every-invocation storage rather than a
        // persisted VAR field (TcXunit-9go).
        Temp,
    }
}
