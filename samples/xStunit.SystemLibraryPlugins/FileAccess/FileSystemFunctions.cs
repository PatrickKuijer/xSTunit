using xStunit.Interpreter.Extensibility;

namespace xStunit.SystemLibraryPlugins.FileAccess
{
    // The seed/inspect surface for the virtual filesystem.
    //
    // These are xStunit's OWN functions, not Tc2_System symbols. There is no
    // vendor equivalent because on a real system a suite cannot reach into the
    // machine's filesystem from ST - which is exactly why testing a
    // file-touching POU is hard enough that most projects give up on it. A
    // test needs to state what the POU is about to read and check what it
    // wrote, and that has to come from somewhere.
    //
    // Text in and text out: the file blocks themselves deal in bytes, but a
    // suite seeding a settings list or checking a trace line is writing
    // characters, and going through a BYTE array for it would bury the
    // assertion.
    public sealed class FileSystemClearFunction : IXstunitNativeFunction
    {
        public string Name => "F_FileSystemClear";

        public object Invoke(NativeCallContext context)
        {
            // Open handles go too. The filesystem is process-wide, so a suite
            // that left one open must not leak it into the next.
            VirtualFileSystem.Clear();
            return 0;
        }
    }

    public sealed class FileSystemPutTextFunction : IXstunitNativeFunction
    {
        public string Name => "F_FileSystemPutText";

        public object Invoke(NativeCallContext context)
        {
            VirtualFileSystem.WriteAll(
                context.RequireString("sPath", 0),
                VirtualFileSystem.ToBytes(context.RequireString("sText", 1)));

            return 0;
        }
    }

    public sealed class FileSystemGetTextFunction : IXstunitNativeFunction
    {
        public string Name => "F_FileSystemGetText";

        public object Invoke(NativeCallContext context) =>
            VirtualFileSystem.ToText(VirtualFileSystem.ReadAll(context.RequireString("sPath", 0)));
    }

    public sealed class FileSystemExistsFunction : IXstunitNativeFunction
    {
        public string Name => "F_FileSystemExists";

        public object Invoke(NativeCallContext context) =>
            VirtualFileSystem.FileExists(context.RequireString("sPath", 0));
    }

    public sealed class FileSystemDirExistsFunction : IXstunitNativeFunction
    {
        public string Name => "F_FileSystemDirExists";

        public object Invoke(NativeCallContext context) =>
            VirtualFileSystem.DirectoryExists(context.RequireString("sPath", 0));
    }

    public sealed class FileSystemSizeFunction : IXstunitNativeFunction
    {
        public string Name => "F_FileSystemSize";

        public object Invoke(NativeCallContext context) =>
            (long)VirtualFileSystem.FileSize(context.RequireString("sPath", 0));
    }

    // Exists so a test can assert that a POU closed every handle it opened -
    // a leak no other output reveals, and one that only shows up on a real
    // system after a long run.
    public sealed class FileSystemOpenHandleCountFunction : IXstunitNativeFunction
    {
        public string Name => "F_FileSystemOpenHandleCount";

        public object Invoke(NativeCallContext context) => (long)VirtualFileSystem.OpenHandleCount;
    }
}
