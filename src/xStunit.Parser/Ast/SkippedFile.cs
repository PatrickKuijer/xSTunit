namespace xStunit.Parser
{
    /// <summary>
    /// Something that couldn't be resolved and was skipped rather than
    /// aborting the whole scan.
    /// </summary>
    public readonly struct SkippedFile
    {
        public SkippedFile(string fileKey, string message)
        {
            FileKey = fileKey;
            Message = message;
        }

        /// <summary>
        /// Usually a file path, but a duplicate-name conflict that can't be
        /// attributed to one file is keyed by the conflicting name instead -
        /// hence "key" rather than "path".
        /// </summary>
        public string FileKey { get; }

        public string Message { get; }
    }
}
