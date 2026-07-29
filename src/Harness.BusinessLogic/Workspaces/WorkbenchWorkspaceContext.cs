using Harness.BusinessLogic.Goals;

namespace Harness.BusinessLogic.Workspaces;

public sealed record WorkbenchWorkspaceContext(
    WorkspaceId WorkspaceId,
    GoalId? GoalId,
    WorkspaceBranchName? Branch,
    WorkbenchWorkspaceScope Scope,
    string Description);
