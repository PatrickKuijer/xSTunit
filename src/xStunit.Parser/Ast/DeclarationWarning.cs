using System.Collections.Generic;

namespace xStunit.Parser
{
    /// <summary>
    /// Declaration lines in one file that could not be read, and so cost the
    /// variables they declared while the file itself loaded normally.
    /// </summary>
    /// <remarks>
    /// Deliberately not a <see cref="SkippedFile"/>. A skip means a file's
    /// types are gone entirely and coverage is reduced by that much; a warning
    /// means the file loaded and all but these lines were understood. Folding
    /// the two together would make the skip count mean two different things,
    /// and a run's exit code is unaffected by either.
    /// </remarks>
    public readonly struct DeclarationWarning
    {
        private readonly IReadOnlyList<DeclarationRejection> _rejections;

        /// <param name="fileKey">Full on-disk path, matching <see cref="SkippedFile.FileKey"/>.</param>
        /// <param name="lines">The unread declaration lines, trimmed and stripped of comments, in declaration order.</param>
        public DeclarationWarning(string fileKey, IReadOnlyList<string> lines)
            : this(fileKey, lines, null)
        {
        }

        /// <param name="fileKey">Full on-disk path, matching <see cref="SkippedFile.FileKey"/>.</param>
        /// <param name="lines">The unread or rejected declaration lines, trimmed and stripped of comments, in declaration order.</param>
        /// <param name="rejections">
        /// Declarations among <paramref name="lines"/> that were readable but refused, each with its reason;
        /// null when every line is simply unreadable.
        /// </param>
        public DeclarationWarning(
            string fileKey, IReadOnlyList<string> lines, IReadOnlyList<DeclarationRejection> rejections)
        {
            FileKey = fileKey;
            Lines = lines;
            _rejections = rejections;
        }

        public string FileKey { get; }

        /// <summary>
        /// The declarations in <see cref="Lines"/> that were readable but refused, each with its reason.
        /// Never null; empty when every line is simply unreadable.
        /// </summary>
        public IReadOnlyList<DeclarationRejection> Rejections =>
            _rejections ?? System.Array.Empty<DeclarationRejection>();

        /// <summary>
        /// Never empty: a file with nothing to report produces no warning at
        /// all rather than one carrying an empty list.
        /// </summary>
        public IReadOnlyList<string> Lines { get; }
    }
}
