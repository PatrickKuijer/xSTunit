using System.Collections.Generic;
using xStunit.Interpreter.Extensibility;

namespace xStunit.SystemLibraryPlugins.FileAccess
{
    // The blocks that name a path rather than hold a handle. All four share one
    // shape - path in, nothing but the handshake out - so only the operation
    // differs.
    //
    // ePath selects which TwinCAT system directory a relative path is resolved
    // against (PATH_GENERIC and friends). The virtual filesystem has a single
    // flat namespace, so it is accepted and ignored: modelling the directory
    // layout of a machine no test runs on would make paths depend on a value
    // nothing here can vary meaningfully.
    public sealed class FileDeleteBlock : FileAccessBlock
    {
        public override string TypeName => "FB_FileDelete";

        public override IReadOnlyList<NativeFieldDeclaration> Fields => FieldsOf(
            CommonInputs,
            new[]
            {
                new NativeFieldDeclaration("sPathName", string.Empty, "STRING(255)"),
                new NativeFieldDeclaration("ePath", 0),
            },
            CommonOutputs);

        public override IReadOnlyList<string> PositionalInputNames =>
            new[] { "sNetId", "sPathName", "ePath", "bExecute", "tTimeout" };

        public override IXstunitNativeFunctionBlock CreateInstance() => new FileDeleteBlock();

        protected override void Perform(NativeFunctionBlockCall call) =>
            VirtualFileSystem.Delete(PathOf(call, "sPathName"));
    }

    public sealed class FileRenameBlock : FileAccessBlock
    {
        public override string TypeName => "FB_FileRename";

        public override IReadOnlyList<NativeFieldDeclaration> Fields => FieldsOf(
            CommonInputs,
            new[]
            {
                new NativeFieldDeclaration("sOldName", string.Empty, "STRING(255)"),
                new NativeFieldDeclaration("sNewName", string.Empty, "STRING(255)"),
                new NativeFieldDeclaration("ePath", 0),
            },
            CommonOutputs);

        public override IReadOnlyList<string> PositionalInputNames =>
            new[] { "sNetId", "sOldName", "sNewName", "ePath", "bExecute", "tTimeout" };

        public override IXstunitNativeFunctionBlock CreateInstance() => new FileRenameBlock();

        protected override void Perform(NativeFunctionBlockCall call) =>
            VirtualFileSystem.Rename(PathOf(call, "sOldName"), PathOf(call, "sNewName"));
    }

    public sealed class CreateDirBlock : FileAccessBlock
    {
        public override string TypeName => "FB_CreateDir";

        public override IReadOnlyList<NativeFieldDeclaration> Fields => FieldsOf(
            CommonInputs,
            new[]
            {
                new NativeFieldDeclaration("sPathName", string.Empty, "STRING(255)"),
                new NativeFieldDeclaration("ePath", 0),
            },
            CommonOutputs);

        public override IReadOnlyList<string> PositionalInputNames =>
            new[] { "sNetId", "sPathName", "ePath", "bExecute", "tTimeout" };

        public override IXstunitNativeFunctionBlock CreateInstance() => new CreateDirBlock();

        protected override void Perform(NativeFunctionBlockCall call) =>
            VirtualFileSystem.MakeDirectory(PathOf(call, "sPathName"));
    }

    public sealed class RemoveDirBlock : FileAccessBlock
    {
        public override string TypeName => "FB_RemoveDir";

        public override IReadOnlyList<NativeFieldDeclaration> Fields => FieldsOf(
            CommonInputs,
            new[]
            {
                new NativeFieldDeclaration("sPathName", string.Empty, "STRING(255)"),
                new NativeFieldDeclaration("ePath", 0),
            },
            CommonOutputs);

        public override IReadOnlyList<string> PositionalInputNames =>
            new[] { "sNetId", "sPathName", "ePath", "bExecute", "tTimeout" };

        public override IXstunitNativeFunctionBlock CreateInstance() => new RemoveDirBlock();

        protected override void Perform(NativeFunctionBlockCall call) =>
            VirtualFileSystem.RemoveDirectory(PathOf(call, "sPathName"));
    }
}
