using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workflows;

namespace Harness.Presentation.Terminal.Tests;

public sealed class GoalWorkflowTextFormatterTests
{
    [Fact]
    public void Formats_persisted_activity_evidence_and_direction_state()
    {
        GoalWorkflowSnapshot snapshot = new(
            new("run-1"),
            new GoalId("goal-1"),
            GoalWorkflowState.NeedsDirection,
            new(2),
            [new(
                new("task-1"), new(1), new("Bounded slice"),
                new("Implement one outcome."), new("src/Feature"),
                new("- Focused tests pass"), GoalTaskState.Completed,
                new("Verified locally."))],
            [new(1, GoalWorkflowCheckpointKind.UserDirectionRequired,
                WorkflowActor.System, new("Uncertain call was not replayed."))],
            [new(1, new("Recovery notice"), new("Inspect cost evidence."))],
            CanResume: false,
            RequiresUserDirection: true);

        string value = GoalWorkflowTextFormatter.Format(snapshot);

        Assert.Contains("NeedsDirection", value, StringComparison.Ordinal);
        Assert.Contains("user direction required", value, StringComparison.Ordinal);
        Assert.Contains("UserDirectionRequired", value, StringComparison.Ordinal);
        Assert.Contains("Bounded slice", value, StringComparison.Ordinal);
        Assert.Contains("src/Feature", value, StringComparison.Ordinal);
        Assert.Contains("Verified locally.", value, StringComparison.Ordinal);
        Assert.Contains("Inspect cost evidence.", value, StringComparison.Ordinal);
    }
}
