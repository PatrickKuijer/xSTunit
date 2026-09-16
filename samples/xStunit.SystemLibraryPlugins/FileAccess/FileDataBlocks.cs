using System;
using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Interpreter.Extensibility;

namespace xStunit.SystemLibraryPlugins.FileAccess
{
    // FB_FileRead is the block that made the whole stateful-FB plugin surface
    // necessary rather than convenient: it fills a buffer the CALLER owns,
    // reached through a POINTER, which the pure-function surface has no way to
    // write to.
    public sealed class FileReadBlock : FileAccessBlock
    {
        public override string TypeName => "FB_FileRead";

        public override IReadOnlyList<NativeFieldDeclaration> Fields => FieldsOf(
            CommonInputs,
            new[]
            {
                new NativeFieldDeclaration("hFile", 0),
                new NativeFieldDeclaration("pReadBuff", null),
                new NativeFieldDeclaration("cbReadLen", 0L),
                new NativeFieldDeclaration("cbRead", 0L),
                new NativeFieldDeclaration("bEOF", false),
            },
            CommonOutputs);

        public override IReadOnlyList<string> PositionalInputNames =>
            new[] { "sNetId", "hFile", "pReadBuff", "cbReadLen", "bExecute", "tTimeout" };

        public override IXstunitNativeFunctionBlock CreateInstance() => new FileReadBlock();

        protected override void Perform(NativeFunctionBlockCall call)
        {
            var requested = Convert.ToInt32(call.GetField("cbReadLen"));
            var bytes = VirtualFileSystem.Read(HandleOf(call), requested);

            if (bytes.Length > 0)
            {
                if (!(call.GetField("pReadBuff") is Pointer target))
                {
                    throw new CommandFailedException(
                        FileError.AccessDenied,
                        "FB_FileRead was given no pReadBuff to read into - pass ADR(buffer)");
                }

                call.WriteBytes(target, bytes);
            }

            call.SetField("cbRead", (long)bytes.Length);

            // The vendor rule, and the one worth reproducing exactly: bEOF is
            // raised only when nothing at all could be read. A read that
            // returned data and happened to land on the end of the file leaves
            // it FALSE, so a loop driven by bEOF does not drop its last record.
            call.SetField("bEOF", bytes.Length == 0);
        }

        protected override void OnFailed(NativeFunctionBlockCall call) => call.SetField("cbRead", 0L);
    }

    public sealed class FileWriteBlock : FileAccessBlock
    {
        public override string TypeName => "FB_FileWrite";

        public override IReadOnlyList<NativeFieldDeclaration> Fields => FieldsOf(
            CommonInputs,
            new[]
            {
                new NativeFieldDeclaration("hFile", 0),
                new NativeFieldDeclaration("pWriteBuff", null),
                new NativeFieldDeclaration("cbWriteLen", 0L),
                new NativeFieldDeclaration("cbWrite", 0L),
            },
            CommonOutputs);

        public override IReadOnlyList<string> PositionalInputNames =>
            new[] { "sNetId", "hFile", "pWriteBuff", "cbWriteLen", "bExecute", "tTimeout" };

        public override IXstunitNativeFunctionBlock CreateInstance() => new FileWriteBlock();

        protected override void Perform(NativeFunctionBlockCall call)
        {
            var length = Convert.ToInt32(call.GetField("cbWriteLen"));

            if (length > 0)
            {
                if (!(call.GetField("pWriteBuff") is Pointer source))
                {
                    throw new CommandFailedException(
                        FileError.AccessDenied,
                        "FB_FileWrite was given no pWriteBuff to write from - pass ADR(buffer)");
                }

                VirtualFileSystem.Write(HandleOf(call), call.ReadBytes(source, length));
            }
            else
            {
                // Still validates the handle: a zero-length write to a handle
                // that was never opened is a defect, and reporting success
                // would hide it until the next block.
                VirtualFileSystem.Write(HandleOf(call), Array.Empty<byte>());
            }

            call.SetField("cbWrite", (long)length);
        }

        protected override void OnFailed(NativeFunctionBlockCall call) => call.SetField("cbWrite", 0L);
    }

    // The text half of the family. sLine carries a STRING rather than a
    // pointer, so these need no buffer at all - which is why settings-list and
    // device-parameter readers reach for them.
    public sealed class FileGetsBlock : FileAccessBlock
    {
        public override string TypeName => "FB_FileGets";

        public override IReadOnlyList<NativeFieldDeclaration> Fields => FieldsOf(
            CommonInputs,
            new[]
            {
                new NativeFieldDeclaration("hFile", 0),
                new NativeFieldDeclaration("sLine", string.Empty, "STRING(255)"),
                new NativeFieldDeclaration("bEOF", false),
            },
            CommonOutputs);

        public override IReadOnlyList<string> PositionalInputNames =>
            new[] { "sNetId", "hFile", "bExecute", "tTimeout" };

        public override IXstunitNativeFunctionBlock CreateInstance() => new FileGetsBlock();

        protected override void Perform(NativeFunctionBlockCall call)
        {
            var line = VirtualFileSystem.ReadLine(HandleOf(call));

            call.SetField("sLine", NarrowText.FromBytes(line));

            // Same rule as FB_FileRead: end of file only when nothing came
            // back, so the final line of a file without a trailing newline is
            // still delivered.
            call.SetField("bEOF", line.Length == 0);
        }

        protected override void OnFailed(NativeFunctionBlockCall call) => call.SetField("sLine", string.Empty);
    }

    public sealed class FilePutsBlock : FileAccessBlock
    {
        public override string TypeName => "FB_FilePuts";

        public override IReadOnlyList<NativeFieldDeclaration> Fields => FieldsOf(
            CommonInputs,
            new[]
            {
                new NativeFieldDeclaration("hFile", 0),
                new NativeFieldDeclaration("sLine", string.Empty, "STRING(255)"),
            },
            CommonOutputs);

        public override IReadOnlyList<string> PositionalInputNames =>
            new[] { "sNetId", "hFile", "sLine", "bExecute", "tTimeout" };

        public override IXstunitNativeFunctionBlock CreateInstance() => new FilePutsBlock();

        protected override void Perform(NativeFunctionBlockCall call)
        {
            // "up to the null termination but without the null character" - so
            // the terminator is not written, and neither is a line feed the
            // caller did not put in sLine itself.
            var text = Convert.ToString(call.GetField("sLine")) ?? string.Empty;
            NarrowStringByte.RequireRepresentable(text);

            VirtualFileSystem.Write(HandleOf(call), NarrowText.ToBytes(text));
        }
    }
}
