namespace Harness.DataAccess.CodeIntelligence;

public interface IRoslynWorkspaceProbe
{
    ValueTask<RoslynWorkspaceProbeResult> ProbeAsync(
        string workspaceRoot,
        string entryPoint,
        CancellationToken cancellationToken = default);
}
