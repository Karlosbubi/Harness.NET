using Harness.BusinessLogic.Goals;

namespace Harness.BusinessLogic.Workspaces;

public sealed record WorkbenchWorkspaceRequest(
    WorkspaceId WorkspaceId,
    GoalId? GoalId);
