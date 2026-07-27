namespace Harness.DataAccess.Execution;

public interface IDotNetToolRunner
{
    ValueTask<DotNetToolResult> RunAsync(
        string worktreeRoot,
        DotNetToolRequest request,
        CancellationToken cancellationToken = default);
}
