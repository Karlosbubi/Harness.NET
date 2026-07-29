namespace Harness.BusinessLogic.Workspaces;

internal sealed record WorkbenchWorkspaceResolution(
    WorkbenchWorkspaceContext Context,
    string? RootPath,
    string? ErrorCode,
    string? Error);
