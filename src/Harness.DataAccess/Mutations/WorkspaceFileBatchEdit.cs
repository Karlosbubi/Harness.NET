namespace Harness.DataAccess.Mutations;

public sealed record WorkspaceFileBatchEdit(
    IReadOnlyList<WorkspaceFileEdit> Edits);
