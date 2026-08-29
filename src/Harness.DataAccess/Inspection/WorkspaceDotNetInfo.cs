namespace Harness.DataAccess.Inspection;

public sealed record WorkspaceDotNetInfo(
    string EntryPoint,
    string EntryPointKind,
    DotNetSdkPolicy? SdkPolicy,
    IReadOnlyList<DotNetProjectInfo> Projects,
    bool IsTruncated,
    string? ErrorCode,
    string? Error,
    DotNetSdkHealthInfo? SdkHealth = null);
