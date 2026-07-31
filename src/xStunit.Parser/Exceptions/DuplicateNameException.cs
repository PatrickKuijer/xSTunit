using System;
using System.Collections.Generic;
using System.Linq;

namespace xStunit.Parser
{
    /// <summary>
    /// Base for the "same name defined in more than one file across the
    /// merged set of POU directories" family of hard, fail-fast errors.
    /// </summary>
    /// <remarks>
    /// Concrete subclasses: <see cref="DuplicatePouTypeException"/> (POU
    /// types, this project), plus DuplicateStructTypeException (STRUCT DUTs)
    /// and DuplicateGvlNameException (GVLs) in xStunit.Interpreter, which
    /// already references this project. Each subclass differs only in the
    /// noun used in the message and the name of its name-exposing property
    /// (kept distinct - e.g. TypeName vs GvlName - for source compatibility
    /// with existing callers).
    /// </remarks>
    public abstract class DuplicateNameException : Exception
    {
        /// <summary>The duplicated name (type, STRUCT, or GVL name, depending on the subclass).</summary>
        public string Name { get; }

        /// <summary>Every file path the duplicated name was found in.</summary>
        public IReadOnlyList<string> FilePaths { get; }

        /// <summary>Constructs a duplicate-name exception, formatting its message from the given noun, name, and conflicting file paths.</summary>
        protected DuplicateNameException(string noun, string name, IReadOnlyList<string> filePaths)
            : base(BuildMessage(noun, name, filePaths))
        {
            Name = name;
            FilePaths = filePaths;
        }

        private static string BuildMessage(string noun, string name, IReadOnlyList<string> filePaths) =>
            $"duplicate {noun} '{name}' defined in multiple files: {string.Join(", ", filePaths.OrderBy(p => p, StringComparer.Ordinal))}";
    }

    /// <summary>
    /// Detects a name defined more than once across a set of items and
    /// throws the caller-supplied exception for it.
    /// </summary>
    /// <remarks>
    /// Extracts the "group by name, take the first group with more than one
    /// entry, throw" shape that was previously copy-pasted across each
    /// loader's own duplicate check. Callers supply how to get a name and a
    /// file path out of their item type, plus a factory for the specific
    /// exception to throw (which differs per caller, but all derive from
    /// <see cref="DuplicateNameException"/>).
    /// </remarks>
    public static class DuplicateNameDetector
    {
        /// <summary>
        /// Throws the exception <paramref name="exceptionFactory"/> builds
        /// for the first name that <paramref name="nameSelector"/> maps more
        /// than one item in <paramref name="items"/> to; does nothing when
        /// every name is unique.
        /// </summary>
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
