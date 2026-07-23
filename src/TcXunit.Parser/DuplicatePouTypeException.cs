using System;
using System.Collections.Generic;
using System.Linq;

namespace TcXunit.Parser
{
    // Thrown by MultiDirectoryPouLoader (TcXunit-98e.1) when the same POU type
    // name is defined in more than one file across the merged set of POU
    // directories. This is a hard, fail-fast error distinct from the
    // per-file TcPouRejectedException skip/report path: a duplicate type name
    // is ambiguous and must stop discovery before any suite runs, naming the
    // type and every conflicting file path.
    public sealed class DuplicatePouTypeException : Exception
    {
        public string TypeName { get; }
        public IReadOnlyList<string> FilePaths { get; }

        public DuplicatePouTypeException(string typeName, IReadOnlyList<string> filePaths)
            : base(BuildMessage(typeName, filePaths))
        {
            TypeName = typeName;
            FilePaths = filePaths;
        }

        private static string BuildMessage(string typeName, IReadOnlyList<string> filePaths) =>
            $"duplicate POU type '{typeName}' defined in multiple files: {string.Join(", ", filePaths.OrderBy(p => p, StringComparer.Ordinal))}";
    }
}
