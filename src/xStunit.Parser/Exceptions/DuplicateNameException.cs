using System;
using System.Collections.Generic;
using System.Linq;

namespace xStunit.Parser
{
    /// <summary>
    /// Base for the "same name defined in more than one file across the
    /// merged set of POU directories" family of hard, fail-fast errors -
    /// deliberately not the per-file skip-and-report path, since an
    /// ambiguous name must stop discovery before any suite runs.
    /// </summary>
    /// <remarks>
    /// Subclasses expose <see cref="Name"/> again under a domain-specific
    /// alias (TypeName, GvlName, ...) and differ only in the noun their
    /// message uses.
    /// </remarks>
    public abstract class DuplicateNameException : Exception
    {
        public string Name { get; }

        /// <summary>Every file the duplicated name was found in.</summary>
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

    public static class DuplicateNameDetector
    {
        /// <summary>
        /// Throws whatever <paramref name="exceptionFactory"/> builds for the
        /// first name shared by more than one item; returns silently when
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
