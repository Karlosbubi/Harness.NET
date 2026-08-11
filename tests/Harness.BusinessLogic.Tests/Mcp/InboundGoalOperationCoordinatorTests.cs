using System.Runtime.CompilerServices;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Workflows;

namespace Harness.BusinessLogic.Tests.Mcp;

public sealed class InboundGoalOperationCoordinatorTests
{
    [Fact]
    public async Task Starts_without_waiting_and_rejects_a_parallel_operation()
    {
        await using InboundGoalOperationCoordinator coordinator =
            new(TimeProvider.System);
        GoalId goalId = new("goal-a");
        TaskCompletionSource started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        InboundGoalOperationResult first = coordinator.Start(
            goalId, "planning", token => Blocking(started, token));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        InboundGoalOperationResult parallel = coordinator.Start(
            goalId, "resume", Empty);

        Assert.Equal(InboundGoalOperationState.Running, first.Operation?.State);
        Assert.Equal("goal_operation_active", parallel.ErrorCode);
        InboundGoalOperationResult cancelled = await coordinator.CancelAsync(
            goalId, first.Operation!.Id);
        Assert.Equal(InboundGoalOperationState.Cancelled, cancelled.Operation?.State);
    }

    [Fact]
    public async Task Cancellation_requires_the_exact_goal_and_operation_identity()
    {
        await using InboundGoalOperationCoordinator coordinator =
            new(TimeProvider.System);
        GoalId goalId = new("goal-a");
        TaskCompletionSource started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        InboundGoalOperationResult operation = coordinator.Start(
            goalId, "planning", token => Blocking(started, token));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        InboundGoalOperationResult stale = await coordinator.CancelAsync(
            goalId, new("stale"));

        Assert.Equal("goal_operation_missing", stale.ErrorCode);
        Assert.Equal(InboundGoalOperationState.Running, coordinator.Get(goalId)?.State);
        await coordinator.CancelAsync(goalId, operation.Operation!.Id);
    }

    private static async IAsyncEnumerable<GoalWorkflowSnapshot> Blocking(
        TaskCompletionSource started,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        started.SetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        yield break;
    }

    private static async IAsyncEnumerable<GoalWorkflowSnapshot> Empty(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        yield break;
    }
}
