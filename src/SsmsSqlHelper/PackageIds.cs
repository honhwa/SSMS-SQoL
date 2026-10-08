using System;

namespace SsmsSqlHelper
{
    // Must stay in sync with VSCommandTable.vsct
    internal static class PackageIds
    {
        public const string PackageGuidString = "a4c7e2b1-3d9f-4e6a-b8c5-7f1e0d2a9b34";
        public static readonly Guid CommandSet = new Guid("c2e9a7d4-6b1f-4a3e-9d8c-5e0b7f2a1c68");

        public const int ShowConnectionCommandId = 0x0100;
        public const int EditSnippetsCommandId = 0x0200;
        public const int SurroundWithSnippetCommandId = 0x0220;
        public const int RecoverQueriesCommandId = 0x0230;
        public const int RefreshMetadataCommandId = 0x0150;
    }
}
