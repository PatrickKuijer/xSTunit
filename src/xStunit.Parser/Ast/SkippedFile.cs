namespace xStunit.Parser
{
    /// <summary>
    /// A file (or, for a duplicate-name conflict that can't be attributed to
    /// a single file, a conflicting type name) that couldn't be resolved -
    /// skip it and report, but don't abort the whole scan.
    /// </summary>
    /// <remarks>
    /// Shared shape reused by every loader in this project and in
    /// xStunit.Interpreter (which already references this project), rather
    /// than each loader declaring its own near-identical struct.
    ///
    /// Named <see cref="FileKey"/> rather than "FilePath" since some callers
    /// key an entry by a duplicate type/STRUCT/GVL name instead of a file
    /// path - the more general of the two names that shape has carried.
    /// </remarks>
    public readonly struct SkippedFile
    {
        /// <summary>Constructs a skipped-file record from its key and the reason it was skipped.</summary>
        public SkippedFile(string fileKey, string message)
        {
            FileKey = fileKey;
            Message = message;
        }

        /// <summary>The file path, or the conflicting name, that identifies what was skipped.</summary>
        public string FileKey { get; }

        /// <summary>Human-readable reason the file (or name) was skipped.</summary>
        public string Message { get; }
    }
}
