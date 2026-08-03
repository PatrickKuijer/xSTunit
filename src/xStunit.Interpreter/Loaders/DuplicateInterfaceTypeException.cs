using System.Collections.Generic;
using xStunit.Parser;

namespace xStunit.Interpreter
{
    // The same INTERFACE name is declared in more than one .TcIO file across
    // the merged set of POU directories. Thrown by InterfaceLoader.
    //
    // Fail-fast for the same reason as its DuplicateNameException siblings: an
    // ambiguous name must stop registry construction before any suite runs,
    // rather than taking the per-file skip-and-report path a malformed file
    // gets.
    public sealed class DuplicateInterfaceTypeException : DuplicateNameException
    {
        public string InterfaceName => Name;

        public DuplicateInterfaceTypeException(string interfaceName, IReadOnlyList<string> filePaths)
            : base("interface", interfaceName, filePaths)
        {
        }
    }
}
