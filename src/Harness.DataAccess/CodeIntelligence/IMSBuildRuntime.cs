namespace Harness.DataAccess.CodeIntelligence;

public interface IMSBuildRuntime
{
    ValueTask<MSBuildRuntimeResult> EnsureRegisteredAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default);
}
