using System;
using System.IO;

namespace xStunit.Parser
{
    // Shared "try-parse, skip file on structural failure" guard (TcXunit-qxp.4).
    // Previously the same catch filter - catch (Exception ex) when (ex is
    // XmlException || ex is NullReferenceException) - was duplicated
    // verbatim across DutStructLoader (x2), GvlLoader, DutAliasLoader and
    // SuiteCaseRunner (all in xStunit.Interpreter, which already references
    // this project via SkippedFile). A structurally unexpected file
    // (malformed XML, missing expected element) must not abort the whole
    // scan for the rest of the merged directory set (TcXunit-022 et al.);
    // this centralizes exactly which exception shapes count as "structural"
    // and how they're turned into a SkippedFile.
    //
    // TcXunit-jql: the filter used to enumerate only XmlException/
    // NullReferenceException (the two shapes malformed/short .TcPOU XML
    // actually produces), which meant any OTHER exception type raised while
    // reading or parsing a single file - an IOException from a file that
    // disappeared/got locked mid-scan, a stray ArgumentException, or any
    // future parser failure mode nobody has hit yet - propagated straight
    // out of this method uncaught, past every caller's own catch (all of
    // which only expect what this guard already promised to handle), and
    // aborted the ENTIRE remaining scan: every other file in the merged
    // directory set, including any suite sitting right next to the bad one,
    // silently never got parsed or discovered. That is exactly the
    // per-file-isolation invariant this class exists to uphold (see the
    // paragraph above and CliRunner's own "never abort the whole run for one
    // bad file" comments) - so the guard now catches every exception except
    // TcPouRejectedException, which callers (CliRunner.Run) still handle via
    // their own dedicated catch site with its own distinct message shape.
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
