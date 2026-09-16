using System;
using System.Collections.Generic;
using xStunit.Interpreter.Extensibility;

namespace xStunit.SystemLibraryPlugins.Ads
{
    // FUNCTION_BLOCK ADSREAD: reads LEN bytes from the device into DESTADDR.
    public sealed class AdsReadBlock : AdsRequestBlock
    {
        public override string TypeName => "ADSREAD";

        public override IReadOnlyList<NativeFieldDeclaration> Fields => FieldsOf(
            CommonInputs,
            new[]
            {
                new NativeFieldDeclaration("IDXGRP", 0L),
                new NativeFieldDeclaration("IDXOFFS", 0L),
                new NativeFieldDeclaration("LEN", 0L),
                new NativeFieldDeclaration("DESTADDR", null),
                new NativeFieldDeclaration("READ", false),
            },
            CommonOutputs);

        public override IReadOnlyList<string> PositionalInputNames =>
            new[] { "NETID", "PORT", "IDXGRP", "IDXOFFS", "LEN", "DESTADDR", "READ", "TMOUT" };

        public override IXstunitNativeFunctionBlock CreateInstance() => new AdsReadBlock();

        protected override string TriggerFieldName => "READ";

        protected override (long Group, long Offset) AddressOf(NativeFunctionBlockCall call) =>
            (FieldAsInt64(call, "IDXGRP"), FieldAsInt64(call, "IDXOFFS"));

        protected override void Serve(NativeFunctionBlockCall call)
        {
            var (group, offset) = AddressOf(call);
            PublishInto(call, "DESTADDR", (int)FieldAsInt64(call, "LEN"), AdsServer.Read(group, offset));
        }
    }

    // FUNCTION_BLOCK ADSWRITE: sends LEN bytes from SRCADDR to the device.
    public sealed class AdsWriteBlock : AdsRequestBlock
    {
        public override string TypeName => "ADSWRITE";

        public override IReadOnlyList<NativeFieldDeclaration> Fields => FieldsOf(
            CommonInputs,
            new[]
            {
                new NativeFieldDeclaration("IDXGRP", 0L),
                new NativeFieldDeclaration("IDXOFFS", 0L),
                new NativeFieldDeclaration("LEN", 0L),
                new NativeFieldDeclaration("SRCADDR", null),
                new NativeFieldDeclaration("WRITE", false),
            },
            CommonOutputs);

        public override IReadOnlyList<string> PositionalInputNames =>
            new[] { "NETID", "PORT", "IDXGRP", "IDXOFFS", "LEN", "SRCADDR", "WRITE", "TMOUT" };

        public override IXstunitNativeFunctionBlock CreateInstance() => new AdsWriteBlock();

        protected override string TriggerFieldName => "WRITE";

        protected override (long Group, long Offset) AddressOf(NativeFunctionBlockCall call) =>
            (FieldAsInt64(call, "IDXGRP"), FieldAsInt64(call, "IDXOFFS"));

        protected override void Serve(NativeFunctionBlockCall call)
        {
            var (group, offset) = AddressOf(call);
            var length = (int)FieldAsInt64(call, "LEN");

            AdsServer.Write(group, offset, BufferOf(call, "SRCADDR", length));
        }
    }

    // FUNCTION_BLOCK ADSRDWRT: one round trip that writes and then reads. The
    // write lands first and the read sees it, which is what makes this
    // different from an ADSWRITE followed by an ADSREAD - a device that
    // answers from what it was just given.
    public sealed class AdsReadWriteBlock : AdsRequestBlock
    {
        public override string TypeName => "ADSRDWRT";

        public override IReadOnlyList<NativeFieldDeclaration> Fields => FieldsOf(
            CommonInputs,
            new[]
            {
                new NativeFieldDeclaration("IDXGRP", 0L),
                new NativeFieldDeclaration("IDXOFFS", 0L),
                new NativeFieldDeclaration("WRITELEN", 0L),
                new NativeFieldDeclaration("READLEN", 0L),
                new NativeFieldDeclaration("SRCADDR", null),
                new NativeFieldDeclaration("DESTADDR", null),
                new NativeFieldDeclaration("WRTRD", false),
            },
            CommonOutputs);

        public override IReadOnlyList<string> PositionalInputNames =>
            new[]
            {
                "NETID", "PORT", "IDXGRP", "IDXOFFS", "WRITELEN", "READLEN",
                "SRCADDR", "DESTADDR", "WRTRD", "TMOUT",
            };

        public override IXstunitNativeFunctionBlock CreateInstance() => new AdsReadWriteBlock();

        protected override string TriggerFieldName => "WRTRD";

        protected override (long Group, long Offset) AddressOf(NativeFunctionBlockCall call) =>
            (FieldAsInt64(call, "IDXGRP"), FieldAsInt64(call, "IDXOFFS"));

        protected override void Serve(NativeFunctionBlockCall call)
        {
            var (group, offset) = AddressOf(call);
            var writeLength = (int)FieldAsInt64(call, "WRITELEN");

            if (writeLength > 0)
                AdsServer.Write(group, offset, BufferOf(call, "SRCADDR", writeLength));

            PublishInto(call, "DESTADDR", (int)FieldAsInt64(call, "READLEN"), AdsServer.Read(group, offset));
        }
    }

    // FUNCTION_BLOCK ADSRDSTATE: asks the device how it is, rather than for
    // any variable it holds. It names no index group or offset, so its
    // scripted failure and timeout are addressed at the reserved
    // state-request key - one scripting mechanism rather than a second one
    // existing solely for this block.
    public sealed class AdsReadStateBlock : AdsRequestBlock
    {
        public override string TypeName => "ADSRDSTATE";

        public override IReadOnlyList<NativeFieldDeclaration> Fields => FieldsOf(
            CommonInputs,
            new[]
            {
                new NativeFieldDeclaration("RDSTATE", false),
                new NativeFieldDeclaration("ADSSTATE", 0),
                new NativeFieldDeclaration("DEVSTATE", 0),
            },
            CommonOutputs);

        public override IReadOnlyList<string> PositionalInputNames =>
            new[] { "NETID", "PORT", "RDSTATE", "TMOUT" };

        public override IXstunitNativeFunctionBlock CreateInstance() => new AdsReadStateBlock();

        protected override string TriggerFieldName => "RDSTATE";

        protected override (long Group, long Offset) AddressOf(NativeFunctionBlockCall call) =>
            AdsServer.StateRequestKey;

        protected override void Serve(NativeFunctionBlockCall call)
        {
            call.SetField("ADSSTATE", AdsServer.AdsState);
            call.SetField("DEVSTATE", AdsServer.DeviceState);
        }
    }
}
