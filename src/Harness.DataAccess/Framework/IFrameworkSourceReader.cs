namespace Harness.DataAccess.Framework;

public interface IFrameworkSourceReader
{
    ValueTask<FrameworkSourceResult> ReadAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default);
}
