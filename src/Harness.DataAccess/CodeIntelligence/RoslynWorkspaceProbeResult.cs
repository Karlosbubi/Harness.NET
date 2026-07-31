namespace Harness.DataAccess.CodeIntelligence;

public enum RoslynWorkspaceProbeState
{
    Ready,
    Degraded,
    Failed,
}

public sealed record RoslynWorkspaceProbeIssue(
    string Code,
    string Message);

public sealed record RoslynWorkspaceProbeResult(
    RoslynWorkspaceProbeState State,
    DotNetSdkVersion? SdkVersion,
    int ProjectCount,
    int DocumentCount,
    IReadOnlyList<RoslynWorkspaceProbeIssue> Issues);
