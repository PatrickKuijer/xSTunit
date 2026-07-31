using System.Collections.Generic;

namespace xStunit.Parser
{
    /// <summary>
    /// The same POU type name is defined in more than one .TcPOU file across
    /// the merged set of directories. Thrown by
    /// <see cref="MultiDirectoryPouLoader"/>.
    /// </summary>
    public sealed class DuplicatePouTypeException : DuplicateNameException
    {
        public string TypeName => Name;

        public DuplicatePouTypeException(string typeName, IReadOnlyList<string> filePaths)
            : base("POU type", typeName, filePaths)
        {
        }
    }
}
