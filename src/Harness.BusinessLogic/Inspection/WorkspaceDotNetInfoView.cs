namespace Harness.BusinessLogic.Inspection;

public sealed record WorkspaceDotNetInfoView(
    string EntryPoint,
    string EntryPointKind,
    DotNetSdkPolicyView? SdkPolicy,
    IReadOnlyList<DotNetProjectView> Projects,
    bool IsTruncated,
    string? ErrorCode,
    string? Error,
    DotNetSdkHealthView? SdkHealth = null);
