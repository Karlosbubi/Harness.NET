namespace Harness.DataAccess.Inspection;

public sealed record WorkspaceGitState(
    string Branch,
    string? HeadSha,
    IReadOnlyList<WorkspaceGitFileChange> Changes,
    string Diff,
    bool IsTruncated,
    string? ErrorCode,
    string? Error,
    string Fingerprint = "",
    string StagedDiff = "",
    string UnstagedDiff = "");
