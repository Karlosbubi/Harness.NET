using Harness.BusinessLogic.Goals;

namespace Harness.BusinessLogic.Inspection;

internal interface IGoalWorkspaceInspectionService
{
    ValueTask<WorkspaceFileView> ReadFileAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        string relativePath,
        CancellationToken cancellationToken = default);

    ValueTask<WorkspaceTextSearchView> SearchTextAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        string query,
        CancellationToken cancellationToken = default);

    ValueTask<WorkspaceGitStateView> InspectGitAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        CancellationToken cancellationToken = default);

    ValueTask<WorkspaceDotNetInfoView> InspectDotNetAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        CancellationToken cancellationToken = default);
}
