using System.Collections.Generic;
using xStunit.Parser;

namespace xStunit.Interpreter
{
    // The same STRUCT type name is declared in more than one .TcDUT file
    // across the merged set of POU directories. Thrown by DutStructLoader.Load.
    //
    // The STRUCT member of the DuplicateNameException family - a sibling of
    // DuplicateGvlNameException (.TcGVL names) and DuplicatePouTypeException
    // (.TcPOU types), differing only in which kind of name it reports.
    // Fail-fast because TypeRegistry's constructor would otherwise silently
    // let whichever file loaded last win.
    public sealed class DuplicateStructTypeException : DuplicateNameException
    {
        public string TypeName => Name;

        public DuplicateStructTypeException(string typeName, IReadOnlyList<string> filePaths)
            : base("STRUCT type", typeName, filePaths)
        {
        }
    }
}
