using System.Collections.Generic;
using TcXunit.Parser;

namespace TcXunit.Interpreter
{
    // Thrown by DutStructLoader.Load (TcXunit-dvd) when the same
    // STRUCT type name is declared in more than one .TcDUT file across the
    // merged set of POU directories. Mirrors DuplicatePouTypeException's
    // shape/behavior for POU types: a duplicate STRUCT name is ambiguous and
    // must stop registry construction before any suite runs, naming the type
    // and every conflicting file path, instead of letting TypeRegistry's
    // constructor silently let the later file win.
    public sealed class DuplicateStructTypeException : DuplicateNameException
    {
        public string TypeName => Name;

        public DuplicateStructTypeException(string typeName, IReadOnlyList<string> filePaths)
            : base("STRUCT type", typeName, filePaths)
        {
        }
    }
}
