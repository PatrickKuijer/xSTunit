using System;

namespace TcXunitResultsSpike
{
    internal static class PackageGuids
    {
        public const string PackageGuidString = "78a251f2-b67d-4fa5-a694-47748a870679";
        public const string CommandSetGuidString = "9e35360d-54dd-45d6-b49e-c0a465f43e89";

        public static readonly Guid CommandSet = new Guid(CommandSetGuidString);
    }

    internal static class PackageCommandIds
    {
        public const int ShowResultsToolWindowCommandId = 0x0100;
    }
}
