using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace xStunit.Interpreter.Conformance
{
    // What LayoutOracle.Compare found: every disagreement between a .tmc's
    // declared layout and xStunit's, plus every declared type the comparison
    // could not reach and why.
    public sealed class LayoutReport
    {
        public LayoutReport(
            string moduleName,
            string targetPlatform,
            int declaredTypeCount,
            int comparedTypeCount,
            IReadOnlyList<LayoutFinding> findings)
        {
            ModuleName = moduleName;
            TargetPlatform = targetPlatform;
            DeclaredTypeCount = declaredTypeCount;
            ComparedTypeCount = comparedTypeCount;
            Findings = findings;
        }

        public string ModuleName { get; }

        public string TargetPlatform { get; }

        public int DeclaredTypeCount { get; }

        // How many of them xStunit's layout math was actually run against - the
        // denominator any conformance claim from this report has to quote.
        public int ComparedTypeCount { get; }

        public IReadOnlyList<LayoutFinding> Findings { get; }

        // The findings that are a real disagreement about a layout rule, as
        // opposed to a gap in what xStunit models.
        public IEnumerable<LayoutFinding> Mismatches => Findings.Where(f =>
            f.Kind == LayoutFindingKind.TypeSize ||
            f.Kind == LayoutFindingKind.MemberOffset ||
            f.Kind == LayoutFindingKind.MemberSize);

        // Line-per-finding rendering, stable enough to commit as an expected
        // file and diff a later run against.
        public string ToText()
        {
            var text = new StringBuilder();
            text.Append("module ").Append(ModuleName ?? "(none)")
                .Append(" target ").Append(TargetPlatform ?? "(none)").Append('\n');
            text.Append("compared ").Append(ComparedTypeCount)
                .Append(" of ").Append(DeclaredTypeCount).Append(" declared types\n");
            text.Append("sizes and offsets in bits\n");

            foreach (var finding in Findings)
            {
                text.Append(finding.Kind).Append(' ').Append(finding.Subject);
                if (finding.DeclaredBits.HasValue || finding.ComputedBits.HasValue)
                {
                    text.Append(" declared=").Append(Show(finding.DeclaredBits))
                        .Append(" computed=").Append(Show(finding.ComputedBits));
                }
                if (finding.Detail != null)
                    text.Append(' ').Append(finding.Detail);
                text.Append('\n');
            }

            return text.ToString();
        }

        private static string Show(int? bits) => bits.HasValue ? bits.Value.ToString() : "(none)";
    }
}
