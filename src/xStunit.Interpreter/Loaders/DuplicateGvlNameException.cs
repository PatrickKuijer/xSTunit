using System.Collections.Generic;
using xStunit.Parser;

namespace xStunit.Interpreter
{
    // The same GVL name is declared in more than one .TcGVL file across the
    // merged set of POU directories. Thrown by GvlLoader.
    //
    // The GVL member of the DuplicateNameException family - a sibling of
    // DuplicateStructTypeException (.TcDUT STRUCT types) and
    // DuplicatePouTypeException (.TcPOU types), differing only in which kind
    // of name it reports. All three are fail-fast: an ambiguous name must
    // stop registry construction before any suite runs, rather than taking
    // the per-file skip-and-report path a malformed file gets.
    public sealed class DuplicateGvlNameException : DuplicateNameException
    {
        public string GvlName => Name;

        public DuplicateGvlNameException(string gvlName, IReadOnlyList<string> filePaths)
            : base("GVL", gvlName, filePaths)
        {
        }
    }
}
