using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using xStunit.Parser;

namespace xStunit.Interpreter
{
    // Which POUs any suite exercises, and - the point of the exercise - which
    // ones none does.
    //
    // Not a coverage percentage and not a CI gate: an uncovered POU is meant
    // to read as a directly usable next task ("write a suite for
    // F_ComputeChecksum") for whoever or whatever is driving the loop.
    //
    // Association is by direct textual reference from a suite: deliberately
    // crude, and enough to be useful. Real line/branch coverage through the
    // interpreter would replace this rule without changing the shape of what
    // it reports.
    public static class SuiteCoverage
    {
        // Given every loaded POU and the subset SuiteDiscovery identified as
        // suites, returns one entry per NON-suite POU, in load order.
        public static IReadOnlyList<PouCoverage> Analyze(
            IReadOnlyList<PouAst> types, IReadOnlyCollection<string> suiteTypeNames)
        {
            var suiteNameSet = new HashSet<string>(suiteTypeNames, StringComparer.OrdinalIgnoreCase);

            // Concatenating each suite's declaration and every body it owns
            // is enough: the reference that matters is a VAR declaration of
            // the type under test or a call into it, and both are in there.
            //
            // Comments are stripped first because a type named only inside a
            // (* ... *) or // comment is not a reference a suite exercises,
            // and the whole-word regex below cannot tell code from prose.
            var suiteTexts = types
                .Where(t => suiteNameSet.Contains(t.Name))
                .Select(t => new { t.Name, Text = Lexer.StripComments(AllText(t)) })
                .ToList();

            return types
                .Where(t => !suiteNameSet.Contains(t.Name))
                .Select(t => new PouCoverage(
                    t.Name,
                    suiteTexts.Where(s => MentionsType(s.Text, t.Name)).Select(s => s.Name).ToList()))
                .ToList();
        }

        private static string AllText(PouAst pou) =>
            string.Join(
                "\n",
                new[] { pou.DeclarationText, pou.ImplementationText }
                    .Concat(pou.Methods.SelectMany(m => new[] { m.DeclarationText, m.ImplementationText }))
                    .Concat(pou.Properties.SelectMany(p =>
                        new[] { p.DeclarationText, p.GetImplementationText, p.SetImplementationText }))
                    .Where(text => text != null));

        // Case-insensitive because IEC 61131-3 names are, like every other
        // type lookup here. The word boundaries are what stop "FB_Counter"
        // matching inside "FB_CounterExtended" and reporting a POU as covered
        // by a suite that never mentions it; \b is exact here because an IEC
        // identifier is [A-Za-z0-9_], the character class \w matches.
        private static bool MentionsType(string suiteText, string typeName) =>
            Regex.IsMatch(suiteText, $@"\b{Regex.Escape(typeName)}\b", RegexOptions.IgnoreCase);
    }

    public sealed class PouCoverage
    {
        public PouCoverage(string pouTypeName, IReadOnlyList<string> suiteTypeNames)
        {
            PouTypeName = pouTypeName;
            SuiteTypeNames = suiteTypeNames;
        }

        public string PouTypeName { get; }

        // In load order, and empty - never null - when nothing references
        // this POU, which is the case the work list exists for.
        public IReadOnlyList<string> SuiteTypeNames { get; }

        public bool IsCovered => SuiteTypeNames.Count > 0;
    }
}
