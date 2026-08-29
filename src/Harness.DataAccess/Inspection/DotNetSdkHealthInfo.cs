namespace Harness.DataAccess.Inspection;

public enum DotNetSdkHealthState
{
    Ready,
    Unavailable,
}

public sealed record DotNetSelectedSdkVersion(string Value);

public sealed record DotNetSdkHealthInfo(
    DotNetSdkHealthState State,
    DotNetSelectedSdkVersion? SelectedVersion,
    bool WorkloadManifestsAvailable,
    string? ErrorCode,
    string? Error);
