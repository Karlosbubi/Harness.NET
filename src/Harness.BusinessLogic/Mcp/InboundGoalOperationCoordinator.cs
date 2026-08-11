using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workflows;

namespace Harness.BusinessLogic.Mcp;

internal enum InboundGoalOperationState
{
    Running,
    Completed,
    Cancelled,
    Failed,
}

internal sealed record InboundGoalOperationId(string Value);

internal sealed record InboundGoalOperationView(
    InboundGoalOperationId Id,
    GoalId GoalId,
    string Kind,
    InboundGoalOperationState State,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    GoalWorkflowSnapshot? Latest,
    string? Error);

internal sealed record InboundGoalOperationResult(
    InboundGoalOperationView? Operation,
    string? ErrorCode,
    string? Error);

internal interface IInboundGoalOperationCoordinator
{
    InboundGoalOperationView? Get(GoalId goalId);

    InboundGoalOperationResult Start(
        GoalId goalId,
        string kind,
        Func<CancellationToken, IAsyncEnumerable<GoalWorkflowSnapshot>> workflow);

    ValueTask<InboundGoalOperationResult> CancelAsync(
        GoalId goalId,
        InboundGoalOperationId operationId,
        CancellationToken cancellationToken = default);
}

internal sealed class InboundGoalOperationCoordinator(TimeProvider timeProvider)
    : IInboundGoalOperationCoordinator, IAsyncDisposable
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, ActiveOperation> operations =
        new(StringComparer.Ordinal);
    private int disposed;

    public InboundGoalOperationView? Get(GoalId goalId)
    {
        if (goalId is null || string.IsNullOrWhiteSpace(goalId.Value))
        {
            return null;
        }

        lock (gate)
        {
            return operations.TryGetValue(goalId.Value, out ActiveOperation? operation)
                ? operation.View()
                : null;
        }
    }

    public InboundGoalOperationResult Start(
        GoalId goalId,
        string kind,
        Func<CancellationToken, IAsyncEnumerable<GoalWorkflowSnapshot>> workflow)
    {
        ArgumentNullException.ThrowIfNull(goalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(workflow);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        ActiveOperation operation;
        lock (gate)
        {
            if (operations.TryGetValue(goalId.Value, out ActiveOperation? current) &&
                current.State is InboundGoalOperationState.Running)
            {
                return new(null, "goal_operation_active",
                    "The goal already has an active inbound operation. Poll harness_goals or " +
                    "cancel that exact operation first.");
            }

            operation = new(
                new(Guid.NewGuid().ToString("N")),
                goalId,
                kind,
                timeProvider.GetUtcNow());
            operations[goalId.Value] = operation;
            operation.Task = RunAsync(operation, workflow);
        }

        return new(operation.View(), ErrorCode: null, Error: null);
    }

    public async ValueTask<InboundGoalOperationResult> CancelAsync(
        GoalId goalId,
        InboundGoalOperationId operationId,
        CancellationToken cancellationToken = default)
    {
        ActiveOperation? operation;
        lock (gate)
        {
            operations.TryGetValue(goalId.Value, out operation);
            if (operation is null || operation.Id != operationId)
            {
                return new(null, "goal_operation_missing",
                    "The active or retained operation does not match the supplied identity.");
            }

            if (operation.State is InboundGoalOperationState.Running)
            {
                operation.Cancellation.Cancel();
            }
        }

        await operation.Task.WaitAsync(cancellationToken);
        return new(operation.View(), ErrorCode: null, Error: null);
    }

    private async Task RunAsync(
        ActiveOperation operation,
        Func<CancellationToken, IAsyncEnumerable<GoalWorkflowSnapshot>> workflow)
    {
        try
        {
            await foreach (GoalWorkflowSnapshot snapshot in
                           workflow(operation.Cancellation.Token)
                               .WithCancellation(operation.Cancellation.Token))
            {
                lock (gate)
                {
                    operation.Latest = snapshot;
                }
            }

            lock (gate)
            {
                operation.State = InboundGoalOperationState.Completed;
                operation.CompletedAt = timeProvider.GetUtcNow();
            }
        }
        catch (OperationCanceledException) when (operation.Cancellation.IsCancellationRequested)
        {
            lock (gate)
            {
                operation.State = InboundGoalOperationState.Cancelled;
                operation.CompletedAt = timeProvider.GetUtcNow();
            }
        }
        catch (Exception exception)
        {
            lock (gate)
            {
                operation.State = InboundGoalOperationState.Failed;
                operation.CompletedAt = timeProvider.GetUtcNow();
                operation.Error = exception.Message;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        ActiveOperation[] active;
        lock (gate)
        {
            active = operations.Values.ToArray();
            foreach (ActiveOperation operation in active.Where(item =>
                         item.State is InboundGoalOperationState.Running))
            {
                operation.Cancellation.Cancel();
            }
        }

        await Task.WhenAll(active.Select(operation => operation.Task));
        foreach (ActiveOperation operation in active)
        {
            operation.Cancellation.Dispose();
        }
    }

    private sealed class ActiveOperation(
        InboundGoalOperationId id,
        GoalId goalId,
        string kind,
        DateTimeOffset startedAt)
    {
        internal InboundGoalOperationId Id { get; } = id;
        internal GoalId GoalId { get; } = goalId;
        internal string Kind { get; } = kind;
        internal DateTimeOffset StartedAt { get; } = startedAt;
        internal CancellationTokenSource Cancellation { get; } = new();
        internal Task Task { get; set; } = Task.CompletedTask;
        internal InboundGoalOperationState State { get; set; } =
            InboundGoalOperationState.Running;
        internal DateTimeOffset? CompletedAt { get; set; }
        internal GoalWorkflowSnapshot? Latest { get; set; }
        internal string? Error { get; set; }

        internal InboundGoalOperationView View() => new(
            Id, GoalId, Kind, State, StartedAt, CompletedAt, Latest, Error);
    }
}
