using System.Runtime.InteropServices;

namespace Harness.DataAccess.Debugging;

internal enum DebugAdapterArchiveKind
{
    TarGzip,
    Zip,
}

internal sealed record DebugAdapterPayload(
    string Name,
    long Bytes,
    string Sha256,
    bool IsExecutable = false);

internal sealed record DebugAdapterPackageDefinition(
    string RuntimeIdentifier,
    Uri ArchiveUri,
    string ArchiveSha256,
    DebugAdapterArchiveKind ArchiveKind,
    string ExecutableName,
    IReadOnlyList<DebugAdapterPayload> Payloads);

internal static class DebugAdapterPackageCatalog
{
    internal const string Version = "3.2.0-1092";
    internal const string LicenseSha256 =
        "6cd03b0de8299b0800f22b35ae842c931ded7684a2d1ba4f1d4188bab9b09a11";
    internal static readonly Uri LicenseUri = new(
        "https://raw.githubusercontent.com/Samsung/netcoredbg/3.2.0-1092/LICENSE");

    private static readonly DebugAdapterPayload[] CommonPayloads =
    [
        new("Microsoft.CodeAnalysis.CSharp.Scripting.dll", 29_728,
            "64050c89e7bf7905f6c7ec9eb1594dcea5fbbd2868341f9ef99b58e9dea01605"),
        new("Microsoft.CodeAnalysis.CSharp.dll", 4_357_136,
            "e9cc57c55a95146f98b55dd46fdd881b4be48925e3773ef4aa5ac622b76903a6"),
        new("Microsoft.CodeAnalysis.Scripting.dll", 129_552,
            "066f1d39b5e211e343c8c79f374438884b1684fa27dc8c29a9000f88615bf764"),
        new("Microsoft.CodeAnalysis.dll", 1_993_728,
            "97553a05b1f8aad2f29349f0fc7893ceb6dd199bd769d3e1092839957c7c0cdb"),
    ];

    private static readonly IReadOnlyDictionary<string, DebugAdapterPackageDefinition> Packages =
        new Dictionary<string, DebugAdapterPackageDefinition>(StringComparer.Ordinal)
        {
            ["linux-x64"] = Package(
                "linux-x64", "netcoredbg-linux-amd64.tar.gz",
                "080eb3b2d2152465f599d3b33d1ee6e747794e11cc0a3773ec689f5e5f2c5afa",
                DebugAdapterArchiveKind.TarGzip, "netcoredbg",
                new("ManagedPart.dll", 54_784,
                    "2652aa2bf306b090b7d53374ad30b922d61c48d3a978d7ba280a6e2cb4797596"),
                new("libdbgshim.so", 311_032,
                    "4f45a9caad30619b36e083711d2e94817454ba976f46bcc229fbc93f24242990"),
                new("netcoredbg", 2_602_320,
                    "03dbc5dd30471ff5648a973e9b1542fea6aca345fd913292159904c44aaf8c1a", true)),
            ["linux-arm64"] = Package(
                "linux-arm64", "netcoredbg-linux-arm64.tar.gz",
                "065ff49badec8a695dbea2de6ab6a330c774a191e426a217ab8cc05250627ccb",
                DebugAdapterArchiveKind.TarGzip, "netcoredbg",
                new("ManagedPart.dll", 54_784,
                    "7cde88a436db711be6158e2f7fbf1b055f395baf73e85cc2cc5db9ee72105d47"),
                new("libdbgshim.so", 313_136,
                    "d5a795efac5db08c9a7485a33a03ec40335caa71e239f35557faebcbbb0a2b15"),
                new("netcoredbg", 2_403_976,
                    "d2ea6a92951c1e7db6554568000c43017f4e5328cbb1157e92d7e9fef7ae198e", true)),
            ["osx-arm64"] = Package(
                "osx-arm64", "netcoredbg-osx-arm64.zip",
                "f4fa33b3ff874910cc184b4bb3b9c56d0abdf5c6521cee0b144d7c6e4a6e59ea",
                DebugAdapterArchiveKind.Zip, "netcoredbg",
                new("ManagedPart.dll", 54_784,
                    "b25ba5bf108e9101192d00a039118e6ad424f39c703f4ea1d4ddd05cff498ca5"),
                new("libdbgshim.dylib", 396_624,
                    "452a9778afe10f54189f9d6816e1e8250d6195a0e664b10d838f33cd8794abcb"),
                new("netcoredbg", 2_625_896,
                    "ffc51b6f716e6bb8cb93c416e352651b945269c9501f820d32f3023d4ce24ece", true)),
            ["win-x64"] = Package(
                "win-x64", "netcoredbg-win64.zip",
                "3c410a45fa502415203a94fcb88654af65bf8e3dac158a5527a722e7a6b9274a",
                DebugAdapterArchiveKind.Zip, "netcoredbg.exe",
                new("ManagedPart.dll", 54_272,
                    "bf6c824357cded65cc6dc0a2467104a835b1f79274e742c0655c263bc906efee"),
                new("dbgshim.dll", 143_392,
                    "4b5d67e6325322e863e5edee33d2f94b83ef1178885824b65c57f7917d4970f3"),
                new("netcoredbg.exe", 2_115_072,
                    "23fc802866a03c5aa02484937d61ecbccf0ca731718d1840b0871f153340a823")),
        };

    internal static string CurrentRuntimeIdentifier =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? Architecture("linux")
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? Architecture("osx")
                : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? Architecture("win")
                    : $"unsupported-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";

    internal static bool TryGetCurrent(out DebugAdapterPackageDefinition? package) =>
        Packages.TryGetValue(CurrentRuntimeIdentifier, out package);

    private static string Architecture(string os) =>
        RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => $"{os}-x64",
            System.Runtime.InteropServices.Architecture.Arm64 => $"{os}-arm64",
            _ => $"{os}-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}",
        };

    private static DebugAdapterPackageDefinition Package(
        string runtimeIdentifier,
        string assetName,
        string archiveSha256,
        DebugAdapterArchiveKind archiveKind,
        string executableName,
        params DebugAdapterPayload[] platformPayloads) =>
        new(
            runtimeIdentifier,
            new($"https://github.com/Samsung/netcoredbg/releases/download/{Version}/{assetName}"),
            archiveSha256,
            archiveKind,
            executableName,
            [.. CommonPayloads, .. platformPayloads]);
}
