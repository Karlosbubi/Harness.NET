namespace Harness.BusinessLogic.Inspection;

public sealed record WorkspaceGitFileChangeView(
    string Path,
    string Status,
    string IndexStatus = "Unmodified",
    string WorktreeStatus = "Unmodified",
    bool IsStaged = false,
    bool IsUnstaged = false,
    bool IsConflicted = false);
