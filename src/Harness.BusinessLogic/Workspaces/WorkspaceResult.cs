namespace Harness.BusinessLogic.Workspaces;

public sealed record WorkspaceResult(
    WorkspaceView? Workspace,
    IReadOnlyList<string> EntryPoints,
    string? Error);
