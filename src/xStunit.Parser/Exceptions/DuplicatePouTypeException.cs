using System.Collections.Generic;

namespace xStunit.Parser
{
    /// <summary>
    /// Thrown by <see cref="MultiDirectoryPouLoader"/> when the same POU
    /// type name is defined in more than one file across the merged set of
    /// POU directories.
    /// </summary>
    /// <remarks>
    /// A hard, fail-fast error distinct from the per-file
    /// <see cref="TcPouRejectedException"/> skip/report path: a duplicate
    /// type name is ambiguous and must stop discovery before any suite runs,
    /// naming the type and every conflicting file path.
    /// </remarks>
    public sealed class DuplicatePouTypeException : DuplicateNameException
    {
        /// <summary>The duplicated POU type name.</summary>
        public string TypeName => Name;

        /// <summary>Constructs the exception for the given duplicated type name and conflicting file paths.</summary>
        public DuplicatePouTypeException(string typeName, IReadOnlyList<string> filePaths)
            : base("POU type", typeName, filePaths)
        {
        }
    }
}
