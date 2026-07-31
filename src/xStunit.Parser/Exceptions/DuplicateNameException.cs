using System;
using System.Collections.Generic;
using System.Linq;

namespace xStunit.Parser
{
    // Shared shape for the "same name defined in more than one file across
    // the merged set of POU directories" family of hard, fail-fast errors
    // (TcXunit-qxp.3): DuplicatePouTypeException (POU types, this project),
    // DuplicateStructTypeException (STRUCT DUTs) and DuplicateGvlNameException
    // (GVLs), the latter two in xStunit.Interpreter which already references
    // this project. Each subclass differs only in the noun used in the
    // message and the name of its name-exposing property (kept distinct -
    // TypeName vs GvlName - for source compatibility with existing callers).
    public abstract class DuplicateNameException : Exception
    {
        public string Name { get; }
        public IReadOnlyList<string> FilePaths { get; }

        protected DuplicateNameException(string noun, string name, IReadOnlyList<string> filePaths)
            : base(BuildMessage(noun, name, filePaths))
        {
            Name = name;
            FilePaths = filePaths;
        }

        private static string BuildMessage(string noun, string name, IReadOnlyList<string> filePaths) =>
            $"duplicate {noun} '{name}' defined in multiple files: {string.Join(", ", filePaths.OrderBy(p => p, StringComparer.Ordinal))}";
    }

    // Extracted "group by name, take the first group with more than one
    // entry, throw" shape (TcXunit-qxp.3) that was previously copy-pasted in
    // MultiDirectoryPouLoader.CheckForDuplicates, DutStructLoader.Load and
    // GvlLoader.Load. Callers supply how to get a name and a file path out
    // of their item type, plus a factory for the specific exception to
    // throw (which differs per caller, but all derive from
    // DuplicateNameException above).
    public static class DuplicateNameDetector
    {
        public static void ThrowIfDuplicate<T>(
            IEnumerable<T> items,
            Func<T, string> nameSelector,
            Func<T, string> filePathSelector,
            Func<string, IReadOnlyList<string>, Exception> exceptionFactory)
        {
            var duplicate = items
                .GroupBy(nameSelector)
                .FirstOrDefault(g => g.Count() > 1);

            if (duplicate != null)
                throw exceptionFactory(duplicate.Key, duplicate.Select(filePathSelector).ToList());
        }
    }
}
