namespace Harness.DataAccess.Mutations;

public sealed record WorkspaceFileEdit(
    string Path,
    string? ExpectedSha256,
    string Content);
