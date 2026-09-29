using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using xStunit.Parser;

namespace xStunit.Interpreter
{
    // Turns the directories a run is pointed at into the type universe it
    // executes against: every .TcPOU, .TcDUT, .TcGVL and .TcIO under them,
    // merged into one TypeRegistry, plus the list of files that could not be
    // read.
    public static class WorkspaceLoader
    {
        private static readonly DeclarationWarning[] NoWarnings = new DeclarationWarning[0];

        public static LoadedWorkspace Load(IReadOnlyList<string> directories)
        {
            var skipped = new List<SkippedFile>();

            foreach (var path in directories)
            {
                if (!Directory.Exists(path))
                    return LoadedWorkspace.Failed($"path does not exist: {path}", skipped, NoWarnings);
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
            var warnings = new List<DeclarationWarning>();
            foreach (var file in MultiDirectoryPouLoader.FindPouFiles(directories))
            {
                try
                {
                    if (StructuralParseGuard.TryParseOrSkip(
                            file, () => TcPouParser.Parse(File.ReadAllText(file)), out var pou, out var skip))
                    {
                        loaded.Add(new LoadedPou(pou, file));
                        CollectDeclarationWarnings(pou, file, warnings);
                    }
                    else
                    {
                        skipped.Add(skip);
                    }
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
                return LoadedWorkspace.Failed(ex.Message, skipped, warnings);
            }

            var types = loaded.Select(l => l.Pou).ToList();

            IReadOnlyList<StructAst> structTypes;
            var dutSkipped = new List<SkippedFile>();
            var dutWarnings = new List<DeclarationWarning>();
            try
            {
                structTypes = DutStructLoader.Load(directories, out dutSkipped, out dutWarnings);
                skipped.AddRange(dutSkipped);
                warnings.AddRange(dutWarnings);
            }
            catch (DuplicateStructTypeException ex)
            {
                skipped.AddRange(dutSkipped);
                warnings.AddRange(dutWarnings);
                return LoadedWorkspace.Failed(ex.Message, skipped, warnings);
            }

            var aliases = DutAliasLoader.Load(directories, out var aliasSkipped)
                .ToDictionary(kv => kv.Key, kv => kv.Value, IecIdentifier.Comparer);
            skipped.AddRange(aliasSkipped);

            // Enum names are merged into the alias map so SIZEOF() and every
            // other ResolveAlias call site resolves an enum to its underlying
            // integer type without a second lookup path.
            var enumAliases = DutEnumLoader.Load(directories, out var enumSkipped, out var enumMembers);
            skipped.AddRange(enumSkipped);
            foreach (var enumAlias in enumAliases)
                aliases[enumAlias.Key] = enumAlias.Value;

            IReadOnlyList<GvlAst> gvls;
            var gvlSkipped = new List<SkippedFile>();
            var gvlWarnings = new List<DeclarationWarning>();
            IReadOnlyDictionary<string, string> gvlPaths;
            try
            {
                gvls = GvlLoader.Load(directories, out gvlSkipped, out gvlWarnings, out gvlPaths);
                skipped.AddRange(gvlSkipped);
                warnings.AddRange(gvlWarnings);
            }
            catch (DuplicateGvlNameException ex)
            {
                skipped.AddRange(gvlSkipped);
                warnings.AddRange(gvlWarnings);
                return LoadedWorkspace.Failed(ex.Message, skipped, warnings);
            }

            IReadOnlyList<InterfaceAst> interfaceTypes;
            try
            {
                interfaceTypes = InterfaceLoader.Load(directories, out var interfaceSkipped);
                skipped.AddRange(interfaceSkipped);
            }
            catch (DuplicateInterfaceTypeException ex)
            {
                return LoadedWorkspace.Failed(ex.Message, skipped, warnings);
            }

            return LoadedWorkspace.Succeeded(
                new TypeRegistry(types, structTypes, gvls, aliases, enumMembers, interfaceTypes),
                types,
                // The duplicate check above has already ruled out two names
                // this would merge.
                loaded.ToDictionary(l => l.Pou.Name, l => l.FilePath, IecIdentifier.Comparer),
                skipped,
                warnings,
                gvlPaths);
        }

        // Declarations are re-parsed here, at load time, purely to attribute
        // what they lose to a file. TypeRegistry parses the same text later and
        // caches it by the text itself, with no path and no POU identity to
        // hand - and by then the five Engine call sites that ask for it are
        // deep in a run. Parsing twice costs less than threading a file path
        // through all of that, and the parse is pure, so the second pass sees
        // exactly what the first did.
        private static void CollectDeclarationWarnings(PouAst pou, string file, List<DeclarationWarning> warnings)
        {
            var unread = new List<string>();
            CollectUnreadLines(pou.DeclarationText, unread);

            foreach (var method in pou.Methods)
                CollectUnreadLines(method.DeclarationText, unread);

            foreach (var property in pou.Properties)
                CollectUnreadLines(property.DeclarationText, unread);

            if (unread.Count > 0)
                warnings.Add(new DeclarationWarning(file, unread));
        }

        private static void CollectUnreadLines(string declarationText, List<string> unread)
        {
            if (string.IsNullOrEmpty(declarationText))
                return;

            VarBlockParser.Parse(declarationText, out var unreadable);
            unread.AddRange(unreadable);
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
            IReadOnlyList<DeclarationWarning> warnings,
            string error,
            IReadOnlyDictionary<string, string> gvlFilePathsByName)
        {
            Registry = registry;
            PouTypes = pouTypes;
            FilePathsByTypeName = filePathsByTypeName;
            Skipped = skipped;
            Warnings = warnings;
            GvlFilePathsByName = gvlFilePathsByName;
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

        // Files that loaded, but lost individual declaration lines on the way
        // in. Distinct from Skipped because the file's types are present and
        // its suites still run: what is missing is the variables those lines
        // declared, and every later use of one reports "Unknown variable"
        // pointing at the use rather than here.
        public IReadOnlyList<DeclarationWarning> Warnings { get; }

        public IReadOnlyDictionary<string, string> GvlFilePathsByName { get; }

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
            IReadOnlyList<SkippedFile> skipped,
            IReadOnlyList<DeclarationWarning> warnings,
            IReadOnlyDictionary<string, string> gvlFilePathsByName) =>
            new LoadedWorkspace(registry, pouTypes, filePathsByTypeName, skipped, warnings, null, gvlFilePathsByName);

        // A load that failed outright still reports what it had already lost,
        // the same way it still reports what it had already skipped. Both lists
        // are required rather than defaulted, so a new error path cannot drop
        // either by saying nothing about it.
        internal static LoadedWorkspace Failed(
            string error,
            IReadOnlyList<SkippedFile> skipped,
            IReadOnlyList<DeclarationWarning> warnings) =>
            new LoadedWorkspace(
                null,
                Array.Empty<PouAst>(),
                new Dictionary<string, string>(IecIdentifier.Comparer),
                skipped,
                warnings,
                error,
                new Dictionary<string, string>(IecIdentifier.Comparer));
    }
}
