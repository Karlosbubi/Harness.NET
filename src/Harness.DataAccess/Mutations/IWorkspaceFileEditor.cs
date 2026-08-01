namespace Harness.DataAccess.Mutations;

public interface IWorkspaceFileEditor
{
    ValueTask<WorkspaceFileEditResult> ApplyAsync(
        string worktreeRoot,
        WorkspaceFileEdit edit,
        CancellationToken cancellationToken = default);

    ValueTask<WorkspaceFileBatchEditResult> ApplyBatchAsync(
        string worktreeRoot,
        WorkspaceFileBatchEdit batch,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new WorkspaceFileBatchEditResult(
            [],
            WasRolledBack: false,
            WasCancelled: false,
            "batch_not_supported",
            "This file editor does not support atomic batches."));
}
