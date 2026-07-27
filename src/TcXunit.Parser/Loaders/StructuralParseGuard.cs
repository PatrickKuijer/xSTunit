using System;
using System.IO;
using System.Xml;

namespace TcXunit.Parser
{
    // Shared "try-parse, skip file on structural failure" guard (TcXunit-qxp.4).
    // Previously the same catch filter - catch (Exception ex) when (ex is
    // XmlException || ex is NullReferenceException) - was duplicated
    // verbatim across DutStructLoader (x2), GvlLoader, DutAliasLoader and
    // SuiteCaseRunner (all in TcXunit.Interpreter, which already references
    // this project via SkippedFile). A structurally unexpected file
    // (malformed XML, missing expected element) must not abort the whole
    // scan for the rest of the merged directory set (TcXunit-022 et al.);
    // this centralizes exactly which exception shapes count as "structural"
    // and how they're turned into a SkippedFile.
    public static class StructuralParseGuard
    {
        public static bool TryParseOrSkip<T>(
            string fileKey, Func<T> parse, out T result, out SkippedFile skipped)
        {
            try
            {
                result = parse();
                skipped = default;
                return true;
            }
            catch (Exception ex) when (ex is XmlException || ex is NullReferenceException)
            {
                result = default;
                skipped = new SkippedFile(
                    fileKey, $"Failed to parse '{Path.GetFileName(fileKey)}': {ex.Message}");
                return false;
            }
        }
    }
}
