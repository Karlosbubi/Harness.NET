namespace Harness.DataAccess.Inspection;

public sealed record WorkspaceGitFileChange(
    string Path,
    string Status,
    string IndexStatus = "Unmodified",
    string WorktreeStatus = "Unmodified",
    bool IsStaged = false,
    bool IsUnstaged = false,
    bool IsConflicted = false);
