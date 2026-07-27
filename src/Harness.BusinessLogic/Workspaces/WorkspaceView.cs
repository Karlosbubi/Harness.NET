namespace Harness.BusinessLogic.Workspaces;

public sealed record WorkspaceView(
    string Id,
    string RootPath,
    string Name,
    string EntryPoint,
    bool IsTrusted,
    string Branch,
    bool IsDirty);
