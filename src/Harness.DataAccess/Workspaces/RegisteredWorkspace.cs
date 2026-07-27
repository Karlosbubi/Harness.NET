namespace Harness.DataAccess.Workspaces;

public sealed record RegisteredWorkspace(
    string Id,
    string RootPath,
    string Name,
    string EntryPoint,
    bool IsTrusted,
    string Branch,
    bool IsDirty,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
