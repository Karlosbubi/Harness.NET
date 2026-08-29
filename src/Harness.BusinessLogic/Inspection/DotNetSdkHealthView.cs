namespace Harness.BusinessLogic.Inspection;

public enum DotNetSdkHealthStateView
{
    Ready,
    Unavailable,
}

public sealed record DotNetSelectedSdkVersionView(string Value);

public sealed record DotNetSdkHealthView(
    DotNetSdkHealthStateView State,
    DotNetSelectedSdkVersionView? SelectedVersion,
    bool WorkloadManifestsAvailable,
    string? ErrorCode,
    string? Error);
