namespace TcXunit.Parser
{
    // Shared shape for "this file (or, for duplicate-name conflicts that
    // can't be attributed to a single file, this conflicting type name)
    // couldn't be resolved - skip it and report, but don't abort the whole
    // scan" (TcXunit-qxp.2). Previously duplicated as SuiteCaseRunner's
    // private SkippedPou plus near-identical SkippedFile structs nested in
    // DutStructLoader, GvlLoader and DutAliasLoader (all in
    // TcXunit.Interpreter, which already references this project).
    //
    // Named FileKey rather than FilePath since SuiteCaseRunner.BuildRegistry
    // also keys some entries by a duplicate type/STRUCT/GVL name instead of
    // a file path (TcXunit-qxp.1) - the more general of the two original
    // property names.
    public readonly struct SkippedFile
    {
        public SkippedFile(string fileKey, string message)
        {
            FileKey = fileKey;
            Message = message;
        }

        public string FileKey { get; }
        public string Message { get; }
    }
}
