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

    ValueTask<GoalTreeView> ListTreeAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        string relativeRoot,
        string? glob,
        int maximumDepth,
        int maximumResults,
        string? continuation,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    ValueTask<GoalFileRangeView> ReadRangeAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        string relativePath,
        int startLine,
        int lineCount,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    ValueTask<GoalRegexSearchView> SearchRegexAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        string pattern,
        string? fileGlob,
        int maximumResults,
        string? continuation,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    ValueTask<GoalProjectGraphView> InspectProjectGraphAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
