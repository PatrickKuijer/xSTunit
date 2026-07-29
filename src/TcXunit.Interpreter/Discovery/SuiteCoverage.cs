using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TcXunit.Parser;

namespace TcXunit.Interpreter
{
    // Which POUs any suite exercises, and - the point of the exercise - which
    // ones none does (TcXunit-3tx.4).
    //
    // Not a coverage percentage and not a CI gate: TcXunit is the verification
    // step in an agentic loop, and an uncovered POU is a directly usable next
    // task ("write a suite for F_ComputeChecksum"). The same data a human reads
    // as a coverage report, aimed at the agent picking what to do next.
    //
    // Association is by direct textual reference from a suite - deliberately
    // crude, and enough to be useful. Real line/branch coverage through the
    // interpreter is a later and much larger step (unusually cheap for TcXunit
    // compared with any on-target tool, since the engine already walks every
    // statement), and would replace this rule without changing its shape.
    public static class SuiteCoverage
    {
        // types is every loaded POU, suiteTypeNames the ones SuiteDiscovery
        // identified. Returns one entry per non-suite POU, in the order they
        // were loaded, each listing the suites that mention it.
        public static IReadOnlyList<PouCoverage> Analyze(
            IReadOnlyList<PouAst> types, IReadOnlyCollection<string> suiteTypeNames)
        {
            var suiteNameSet = new HashSet<string>(suiteTypeNames, StringComparer.OrdinalIgnoreCase);

            // Concatenating each suite's declaration and every body it owns is
            // enough: the reference that matters is a VAR declaration of the
            // type under test or a call into it, and both are in this text.
            var suiteTexts = types
                .Where(t => suiteNameSet.Contains(t.Name))
                .Select(t => new { t.Name, Text = AllText(t) })
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

        // Whole-identifier, case-insensitive - IEC 61131-3 names are
        // case-insensitive, like every other type lookup here (TcXunit-fzm).
        // The word boundaries are what stop "FB_Counter" from matching inside
        // "FB_CounterExtended" and reporting a POU as covered by a suite that
        // never mentions it. \b works because an IEC identifier is exactly
        // [A-Za-z0-9_], the same character class \w matches.
        private static bool MentionsType(string suiteText, string typeName) =>
            Regex.IsMatch(suiteText, $@"\b{Regex.Escape(typeName)}\b", RegexOptions.IgnoreCase);
    }

    // One POU and the suites exercising it (TcXunit-3tx.4).
    public sealed class PouCoverage
    {
        public PouCoverage(string pouTypeName, IReadOnlyList<string> suiteTypeNames)
        {
            PouTypeName = pouTypeName;
            SuiteTypeNames = suiteTypeNames;
        }

        public string PouTypeName { get; }

        // The suites that reference this POU, in load order. Empty - never
        // null - when nothing does, which is the case the work list is for.
        public IReadOnlyList<string> SuiteTypeNames { get; }

        public bool IsCovered => SuiteTypeNames.Count > 0;
    }
}
