using System.Collections.Immutable;

namespace Harness.DataAccess.Execution;

public sealed record StoredDeveloperExecutionId(string Value);
public sealed record StoredDeveloperWorkspaceId(string Value);
public sealed record StoredDeveloperGoalId(string Value);
public sealed record StoredDeveloperSourceDescription(string Value);
public sealed record StoredDeveloperDeclarationId(string Value);
public sealed record StoredDeveloperTestId(string Value);
public sealed record StoredDeveloperTestName(string Value);
public enum StoredDeveloperTestOutcome
{
    Passed,
    Failed,
    Skipped,
    Other,
}

public sealed record StoredDeveloperTestCaseResult(
    StoredDeveloperTestName FullyQualifiedName,
    StoredDeveloperTestOutcome Outcome,
    long DurationMilliseconds);

public enum StoredDeveloperExecutionOperation
{
    Run,
    HotReload,
    Build,
    Rebuild,
    Test,
}

public enum StoredDeveloperTestScope
{
    Exact,
    Type,
    Project,
    Selection,
}

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
    StoredDeveloperExecutionOperation Operation,
    DotNetProjectPath ProjectPath,
    DotNetTargetFramework? TargetFramework,
    DotNetConfigurationName? Configuration,
    StoredDeveloperDeclarationId? DeclarationId,
    StoredDeveloperExecutionState State,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int? ExitCode,
    long DurationMilliseconds,
    string? ErrorCode,
    string? Error,
    StoredDeveloperTestId? TestId = null,
    StoredDeveloperTestName? TestName = null,
    StoredDeveloperTestScope? TestScope = null,
    ImmutableArray<StoredDeveloperTestName> SelectedTests = default,
    ImmutableArray<StoredDeveloperTestCaseResult> TestCases = default,
    bool AreTestCasesTruncated = false);

public sealed record StoredDeveloperExecutionStart(
    StoredDeveloperExecutionId Id,
    StoredDeveloperWorkspaceId WorkspaceId,
    StoredDeveloperGoalId? GoalId,
    StoredDeveloperSourceDescription SourceDescription,
    StoredDeveloperExecutionOperation Operation,
    DotNetProjectPath ProjectPath,
    DotNetTargetFramework? TargetFramework,
    DotNetConfigurationName? Configuration,
    StoredDeveloperDeclarationId? DeclarationId,
    DateTimeOffset StartedAt,
    StoredDeveloperTestId? TestId = null,
    StoredDeveloperTestName? TestName = null,
    StoredDeveloperTestScope? TestScope = null,
    ImmutableArray<StoredDeveloperTestName> SelectedTests = default);

public sealed record StoredDeveloperExecutionCompletion(
    StoredDeveloperExecutionId Id,
    StoredDeveloperExecutionState State,
    DateTimeOffset CompletedAt,
    int? ExitCode,
    long DurationMilliseconds,
    string? ErrorCode,
    string? Error,
    ImmutableArray<StoredDeveloperTestCaseResult> TestCases = default,
    bool AreTestCasesTruncated = false);

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
