using System;
using System.Collections.Generic;
using System.Linq;

namespace TcXunit.Interpreter
{
    // Thrown by SuiteCaseRunner.BuildRegistry (TcXunit-dvd) when the same
    // STRUCT type name is declared in more than one .TcDUT file across the
    // merged set of POU directories. Mirrors DuplicatePouTypeException's
    // shape/behavior for POU types: a duplicate STRUCT name is ambiguous and
    // must stop registry construction before any suite runs, naming the type
    // and every conflicting file path, instead of letting TypeRegistry's
    // constructor silently let the later file win.
    public sealed class DuplicateStructTypeException : Exception
    {
        public string TypeName { get; }
        public IReadOnlyList<string> FilePaths { get; }

        public DuplicateStructTypeException(string typeName, IReadOnlyList<string> filePaths)
            : base(BuildMessage(typeName, filePaths))
        {
            TypeName = typeName;
            FilePaths = filePaths;
        }

        private static string BuildMessage(string typeName, IReadOnlyList<string> filePaths) =>
            $"duplicate STRUCT type '{typeName}' defined in multiple files: {string.Join(", ", filePaths.OrderBy(p => p, StringComparer.Ordinal))}";
    }
}
