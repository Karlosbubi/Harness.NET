namespace Harness.DataAccess.Execution;

public sealed record StoredDeveloperExecutionId(string Value);
public sealed record StoredDeveloperWorkspaceId(string Value);
public sealed record StoredDeveloperGoalId(string Value);
public sealed record StoredDeveloperSourceDescription(string Value);
public sealed record StoredDeveloperDeclarationId(string Value);

public enum StoredDeveloperExecutionState
{
    Running,
    Succeeded,
    Failed,
    Cancelled,
    Interrupted,
}

public sealed record StoredDeveloperExecution(
    StoredDeveloperExecutionId Id,
    StoredDeveloperWorkspaceId WorkspaceId,
    StoredDeveloperGoalId? GoalId,
    StoredDeveloperSourceDescription SourceDescription,
    DotNetProjectPath ProjectPath,
    DotNetTargetFramework? TargetFramework,
    StoredDeveloperDeclarationId DeclarationId,
    StoredDeveloperExecutionState State,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int? ExitCode,
    long DurationMilliseconds,
    string? ErrorCode,
    string? Error);

public sealed record StoredDeveloperExecutionStart(
    StoredDeveloperExecutionId Id,
    StoredDeveloperWorkspaceId WorkspaceId,
    StoredDeveloperGoalId? GoalId,
    StoredDeveloperSourceDescription SourceDescription,
    DotNetProjectPath ProjectPath,
    DotNetTargetFramework? TargetFramework,
    StoredDeveloperDeclarationId DeclarationId,
    DateTimeOffset StartedAt);

public sealed record StoredDeveloperExecutionCompletion(
    StoredDeveloperExecutionId Id,
    StoredDeveloperExecutionState State,
    DateTimeOffset CompletedAt,
    int? ExitCode,
    long DurationMilliseconds,
    string? ErrorCode,
    string? Error);

public interface IDeveloperDotNetExecutionStore
{
    ValueTask<StoredDeveloperExecution> StartAsync(
        StoredDeveloperExecutionStart execution,
        CancellationToken cancellationToken = default);

    ValueTask CompleteAsync(
        StoredDeveloperExecutionCompletion completion,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<StoredDeveloperExecution>> ListAsync(
        StoredDeveloperWorkspaceId workspaceId,
        StoredDeveloperGoalId? goalId,
        int maximumResults,
        CancellationToken cancellationToken = default);

    ValueTask<int> InterruptRunningAsync(
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);
}
