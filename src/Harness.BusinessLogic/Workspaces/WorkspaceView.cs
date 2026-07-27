namespace Harness.BusinessLogic.Workspaces;

public sealed record WorkspaceView(
    string Id,
    string RootPath,
    string Name,
    string EntryPoint,
    bool IsTrusted,
    bool IsActive,
    string Branch,
    bool IsDirty);
