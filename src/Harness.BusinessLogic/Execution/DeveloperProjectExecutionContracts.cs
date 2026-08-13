using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workspaces;

namespace Harness.BusinessLogic.Execution;

public sealed record DeveloperExecutionId(string Value);
public sealed record DeveloperExecutionOutput(string Value);

public enum DeveloperExecutionState
{
    Running,
    Succeeded,
    Failed,
    Cancelled,
    Interrupted,
}

public sealed record DeveloperExecutionCapabilities(
    bool CanRunProjectEntryPoint,
    bool CanDebugProjectEntryPoint,
    string DebugStatus);

public sealed record DeveloperExecutionView(
    DeveloperExecutionId Id,
    WorkspaceId WorkspaceId,
    GoalId? GoalId,
    string SourceDescription,
    WorkbenchExecutionTarget Target,
    DeveloperExecutionState State,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int? ExitCode,
    long DurationMilliseconds,
    DeveloperExecutionOutput? StandardOutput,
    DeveloperExecutionOutput? StandardError,
    bool IsOutputTruncated,
    bool IsErrorTruncated,
    bool IsOutputAvailable,
    string? ErrorCode,
    string? Error);

public sealed record DeveloperExecutionStartRequest(
    WorkbenchWorkspaceRequest Workspace,
    WorkbenchExecutionTarget Target);

public sealed record DeveloperExecutionStartResult(
    DeveloperExecutionView? Execution,
    string? ErrorCode,
    string? Error);

public sealed record DeveloperExecutionListResult(
    IReadOnlyList<DeveloperExecutionView> Executions,
    bool IsTruncated,
    string? ErrorCode,
    string? Error);

public sealed record DeveloperExecutionCancelResult(
    bool CancellationRequested,
    string? ErrorCode,
    string? Error);

public interface IDeveloperProjectExecutionService
{
    DeveloperExecutionCapabilities Capabilities { get; }

    ValueTask<DeveloperExecutionStartResult> StartRunAsync(
        DeveloperExecutionStartRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperExecutionListResult> ListAsync(
        WorkbenchWorkspaceRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperExecutionCancelResult> CancelAsync(
        DeveloperExecutionId executionId,
        CancellationToken cancellationToken = default);
}
