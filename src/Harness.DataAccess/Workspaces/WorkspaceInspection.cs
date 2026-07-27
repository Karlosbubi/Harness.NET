namespace Harness.DataAccess.Workspaces;

public sealed record WorkspaceInspection(
    string RootPath,
    string Name,
    string Branch,
    bool IsDirty,
    IReadOnlyList<string> EntryPoints,
    string? Error);
