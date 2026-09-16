using System;
using System.Collections.Generic;
using System.Linq;
using xStunit.Interpreter;

namespace xStunit.SystemLibraryPlugins.FileAccess
{
    // Non-zero nErrId values for the conditions the file blocks can hit.
    //
    // These are xStunit's OWN codes, not TwinCAT's. The vendor documents nErrId
    // as "ADS error code or command-specific error code" without publishing the
    // command-specific half for file access, so reproducing the real numbers is
    // not possible from outside. What is guaranteed here is what a suite can
    // legitimately depend on: zero means success, non-zero means failure, and
    // distinct causes get distinct codes.
    internal static class FileError
    {
        public const long None = 0;
        public const long NotFound = 1;
        public const long InvalidHandle = 2;
        public const long AlreadyExists = 3;
        public const long AccessDenied = 4;
        public const long DirectoryNotEmpty = 5;
        public const long PathNotFound = 6;
    }

    // The open-mode bits FB_FileOpen's nMode carries.
    //
    // FOPEN_MODEBINARY (16) and FOPEN_MODETEXT (32) are documented; the other
    // four are the same bit sequence continued downward, which is the only
    // arrangement consistent with those two and with the documented rule that
    // the pairs are mutually exclusive. Nothing here depends on the binary/text
    // distinction anyway - see VirtualFileSystem's note on line endings.
    [Flags]
    internal enum OpenMode
    {
        None = 0,
        Read = 0x01,
        Write = 0x02,
        Append = 0x04,
        Plus = 0x08,
        Binary = 0x10,
        Text = 0x20,
    }

    internal sealed class OpenFile
    {
        public OpenFile(string path, OpenMode mode)
        {
            Path = path;
            Mode = mode;
        }

        public string Path { get; }

        public OpenMode Mode { get; }

        public int Position { get; set; }

        public bool CanRead => (Mode & (OpenMode.Read | OpenMode.Plus)) != 0;

        public bool CanWrite => (Mode & (OpenMode.Write | OpenMode.Append | OpenMode.Plus)) != 0;
    }

    // The storage the file blocks act on: an in-memory tree, never real disk.
    //
    // Never disk for two reasons, not one. A suite that wrote to the machine's
    // filesystem would leave artefacts behind, race other runs, and behave
    // differently depending on what the running user may write - none of which
    // a test can control. And a suite has to be able to SEED what a POU is
    // about to read and INSPECT what it wrote, which a real path makes
    // needlessly awkward. F_FileSystem* (FileSystemFunctions.cs) is that
    // seed/inspect surface.
    //
    // Static for the same reason AdsLogSink is: FB_FileOpen and FB_FileRead are
    // separate declarations that must see one filesystem, and nothing travels
    // between them but the handle. The consequence is that state outlives a
    // suite, so a test that cares starts with F_FileSystemClear().
    //
    // NOT modelled: text-vs-binary line-ending translation. A file holds
    // exactly the bytes written to it, with no CR inserted or stripped, so a
    // POU whose behavior depends on that translation is outside what this can
    // test.
    internal static class VirtualFileSystem
    {
        // Paths compare case-insensitively with separators normalized, because
        // TwinCAT file paths are Windows paths and real source mixes the two
        // separators freely - a seed written with one and opened with the other
        // is the same file.
        private static readonly Dictionary<string, List<byte>> Files =
            new Dictionary<string, List<byte>>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> Directories =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<int, OpenFile> Handles = new Dictionary<int, OpenFile>();

        // Starts at 1 and never reuses a number within a run. 0 stays reserved
        // as "no handle": it is what an unset hFile holds, and a reused handle
        // would let a stale one silently address a different file.
        private static int _nextHandle = 1;

        public static string Normalize(string path) => (path ?? string.Empty).Replace('\\', '/').TrimEnd('/');

        public static void Clear()
        {
            Files.Clear();
            Directories.Clear();
            Handles.Clear();
            _nextHandle = 1;
        }

        public static int OpenHandleCount => Handles.Count;

        public static bool FileExists(string path) => Files.ContainsKey(Normalize(path));

        public static bool DirectoryExists(string path) => Directories.Contains(Normalize(path));

        public static int FileSize(string path) =>
            Files.TryGetValue(Normalize(path), out var content) ? content.Count : 0;

        public static byte[] ReadAll(string path)
        {
            var key = Normalize(path);
            if (!Files.TryGetValue(key, out var content))
                throw new CommandFailedException(FileError.NotFound, $"no file '{path}' in the virtual filesystem");

            return content.ToArray();
        }

        public static void WriteAll(string path, byte[] content) =>
            Files[Normalize(path)] = content.ToList();

        public static void MakeDirectory(string path)
        {
            var key = Normalize(path);
            if (Directories.Contains(key))
                throw new CommandFailedException(FileError.AlreadyExists, $"directory '{path}' already exists");

            Directories.Add(key);
        }

