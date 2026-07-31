using System.Collections.Generic;
using xStunit.Parser;

namespace xStunit.Interpreter
{
    // Thrown by GvlLoader (TcXunit-71o) when the same GVL name is declared
    // in more than one .TcGVL file across the merged set of POU directories.
    // Mirrors DuplicateStructTypeException/DuplicatePouTypeException's
    // shape/behavior: a duplicate GVL name is ambiguous and must stop
    // registry construction before any suite runs, naming the GVL and every
    // conflicting file path.
    public sealed class DuplicateGvlNameException : DuplicateNameException
    {
        public string GvlName => Name;

        public DuplicateGvlNameException(string gvlName, IReadOnlyList<string> filePaths)
            : base("GVL", gvlName, filePaths)
        {
        }
    }
}
