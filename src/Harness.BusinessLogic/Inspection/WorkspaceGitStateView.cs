namespace Harness.BusinessLogic.Inspection;

public sealed record WorkspaceGitStateView(
    string Branch,
    string? HeadSha,
    IReadOnlyList<WorkspaceGitFileChangeView> Changes,
    string Diff,
    bool IsTruncated,
    string? ErrorCode,
    string? Error,
    string Fingerprint = "",
    string StagedDiff = "",
    string UnstagedDiff = "",
    IReadOnlyList<DeveloperGitPatchUnitView>? PatchUnits = null);
