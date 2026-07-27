namespace Harness.DataAccess.Mutations;

public interface IWorkspaceFileEditor
{
    ValueTask<WorkspaceFileEditResult> ApplyAsync(
        string worktreeRoot,
        WorkspaceFileEdit edit,
        CancellationToken cancellationToken = default);
}
