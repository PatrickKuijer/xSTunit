using System;
using System.IO;

namespace xStunit.Parser
{
    /// <summary>
    /// Shared "try-parse, skip the file on failure" guard that keeps one bad
    /// file from aborting a whole directory scan.
    /// </summary>
    public static class StructuralParseGuard
    {
        /// <summary>
        /// Runs <paramref name="parse"/>, returning true on success, or false
        /// with a populated <paramref name="skipped"/> if it threw.
        /// </summary>
        /// <remarks>
        /// Catches broadly ON PURPOSE. Narrowing this to an enumerated list of
        /// "expected" exception types breaks the per-file isolation the guard
        /// exists for: anything not on the list (a locked or vanished file, a
        /// future parser failure mode) escapes past every caller's catch and
        /// takes down the remaining scan, so files next to the bad one are
        /// never discovered.
        ///
        /// <see cref="TcPouRejectedException"/> is the one exclusion - callers
        /// handle an unsupported-construct rejection themselves, with their own
        /// message shape.
        /// </remarks>
        /// <param name="fileKey">Reported as <see cref="SkippedFile.FileKey"/> on failure.</param>
        /// <param name="parse">The parse to attempt.</param>
        /// <param name="result">The parsed value, or default(T) on failure.</param>
        /// <param name="skipped">The skip record, or default on success.</param>
        public static bool TryParseOrSkip<T>(
            string fileKey, Func<T> parse, out T result, out SkippedFile skipped)
        {
            try
            {
                result = parse();
                skipped = default;
                return true;
            }
            catch (Exception ex) when (!(ex is TcPouRejectedException))
            {
                result = default;
                skipped = new SkippedFile(
                    fileKey, $"Failed to parse '{Path.GetFileName(fileKey)}': {ex.Message}");
                return false;
            }
        }
    }
}
