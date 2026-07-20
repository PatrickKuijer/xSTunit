using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TcXunit.Interpreter
{
    // Parses the raw VAR/VAR_INPUT/VAR_OUTPUT/VAR_IN_OUT declaration text a
    // FUNCTION_BLOCK or METHOD carries in its Declaration CDATA into typed
    // VarDecl entries. Scoped to the fixture's grammar (TcXunit-w5x.8): single
    // name per line, no comma lists, no ARRAY/STRUCT types.
    public static class VarBlockParser
    {
        private static readonly Regex VarLinePattern = new Regex(
            @"^(?<name>\w+)\s*:\s*(?<type>POINTER TO \w+|REFERENCE TO \w+|\w+)\s*(:=\s*(?<default>.+?))?;$",
            RegexOptions.Compiled);

        public static IReadOnlyList<VarDecl> Parse(string declarationText)
        {
            var result = new List<VarDecl>();
            var currentSection = (VarSection?)null;

            foreach (var rawLine in declarationText.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0)
                    continue;

                switch (line)
                {
                    case "VAR":
                        currentSection = VarSection.Local;
                        continue;
                    case "VAR_INPUT":
                        currentSection = VarSection.Input;
                        continue;
                    case "VAR_OUTPUT":
                        currentSection = VarSection.Output;
                        continue;
                    case "VAR_IN_OUT":
                        currentSection = VarSection.InOut;
                        continue;
                    case "END_VAR":
                        currentSection = null;
                        continue;
                }

                if (currentSection == null)
                    continue;

                var match = VarLinePattern.Match(line);
                if (!match.Success)
                    continue;

                var defaultGroup = match.Groups["default"];
                result.Add(new VarDecl(
                    match.Groups["name"].Value,
                    match.Groups["type"].Value,
                    defaultGroup.Success ? defaultGroup.Value : null,
                    currentSection.Value));
            }

            return result;
        }
    }
}
