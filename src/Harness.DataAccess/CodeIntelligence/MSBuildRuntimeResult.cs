namespace Harness.DataAccess.CodeIntelligence;

public sealed record MSBuildRuntimeResult(
    MSBuildRuntimeState State,
    DotNetSdkVersion? SdkVersion,
    DotNetSdkPath? SdkPath,
    string? ErrorCode,
    string? Error);
