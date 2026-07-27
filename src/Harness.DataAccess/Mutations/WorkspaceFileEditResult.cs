namespace Harness.DataAccess.Mutations;

public sealed record WorkspaceFileEditResult(
    string Path,
    string? PreviousSha256,
    string? NewSha256,
    int BytesWritten,
    bool WasCreated,
    string? ErrorCode,
    string? Error);
