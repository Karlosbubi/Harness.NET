namespace Harness.DataAccess.Mutations;

public sealed record WorkspaceFileBatchEditResult(
    IReadOnlyList<WorkspaceFileEditResult> Files,
    bool WasRolledBack,
    bool WasCancelled,
    string? ErrorCode,
    string? Error);
