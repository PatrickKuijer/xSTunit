using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using xStunit.Interpreter;
using xStunit.Parser;

namespace xStunit.Cli
{
    // Turns the directories named on the command line into the type universe a
    // run executes against: every .TcPOU, .TcDUT and .TcGVL under them, merged
    // into one TypeRegistry, plus the list of files that could not be read.
    public static class WorkspaceLoader
    {
        public static LoadedWorkspace Load(IReadOnlyList<string> directories)
        {
            var skipped = new List<SkippedFile>();

            foreach (var path in directories)
            {
                if (!Directory.Exists(path))
                    return LoadedWorkspace.Failed($"path does not exist: {path}", skipped);
            }

            // Parsed file by file rather than through
            // MultiDirectoryPouLoader.Load, which propagates the first
            // TcPouRejectedException and takes the whole run down with it: a
            // real tree always contains POUs outside the parse subset
            // (Tc2_System, __NEW, ...), and one of them must not make every
            // other suite in the tree unrunnable. A suite that genuinely
            // depends on a skipped POU still fails clearly at run time with an
            // unresolved-type error.
            var loaded = new List<LoadedPou>();
            foreach (var file in MultiDirectoryPouLoader.FindPouFiles(directories))
            {
                try
                {
                    if (StructuralParseGuard.TryParseOrSkip(
                            file, () => TcPouParser.Parse(File.ReadAllText(file)), out var pou, out var skip))
                        loaded.Add(new LoadedPou(pou, file));
                    else
                        skipped.Add(skip);
                }
                catch (TcPouRejectedException ex)
                {
                    skipped.Add(new SkippedFile(file, ex.Message));
                }
            }

            // A duplicate type name across the merged directory set is a hard
            // error, not a skip: it means the caller pointed the CLI at an
            // inconsistent set of directories, which is usage, not an
            // unsupported file. Every duplicate-name check below follows suit.
            try
            {
                MultiDirectoryPouLoader.CheckForDuplicates(loaded);
            }
            catch (DuplicatePouTypeException ex)
            {
                return LoadedWorkspace.Failed(ex.Message, skipped);
            }

            var types = loaded.Select(l => l.Pou).ToList();

            IReadOnlyList<StructAst> structTypes;
            try
            {
                structTypes = DutStructLoader.Load(directories, out var dutSkipped);
                skipped.AddRange(dutSkipped);
            }
            catch (DuplicateStructTypeException ex)
            {
                return LoadedWorkspace.Failed(ex.Message, skipped);
            }

            var aliases = DutAliasLoader.Load(directories, out var aliasSkipped).ToDictionary(kv => kv.Key, kv => kv.Value);
            skipped.AddRange(aliasSkipped);

            // Enum names are merged into the alias map so SIZEOF() and every
            // other ResolveAlias call site resolves an enum to its underlying
            // integer type without a second lookup path.
            var enumAliases = DutEnumLoader.Load(directories, out var enumSkipped, out var enumMembers);
            skipped.AddRange(enumSkipped);
            foreach (var enumAlias in enumAliases)
                aliases[enumAlias.Key] = enumAlias.Value;

            IReadOnlyList<GvlAst> gvls;
            try
            {
                gvls = GvlLoader.Load(directories, out var gvlSkipped);
                skipped.AddRange(gvlSkipped);
            }
            catch (DuplicateGvlNameException ex)
            {
                return LoadedWorkspace.Failed(ex.Message, skipped);
            }

            return LoadedWorkspace.Succeeded(
                new TypeRegistry(types, structTypes, gvls, aliases, enumMembers),
                types,
                loaded.ToDictionary(l => l.Pou.Name, l => l.FilePath),
                skipped);
        }
    }

    // What a load leaves behind, whether or not it got all the way through.
    public sealed class LoadedWorkspace
    {
        private LoadedWorkspace(
            TypeRegistry registry,
            IReadOnlyList<PouAst> pouTypes,
            IReadOnlyDictionary<string, string> filePathsByTypeName,
            IReadOnlyList<SkippedFile> skipped,
            string error)
        {
            Registry = registry;
            PouTypes = pouTypes;
            FilePathsByTypeName = filePathsByTypeName;
            Skipped = skipped;
            Error = error;
        }

        public TypeRegistry Registry { get; }

        public IReadOnlyList<PouAst> PouTypes { get; }

        public IReadOnlyDictionary<string, string> FilePathsByTypeName { get; }

        // Files that could not be read, in the order they were met. Never an
        // error in itself: a skip costs its own file's types and nothing else,
        // so a load that skipped files still hands back a usable registry and
        // the run that follows still ends by test outcome.
        public IReadOnlyList<SkippedFile> Skipped { get; }

        // Non-null only for a load that produced no registry at all - a
        // directory that does not exist, or a type name defined twice across
        // the merged set. Both are usage errors the caller reports and exits
        // on; the skip list is still populated, because what was already
        // dropped is reported alongside the error.
        public string Error { get; }

        internal static LoadedWorkspace Succeeded(
            TypeRegistry registry,
            IReadOnlyList<PouAst> pouTypes,
            IReadOnlyDictionary<string, string> filePathsByTypeName,
            IReadOnlyList<SkippedFile> skipped) =>
            new LoadedWorkspace(registry, pouTypes, filePathsByTypeName, skipped, null);

        internal static LoadedWorkspace Failed(string error, IReadOnlyList<SkippedFile> skipped) =>
            new LoadedWorkspace(null, Array.Empty<PouAst>(), new Dictionary<string, string>(), skipped, error);
    }
}
