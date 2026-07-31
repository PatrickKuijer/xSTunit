using System;
using System.IO;

namespace xStunit.Parser
{
    /// <summary>
    /// Shared "try-parse, skip file on structural failure" guard.
    /// </summary>
    /// <remarks>
    /// A structurally unexpected file (malformed XML, missing expected
    /// element, or any other failure mode not specifically anticipated) must
    /// not abort the whole scan for the rest of the merged directory set -
    /// this centralizes exactly which exception shapes count as "structural"
    /// and how they're turned into a <see cref="SkippedFile"/>, instead of
    /// each loader duplicating its own catch filter.
    ///
    /// The guard catches every exception EXCEPT
    /// <see cref="TcPouRejectedException"/>: an earlier, narrower filter
    /// (catching only XmlException/NullReferenceException, the two shapes a
    /// malformed/short .TcPOU actually produces) let any other failure mode
    /// - an IOException from a file that disappeared or got locked mid-scan,
    /// a stray ArgumentException, or any future parser failure nobody has
    /// hit yet - propagate straight out of this method uncaught, past every
    /// caller's own catch (all of which only expect what this guard already
    /// promised to handle), aborting the ENTIRE remaining scan rather than
    /// just the one bad file. Catching broadly (rather than by an
    /// enumerated exception list) is what keeps that per-file-isolation
    /// invariant true regardless of which failure mode a given file
    /// triggers. TcPouRejectedException stays excluded because callers (the
    /// CLI's suite discovery) handle it via their own dedicated catch site
    /// with its own distinct message shape.
    /// </remarks>
    public static class StructuralParseGuard
    {
        /// <summary>
        /// Runs <paramref name="parse"/>, returning true and the parsed
        /// value in <paramref name="result"/> on success, or false and a
        /// <see cref="SkippedFile"/> in <paramref name="skipped"/> if
        /// <paramref name="parse"/> throws anything other than
        /// <see cref="TcPouRejectedException"/> (which propagates uncaught).
        /// </summary>
        /// <param name="fileKey">The file path (or conflicting name) to report if parsing fails.</param>
        /// <param name="parse">Performs the parse; its return value becomes <paramref name="result"/> on success.</param>
        /// <param name="result">The parsed value on success; default(T) on failure.</param>
        /// <param name="skipped">Default on success; the skip record on failure.</param>
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