        public static void RemoveDirectory(string path)
        {
            var key = Normalize(path);
            if (!Directories.Contains(key))
                throw new CommandFailedException(FileError.PathNotFound, $"no directory '{path}'");

            // The vendor is explicit that a directory containing files cannot
            // be deleted, and a POU that assumes otherwise is exactly the kind
            // of defect worth catching here.
            var prefix = key + "/";
            if (Files.Keys.Any(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                throw new CommandFailedException(FileError.DirectoryNotEmpty, $"directory '{path}' still contains files");

            Directories.Remove(key);
        }

        public static void Delete(string path)
        {
            var key = Normalize(path);
            if (!Files.Remove(key))
                throw new CommandFailedException(FileError.NotFound, $"no file '{path}' to delete");
        }

        public static void Rename(string oldPath, string newPath)
        {
            var from = Normalize(oldPath);
            var to = Normalize(newPath);

            if (!Files.TryGetValue(from, out var content))
                throw new CommandFailedException(FileError.NotFound, $"no file '{oldPath}' to rename");

            if (Files.ContainsKey(to))
                throw new CommandFailedException(FileError.AlreadyExists, $"'{newPath}' already exists");

            Files.Remove(from);
            Files[to] = content;
        }

        public static int Open(string path, OpenMode mode)
        {
            var key = Normalize(path);

            if ((mode & OpenMode.Write) != 0)
            {
                // Write truncates, append does not, and read requires the file
                // to be there already - the three distinctions the mode bits
                // exist to make.
                Files[key] = new List<byte>();
            }
            else if ((mode & OpenMode.Append) != 0)
            {
                if (!Files.ContainsKey(key))
                    Files[key] = new List<byte>();
            }
            else if (!Files.ContainsKey(key))
            {
                throw new CommandFailedException(FileError.NotFound, $"no file '{path}' to open for reading");
            }

            var file = new OpenFile(key, mode);
            if ((mode & OpenMode.Append) != 0)
                file.Position = Files[key].Count;

            var handle = _nextHandle++;
            Handles[handle] = file;
            return handle;
        }

        public static void Close(int handle)
        {
            if (!Handles.Remove(handle))
                throw new CommandFailedException(FileError.InvalidHandle, $"file handle {handle} is not open");
        }

        public static byte[] Read(int handle, int count)
        {
            var file = Require(handle);
            if (!file.CanRead)
                throw new CommandFailedException(FileError.AccessDenied, $"file handle {handle} was not opened for reading");

            var content = Files[file.Path];
            var available = Math.Max(0, content.Count - file.Position);
            var take = Math.Min(count, available);

            var bytes = new byte[take];
            for (var i = 0; i < take; i++)
                bytes[i] = content[file.Position + i];

            file.Position += take;
            return bytes;
        }

        // Reads up to and INCLUDING the line feed, C fgets style, or to the end
        // of the file when the last line has no terminator.
        //
        // Whether sLine keeps that line feed is an assumption: the vendor doc
        // says the read runs "up to and including the line feed character" but
        // does not say what lands in sLine, and this has not been measured
        // against a real PLC. fgets semantics are the reading that makes the
        // sentence true, so that is what is reproduced.
        public static byte[] ReadLine(int handle)
        {
            var file = Require(handle);
            if (!file.CanRead)
                throw new CommandFailedException(FileError.AccessDenied, $"file handle {handle} was not opened for reading");

            var content = Files[file.Path];
            var line = new List<byte>();

            while (file.Position < content.Count)
            {
                var b = content[file.Position++];
                line.Add(b);
                if (b == (byte)'\n')
                    break;
            }

            return line.ToArray();
        }

        public static void Write(int handle, byte[] bytes)
        {
            var file = Require(handle);
            if (!file.CanWrite)
                throw new CommandFailedException(FileError.AccessDenied, $"file handle {handle} was not opened for writing");

            var content = Files[file.Path];

            // A write past the end extends the file; a write in the middle
            // overwrites in place. Both follow from the seek position, which is
            // why FB_FileSeek and FB_FileWrite have to share one notion of it.
            for (var i = 0; i < bytes.Length; i++)
            {
                if (file.Position < content.Count)
                    content[file.Position] = bytes[i];
                else
                    content.Add(bytes[i]);

                file.Position++;
            }
        }

        public static int Tell(int handle) => Require(handle).Position;

        public static bool EndOfFile(int handle)
        {
            var file = Require(handle);
            return file.Position >= Files[file.Path].Count;
        }

        // origin follows the C convention SEEK_SET/SEEK_CUR/SEEK_END = 0/1/2,
        // which E_SeekOrigin's own names say it mirrors. The enum's numeric
        // values are not published, so this is the documented-by-name mapping
        // rather than a measured one.
        public static void Seek(int handle, int position, int origin)
        {
            var file = Require(handle);
            var length = Files[file.Path].Count;

            var target = origin switch
            {
                0 => position,
                1 => file.Position + position,
                2 => length + position,
                _ => throw new CommandFailedException(FileError.AccessDenied, $"unknown seek origin {origin}"),
            };

            if (target < 0)
                throw new CommandFailedException(FileError.AccessDenied, "cannot seek before the start of the file");

            file.Position = target;
        }

        public static string ToText(byte[] bytes)
        {
            var text = new char[bytes.Length];
            for (var i = 0; i < bytes.Length; i++)
                text[i] = NarrowStringByte.ToChar(bytes[i]);

            return new string(text);
        }

        public static byte[] ToBytes(string text)
        {
            var bytes = new byte[text.Length];
            for (var i = 0; i < text.Length; i++)
                bytes[i] = NarrowStringByte.FromChar(text[i]);

            return bytes;
        }

        private static OpenFile Require(int handle)
        {
            if (!Handles.TryGetValue(handle, out var file))
                throw new CommandFailedException(FileError.InvalidHandle, $"file handle {handle} is not open");

            return file;
        }
    }
}
