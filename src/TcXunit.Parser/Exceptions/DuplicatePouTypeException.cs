using System.Collections.Generic;

namespace TcXunit.Parser
{
    // Thrown by MultiDirectoryPouLoader (TcXunit-98e.1) when the same POU type
    // name is defined in more than one file across the merged set of POU
    // directories. This is a hard, fail-fast error distinct from the
    // per-file TcPouRejectedException skip/report path: a duplicate type name
    // is ambiguous and must stop discovery before any suite runs, naming the
    // type and every conflicting file path.
    public sealed class DuplicatePouTypeException : DuplicateNameException
    {
        public string TypeName => Name;

        public DuplicatePouTypeException(string typeName, IReadOnlyList<string> filePaths)
            : base("POU type", typeName, filePaths)
        {
        }
    }
}
