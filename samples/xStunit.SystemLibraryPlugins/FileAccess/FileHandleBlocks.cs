using System;
using System.Collections.Generic;
using xStunit.Interpreter.Extensibility;

namespace xStunit.SystemLibraryPlugins.FileAccess
{
    // FB_FileOpen: the only block that mints a handle, and so the one whose
    // contract the rest depend on. hFile stays 0 on failure - 0 is what an
    // unset hFile already holds, so a POU that skipped its bError check and
    // used the handle anyway fails at the next block rather than silently
    // addressing a file.
    public sealed class FileOpenBlock : FileAccessBlock
    {
        public override string TypeName => "FB_FileOpen";

        public override IReadOnlyList<NativeFieldDeclaration> Fields => FieldsOf(
            CommonInputs,
            new[]
            {
                new NativeFieldDeclaration("sPathName", string.Empty, "STRING(255)"),
                new NativeFieldDeclaration("nMode", 0L),
                new NativeFieldDeclaration("ePath", 0),
                new NativeFieldDeclaration("hFile", 0),
            },
            CommonOutputs);

        public override IReadOnlyList<string> PositionalInputNames =>
            new[] { "sNetId", "sPathName", "nMode", "ePath", "bExecute", "tTimeout" };

        public override IXstunitNativeFunctionBlock CreateInstance() => new FileOpenBlock();

        protected override void Execute(NativeFunctionBlockCall call)
        {
            var mode = (OpenMode)Convert.ToInt64(call.GetField("nMode"));
            call.SetField("hFile", VirtualFileSystem.Open(PathOf(call, "sPathName"), mode));
        }

        protected override void OnFailed(NativeFunctionBlockCall call) => call.SetField("hFile", 0);
    }

    public sealed class FileCloseBlock : FileAccessBlock
    {
        public override string TypeName => "FB_FileClose";

        public override IReadOnlyList<NativeFieldDeclaration> Fields => FieldsOf(
            CommonInputs,
            new[] { new NativeFieldDeclaration("hFile", 0) },
            CommonOutputs);

        public override IReadOnlyList<string> PositionalInputNames =>
            new[] { "sNetId", "hFile", "bExecute", "tTimeout" };

        public override IXstunitNativeFunctionBlock CreateInstance() => new FileCloseBlock();

        protected override void Execute(NativeFunctionBlockCall call) =>
            VirtualFileSystem.Close(HandleOf(call));
    }

    public sealed class FileSeekBlock : FileAccessBlock
    {
        public override string TypeName => "FB_FileSeek";

        public override IReadOnlyList<NativeFieldDeclaration> Fields => FieldsOf(
            CommonInputs,
            new[]
            {
                new NativeFieldDeclaration("hFile", 0),
                new NativeFieldDeclaration("nSeekPos", 0),
                new NativeFieldDeclaration("eOrigin", 0),
            },
            CommonOutputs);

        public override IReadOnlyList<string> PositionalInputNames =>
            new[] { "sNetId", "hFile", "nSeekPos", "eOrigin", "bExecute", "tTimeout" };

        public override IXstunitNativeFunctionBlock CreateInstance() => new FileSeekBlock();

        protected override void Execute(NativeFunctionBlockCall call) =>
            VirtualFileSystem.Seek(
                HandleOf(call),
                Convert.ToInt32(call.GetField("nSeekPos")),
                Convert.ToInt32(call.GetField("eOrigin")));
    }

    // nSeekPos reads -1 after a failure, which the vendor documents. Without
    // OnFailed it would hold whatever the last successful call left there, and
    // a POU checking the position instead of bError would read a plausible
    // offset for a file it never opened.
    public sealed class FileTellBlock : FileAccessBlock
    {
        public override string TypeName => "FB_FileTell";

        public override IReadOnlyList<NativeFieldDeclaration> Fields => FieldsOf(
            CommonInputs,
            new[]
            {
                new NativeFieldDeclaration("hFile", 0),
                new NativeFieldDeclaration("nSeekPos", 0),
            },
            CommonOutputs);

        public override IReadOnlyList<string> PositionalInputNames =>
            new[] { "sNetId", "hFile", "bExecute", "tTimeout" };

        public override IXstunitNativeFunctionBlock CreateInstance() => new FileTellBlock();

        protected override void Execute(NativeFunctionBlockCall call) =>
            call.SetField("nSeekPos", VirtualFileSystem.Tell(HandleOf(call)));

        protected override void OnFailed(NativeFunctionBlockCall call) => call.SetField("nSeekPos", -1);
    }

    // FB_EOF, not FB_FileEOF: the vendor's own name for it.
    public sealed class EndOfFileBlock : FileAccessBlock
    {
        public override string TypeName => "FB_EOF";

        public override IReadOnlyList<NativeFieldDeclaration> Fields => FieldsOf(
            CommonInputs,
            new[]
            {
                new NativeFieldDeclaration("hFile", 0),
                new NativeFieldDeclaration("bEOF", false),
            },
            CommonOutputs);

        public override IReadOnlyList<string> PositionalInputNames =>
            new[] { "sNetId", "hFile", "bExecute", "tTimeout" };

        public override IXstunitNativeFunctionBlock CreateInstance() => new EndOfFileBlock();

        protected override void Execute(NativeFunctionBlockCall call) =>
            call.SetField("bEOF", VirtualFileSystem.EndOfFile(HandleOf(call)));
    }
}
