using System;
using System.Collections.Generic;
using System.Linq;

namespace TcXunit.Interpreter
{
    // Thrown by GvlLoader (TcXunit-71o) when the same GVL name is declared
    // in more than one .TcGVL file across the merged set of POU directories.
    // Mirrors DuplicateStructTypeException/DuplicatePouTypeException's
    // shape/behavior: a duplicate GVL name is ambiguous and must stop
    // registry construction before any suite runs, naming the GVL and every
    // conflicting file path.
    public sealed class DuplicateGvlNameException : Exception
    {
        public string GvlName { get; }
        public IReadOnlyList<string> FilePaths { get; }

        public DuplicateGvlNameException(string gvlName, IReadOnlyList<string> filePaths)
            : base(BuildMessage(gvlName, filePaths))
        {
            GvlName = gvlName;
            FilePaths = filePaths;
        }

        private static string BuildMessage(string gvlName, IReadOnlyList<string> filePaths) =>
            $"duplicate GVL '{gvlName}' defined in multiple files: {string.Join(", ", filePaths.OrderBy(p => p, StringComparer.Ordinal))}";
    }
}
