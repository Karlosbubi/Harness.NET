using Harness.BusinessLogic.Workspaces;

namespace Harness.BusinessLogic.Inspection;

public sealed record WorkbenchGitInspectionResult(
    WorkbenchWorkspaceContext Context,
    WorkspaceGitStateView Git);
