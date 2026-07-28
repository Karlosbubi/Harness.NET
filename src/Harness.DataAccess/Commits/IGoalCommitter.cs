namespace Harness.DataAccess.Commits;

public interface IGoalCommitter
{
    ValueTask<GoalCommitInspection> InspectAsync(
        GoalCommitInspectionRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<GoalCommitResult> CommitAsync(
        GoalCommitRequest request,
        CancellationToken cancellationToken = default);
}
