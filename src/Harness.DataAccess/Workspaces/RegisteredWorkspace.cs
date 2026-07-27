namespace Harness.DataAccess.Workspaces;

public sealed record RegisteredWorkspace(
    string Id,
    string RootPath,
    string Name,
    string EntryPoint,
    bool IsTrusted,
    bool IsActive,
    string Branch,
    bool IsDirty,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
